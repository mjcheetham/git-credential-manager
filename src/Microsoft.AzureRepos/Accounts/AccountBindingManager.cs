using System;
using System.Collections.Generic;
using System.Linq;
using GitCredentialManager;
using GitCredentialManager.Authentication.Entra;

namespace Microsoft.AzureRepos.Accounts;

public sealed class AccountBindingManager : IAccountBindingManager
{
    private readonly ITrace _trace;
    private readonly IGit _git;

    public AccountBindingManager(ICommandContext context)
        : this(GetTrace(context), context.Git) { }

    public AccountBindingManager(ITrace trace, IGit git)
    {
        EnsureArgument.NotNull(trace, nameof(trace));
        EnsureArgument.NotNull(git, nameof(git));

        _trace = trace;
        _git = git;
    }

    public AccountBinding Get(AccountBindingTarget target, AccountBindingScope scope)
    {
        EnsureArgument.NotNull(target, nameof(target));
        EnsureLocalAvailable(scope);

        return GetAllCore(scope, target).SingleOrDefault();
    }

    public AccountBindingResult Resolve(string organization, Guid? tenantId = null)
    {
        AccountBindingTarget organizationTarget =
            AccountBindingTarget.ForOrganization(organization);
        var invalid = new List<AccountBinding>();

        if (_git.IsInsideRepository())
        {
            AccountBindingResult result = Inspect(
                GetAllCore(AccountBindingScope.Local, organizationTarget).SingleOrDefault(),
                invalid);
            if (result is not null)
            {
                return result;
            }
        }

        AccountBindingResult globalOrganization = Inspect(
            GetAllCore(AccountBindingScope.Global, organizationTarget).SingleOrDefault(),
            invalid);
        if (globalOrganization is not null)
        {
            return globalOrganization;
        }

        if (tenantId is null || tenantId == Guid.Empty)
        {
            return new AccountBindingResult(invalidBindings: invalid);
        }

        AccountBindingTarget tenantTarget = AccountBindingTarget.ForTenant(tenantId.Value);
        if (_git.IsInsideRepository())
        {
            AccountBindingResult result = Inspect(
                GetAllCore(AccountBindingScope.Local, tenantTarget).SingleOrDefault(),
                invalid);
            if (result is not null)
            {
                return result;
            }
        }

        AccountBindingResult globalTenant = Inspect(
            GetAllCore(AccountBindingScope.Global, tenantTarget).SingleOrDefault(),
            invalid);

        return globalTenant ?? new AccountBindingResult(invalidBindings: invalid);
    }

    public IReadOnlyList<AccountBinding> GetAll(
        AccountBindingScope scope,
        AccountBindingTarget filter = null)
    {
        EnsureLocalAvailable(scope);
        return GetAllCore(scope, filter);
    }

    public void Set(
        AccountBindingTarget target,
        AccountBindingScope scope,
        IEntraAccount account)
    {
        EnsureArgument.NotNull(target, nameof(target));
        EnsureArgument.NotNull(account, nameof(account));
        EnsureArgument.NotNullOrWhiteSpace(account.HomeAccountId, nameof(account.HomeAccountId));
        EnsureArgument.NotNullOrWhiteSpace(account.UserName, nameof(account.UserName));
        EnsureLocalAvailable(scope);

        string accountIdKey =
            AccountBindingConfiguration.GetKey(target, AccountBindingProperty.AccountId);
        string userNameKey =
            AccountBindingConfiguration.GetKey(target, AccountBindingProperty.UserName);
        var desired = new Dictionary<string, string>(GitConfigurationKeyComparer.Instance)
        {
            [accountIdKey] = account.HomeAccountId,
            [userNameKey] = account.UserName
        };

        IReadOnlyList<GitConfigurationEntry> entries = GetEntries(scope, target);
        if (Matches(entries, desired))
        {
            return;
        }

        _trace.WriteLine(
            $"Setting {scope.ToString().ToLowerInvariant()} account binding for {target.Kind.ToString().ToLowerInvariant()} '{target}'...");

        RemoveEntries(scope, entries);
        IGitConfiguration config = _git.GetConfiguration();
        GitConfigurationLevel level = GetConfigurationLevel(scope);
        config.Set(level, accountIdKey, account.HomeAccountId);
        config.Set(level, userNameKey, account.UserName);
    }

    public void SetNoInherit(AccountBindingTarget target)
    {
        EnsureArgument.NotNull(target, nameof(target));
        EnsureLocalAvailable(AccountBindingScope.Local);

        string userNameKey =
            AccountBindingConfiguration.GetKey(target, AccountBindingProperty.UserName);
        var desired = new Dictionary<string, string>(GitConfigurationKeyComparer.Instance)
        {
            [userNameKey] = string.Empty
        };

        IReadOnlyList<GitConfigurationEntry> entries =
            GetEntries(AccountBindingScope.Local, target);
        if (Matches(entries, desired))
        {
            return;
        }

        _trace.WriteLine(
            $"Setting local account binding to no-inherit for {target.Kind.ToString().ToLowerInvariant()} '{target}'...");

        RemoveEntries(AccountBindingScope.Local, entries);
        _git.GetConfiguration().Set(
            GitConfigurationLevel.Local, userNameKey, string.Empty);
    }

    public void Unset(AccountBindingTarget target, AccountBindingScope scope)
    {
        EnsureArgument.NotNull(target, nameof(target));
        EnsureLocalAvailable(scope);

        IReadOnlyList<GitConfigurationEntry> entries = GetEntries(scope, target);
        if (entries.Count == 0)
        {
            return;
        }

        _trace.WriteLine(
            $"Removing {scope.ToString().ToLowerInvariant()} account binding for {target.Kind.ToString().ToLowerInvariant()} '{target}'...");
        RemoveEntries(scope, entries);
    }

    private IReadOnlyList<AccountBinding> GetAllCore(
        AccountBindingScope scope,
        AccountBindingTarget filter)
    {
        var variants = new Dictionary<string, RawBinding>(StringComparer.Ordinal);
        GitConfigurationLevel level = GetConfigurationLevel(scope);

        _git.GetConfiguration().Enumerate(level, entry =>
        {
            if (AccountBindingConfiguration.TryParseKey(
                    entry.Key, out AccountBindingTarget target, out AccountBindingProperty property) &&
                (filter is null || filter.Equals(target)))
            {
                string identity = GetStorageIdentity(target);
                if (!variants.TryGetValue(identity, out RawBinding raw))
                {
                    raw = new RawBinding(target);
                    variants[identity] = raw;
                }

                raw.Add(property, entry.Value);
            }

            return true;
        });

        var logical = new Dictionary<AccountBindingTarget, List<AccountBinding>>();
        foreach (RawBinding raw in variants.Values)
        {
            AccountBinding binding = raw.ToBinding(scope);
            if (!logical.TryGetValue(binding.Target, out List<AccountBinding> records))
            {
                records = new List<AccountBinding>();
                logical[binding.Target] = records;
            }

            records.Add(binding);
        }

        return logical
            .Select(x => MergeVariants(x.Key, scope, x.Value))
            .OrderBy(x => x.Target.Kind)
            .ThenBy(x => x.Target.ToString(), StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IReadOnlyList<GitConfigurationEntry> GetEntries(
        AccountBindingScope scope,
        AccountBindingTarget target)
    {
        var entries = new List<GitConfigurationEntry>();
        _git.GetConfiguration().Enumerate(GetConfigurationLevel(scope), entry =>
        {
            if (AccountBindingConfiguration.TryParseKey(
                    entry.Key, out AccountBindingTarget entryTarget, out _) &&
                target.Equals(entryTarget))
            {
                entries.Add(entry);
            }

            return true;
        });

        return entries;
    }

    private void RemoveEntries(
        AccountBindingScope scope,
        IEnumerable<GitConfigurationEntry> entries)
    {
        IGitConfiguration config = _git.GetConfiguration();
        GitConfigurationLevel level = GetConfigurationLevel(scope);

        foreach (string key in entries
                     .Select(x => x.Key)
                     .Distinct(GitConfigurationKeyComparer.Instance))
        {
            config.UnsetAll(level, key, ".*");
        }
    }

    private void EnsureLocalAvailable(AccountBindingScope scope)
    {
        if (scope == AccountBindingScope.Local && !_git.IsInsideRepository())
        {
            throw new InvalidOperationException(
                "Local account bindings require a Git repository.");
        }
    }

    private static AccountBindingResult Inspect(
        AccountBinding binding,
        ICollection<AccountBinding> invalid)
    {
        if (binding is null)
        {
            return null;
        }

        switch (binding.State)
        {
            case AccountBindingState.Bound:
                return new AccountBindingResult(
                    selectedBinding: binding, invalidBindings: invalid);
            case AccountBindingState.NoInherit:
                return new AccountBindingResult(
                    suppressingBinding: binding, invalidBindings: invalid);
            case AccountBindingState.Invalid:
                invalid.Add(binding);
                return null;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private static AccountBinding MergeVariants(
        AccountBindingTarget target,
        AccountBindingScope scope,
        IReadOnlyList<AccountBinding> records)
    {
        AccountBindingTarget canonicalTarget = target.Kind == AccountBindingTargetKind.Organization
            ? AccountBindingTarget.ForOrganization(target.Organization.ToLowerInvariant())
            : target;

        AccountBinding first = records[0];
        if (records.Skip(1).All(x => AreEquivalent(first, x)))
        {
            return Retarget(first, canonicalTarget);
        }

        return AccountBinding.Invalid(
            canonicalTarget,
            scope,
            string.Join(" | ", records.Select(x => x.RawAccountId)),
            string.Join(" | ", records.Select(x => x.RawUserName)),
            "Conflicting configuration records exist for this target.");
    }

    private static AccountBinding Retarget(
        AccountBinding binding,
        AccountBindingTarget target)
    {
        return binding.State switch
        {
            AccountBindingState.Bound =>
                AccountBinding.Bound(target, binding.Scope, binding.Account),
            AccountBindingState.NoInherit => AccountBinding.NoInherit(target),
            AccountBindingState.Invalid => AccountBinding.Invalid(
                target,
                binding.Scope,
                binding.RawAccountId,
                binding.RawUserName,
                binding.InvalidReason),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private static bool AreEquivalent(AccountBinding first, AccountBinding second)
    {
        if (first.State != second.State)
        {
            return false;
        }

        return first.State switch
        {
            AccountBindingState.Bound => first.Account.Equals(second.Account),
            AccountBindingState.NoInherit => true,
            AccountBindingState.Invalid =>
                StringComparer.Ordinal.Equals(first.RawAccountId, second.RawAccountId) &&
                StringComparer.Ordinal.Equals(first.RawUserName, second.RawUserName) &&
                StringComparer.Ordinal.Equals(first.InvalidReason, second.InvalidReason),
            _ => false
        };
    }

    private static bool Matches(
        IReadOnlyList<GitConfigurationEntry> entries,
        IReadOnlyDictionary<string, string> desired)
    {
        if (entries.Count != desired.Count)
        {
            return false;
        }

        foreach (GitConfigurationEntry entry in entries)
        {
            if (!desired.TryGetValue(entry.Key, out string value) ||
                !StringComparer.Ordinal.Equals(entry.Value, value))
            {
                return false;
            }
        }

        return true;
    }

    private static string GetStorageIdentity(AccountBindingTarget target)
    {
        return target.Kind == AccountBindingTargetKind.Organization
            ? $"organization:{target.Organization}"
            : $"tenant:{target.TenantId:D}";
    }

    private static GitConfigurationLevel GetConfigurationLevel(AccountBindingScope scope)
    {
        return scope switch
        {
            AccountBindingScope.Global => GitConfigurationLevel.Global,
            AccountBindingScope.Local => GitConfigurationLevel.Local,
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unsupported binding scope.")
        };
    }

    private static ITrace GetTrace(ICommandContext context)
    {
        EnsureArgument.NotNull(context, nameof(context));
        return context.Trace;
    }

    private sealed class RawBinding
    {
        private readonly List<string> _accountIds = new();
        private readonly List<string> _userNames = new();

        public RawBinding(AccountBindingTarget target)
        {
            Target = target;
        }

        public AccountBindingTarget Target { get; }

        public void Add(AccountBindingProperty property, string value)
        {
            switch (property)
            {
                case AccountBindingProperty.AccountId:
                    _accountIds.Add(value);
                    break;
                case AccountBindingProperty.UserName:
                    _userNames.Add(value);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(property), property, null);
            }
        }

        public AccountBinding ToBinding(AccountBindingScope scope)
        {
            string rawAccountId = _accountIds.FirstOrDefault();
            string rawUserName = _userNames.FirstOrDefault();

            if (_accountIds.Count > 1 || _userNames.Count > 1)
            {
                return AccountBinding.Invalid(
                    Target, scope, rawAccountId, rawUserName,
                    "Binding properties must not contain multiple values.");
            }

            if (_userNames.Count == 0)
            {
                return AccountBinding.Invalid(
                    Target, scope, rawAccountId, null,
                    "Binding has an account ID but no username.");
            }

            if (rawUserName.Length == 0)
            {
                if (scope == AccountBindingScope.Local && _accountIds.Count == 0)
                {
                    return AccountBinding.NoInherit(Target);
                }

                return AccountBinding.Invalid(
                    Target, scope, rawAccountId, rawUserName,
                    "An empty username is valid only for a local no-inherit binding without an account ID.");
            }

            if (string.IsNullOrWhiteSpace(rawUserName))
            {
                return AccountBinding.Invalid(
                    Target, scope, rawAccountId, rawUserName,
                    "Binding username must not be whitespace.");
            }

            if (_accountIds.Count == 0)
            {
                return AccountBinding.Bound(
                    Target, scope, new AccountReference(null, rawUserName));
            }

            if (string.IsNullOrWhiteSpace(rawAccountId))
            {
                return AccountBinding.Invalid(
                    Target, scope, rawAccountId, rawUserName,
                    "Binding account ID must not be empty.");
            }

            return AccountBinding.Bound(
                Target, scope, new AccountReference(rawAccountId, rawUserName));
        }
    }
}
