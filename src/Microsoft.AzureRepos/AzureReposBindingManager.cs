using System;
using System.Collections.Generic;
using System.Globalization;
using GitCredentialManager;
using GitCredentialManager.Authentication;
using GitCredentialManager.Authentication.Entra;

namespace Microsoft.AzureRepos;

/// <summary>
/// Manages bindings between Microsoft accounts and Azure DevOps tenants or organizations
/// for the Azure Repos provider.
/// </summary>
/// <remarks>
/// Bindings are pure preferences ("for this scope, prefer this account") and have no
/// authentication state. They sit on top of the MSAL account cache (the source of truth
/// for "which accounts do we have credentials for") and are resolved most-specific-first
/// at credential time.
/// </remarks>
public interface IAzureReposBindingManager
{
    /// <summary>
    /// Set the binding for the given scope to the given account.
    /// </summary>
    /// <param name="scope">Scope at which to bind.</param>
    /// <param name="account">Account to bind. At least one of
    /// <see cref="IEntraAccount.HomeAccountId"/> or
    /// <see cref="IEntraAccount.UserName"/> must be populated; both is preferred so the
    /// binding survives independently of the MSAL cache being able to resolve one to the
    /// other.</param>
    void Bind(AzureReposBindingScope scope, IEntraAccount account);

    /// <summary>
    /// Remove the binding at the given scope, if any.
    /// </summary>
    void Unbind(AzureReposBindingScope scope);

    /// <summary>
    /// Read the account currently bound at the given scope, or <see langword="null"/> if none.
    /// </summary>
    /// <remarks>
    /// The returned account carries whichever of <c>HomeAccountId</c> and <c>UserName</c> the
    /// stored binding has on disk. New bindings always store both; legacy bindings written by
    /// earlier releases stored only <c>UserName</c>.
    /// </remarks>
    IEntraAccount GetAccount(AzureReposBindingScope scope);

    /// <summary>
    /// Enumerate every binding the manager knows about. Local-level entries are only enumerated
    /// when inside a git repository.
    /// </summary>
    IEnumerable<AzureReposBinding> GetBindings();
}

/// <summary>
/// A single stored binding: which scope it applies to and which account it points at.
/// </summary>
public sealed record AzureReposBinding(AzureReposBindingScope Scope, IEntraAccount Account);

public class AzureReposBindingManager : IAzureReposBindingManager
{
    private const string AccountIdProperty = "accountid";
    private const string UserNameProperty = "username";

    private readonly ITrace _trace;
    private readonly IGit _git;

    public AzureReposBindingManager(ICommandContext context) : this(context.Trace, context.Git) { }

    public AzureReposBindingManager(ITrace trace, IGit git)
    {
        EnsureArgument.NotNull(trace, nameof(trace));
        EnsureArgument.NotNull(git, nameof(git));

        _trace = trace;
        _git = git;
    }

    public void Bind(AzureReposBindingScope scope, IEntraAccount account)
    {
        EnsureArgument.NotNull(scope, nameof(scope));
        EnsureArgument.NotNull(account, nameof(account));
        if (string.IsNullOrWhiteSpace(account.HomeAccountId) &&
            string.IsNullOrWhiteSpace(account.UserName))
        {
            throw new ArgumentException(
                "Account must have at least one of HomeAccountId or UserName populated.",
                nameof(account));
        }

        GitConfigurationLevel level = GetConfigLevel(scope);
        if (level == GitConfigurationLevel.Local && !_git.IsInsideRepository())
        {
            _trace.WriteLine("Cannot record local-scoped binding - not inside a repository.");
            return;
        }

        string keyPrefix = GetKeyPrefix(scope);
        _trace.WriteLine(
            $"Recording binding for scope '{keyPrefix}' at {level} " +
            $"(accountid='{account.HomeAccountId}', username='{account.UserName}').");

        IGitConfiguration config = _git.GetConfiguration();
        WriteOrClear(config, level, $"{keyPrefix}.{AccountIdProperty}", account.HomeAccountId);
        WriteOrClear(config, level, $"{keyPrefix}.{UserNameProperty}", account.UserName);
    }

    public void Unbind(AzureReposBindingScope scope)
    {
        EnsureArgument.NotNull(scope, nameof(scope));

        GitConfigurationLevel level = GetConfigLevel(scope);
        if (level == GitConfigurationLevel.Local && !_git.IsInsideRepository())
        {
            _trace.WriteLine("Cannot remove local-scoped binding - not inside a repository.");
            return;
        }

        string keyPrefix = GetKeyPrefix(scope);
        _trace.WriteLine($"Removing binding for scope '{keyPrefix}' at {level}.");

        IGitConfiguration config = _git.GetConfiguration();
        config.Unset(level, $"{keyPrefix}.{AccountIdProperty}");
        config.Unset(level, $"{keyPrefix}.{UserNameProperty}");
    }

    public IEntraAccount GetAccount(AzureReposBindingScope scope)
    {
        EnsureArgument.NotNull(scope, nameof(scope));

        GitConfigurationLevel level = GetConfigLevel(scope);
        if (level == GitConfigurationLevel.Local && !_git.IsInsideRepository())
        {
            return null;
        }

        string keyPrefix = GetKeyPrefix(scope);
        IGitConfiguration config = _git.GetConfiguration();

        string accountId = TryGet(config, level, $"{keyPrefix}.{AccountIdProperty}");
        string userName  = TryGet(config, level, $"{keyPrefix}.{UserNameProperty}");

        if (accountId is null && userName is null) return null;
        return new EntraAccount(accountId, userName);
    }

    public IEnumerable<AzureReposBinding> GetBindings()
    {
        IGitConfiguration config = _git.GetConfiguration();

        foreach (AzureReposBinding b in EnumerateBindings(config, GitConfigurationLevel.Global, isLocal: false))
            yield return b;

        if (_git.IsInsideRepository())
        {
            foreach (AzureReposBinding b in EnumerateBindings(config, GitConfigurationLevel.Local, isLocal: true))
                yield return b;
        }
    }

    private static IEnumerable<AzureReposBinding> EnumerateBindings(
        IGitConfiguration config, GitConfigurationLevel level, bool isLocal)
    {
        var accountIds = new Dictionary<AzureReposBindingScope, string>();
        var userNames  = new Dictionary<AzureReposBindingScope, string>();

        config.Enumerate(level, Constants.GitConfiguration.Credential.SectionName,
            AccountIdProperty, entry =>
        {
            AzureReposBindingScope scope = ParseScopeFromKey(entry.Key, isLocal);
            if (scope is not null) accountIds[scope] = entry.Value;
            return true;
        });

        config.Enumerate(level, Constants.GitConfiguration.Credential.SectionName,
            UserNameProperty, entry =>
        {
            AzureReposBindingScope scope = ParseScopeFromKey(entry.Key, isLocal);
            if (scope is not null) userNames[scope] = entry.Value;
            return true;
        });

        var scopes = new HashSet<AzureReposBindingScope>(accountIds.Keys);
        scopes.UnionWith(userNames.Keys);

        var bindings = new List<AzureReposBinding>(scopes.Count);
        foreach (AzureReposBindingScope scope in scopes)
        {
            accountIds.TryGetValue(scope, out string accountId);
            userNames.TryGetValue(scope, out string userName);
            bindings.Add(new AzureReposBinding(scope, new EntraAccount(accountId, userName)));
        }
        return bindings;
    }

    private static AzureReposBindingScope ParseScopeFromKey(string key, bool isLocal)
    {
        if (!GitConfigurationKeyComparer.TrySplit(key, out _, out string subsection, out _))
            return null;

        if (!Uri.TryCreate(subsection, UriKind.Absolute, out Uri uri))
            return null;
        if (!StringComparer.OrdinalIgnoreCase.Equals(uri.Scheme, AzureDevOpsConstants.UrnScheme))
            return null;

        string path = uri.AbsolutePath;
        if (path.StartsWith(AzureDevOpsConstants.UrnOrgPrefix + "/", StringComparison.OrdinalIgnoreCase))
        {
            return AzureReposBindingScope.ForOrg(path.Substring(AzureDevOpsConstants.UrnOrgPrefix.Length + 1), isLocal);
        }
        if (path.StartsWith(AzureDevOpsConstants.UrnTenantPrefix + "/", StringComparison.OrdinalIgnoreCase))
        {
            return AzureReposBindingScope.ForTenant(path.Substring(AzureDevOpsConstants.UrnTenantPrefix.Length + 1), isLocal);
        }
        return null;
    }

    private static GitConfigurationLevel GetConfigLevel(AzureReposBindingScope scope) =>
        scope.IsLocal ? GitConfigurationLevel.Local : GitConfigurationLevel.Global;

    private static string GetKeyPrefix(AzureReposBindingScope scope)
    {
        (string k, string v) = scope switch
        {
            AzureReposBindingScope.Tenant t => (AzureDevOpsConstants.UrnTenantPrefix, t.TenantId),
            AzureReposBindingScope.Org o    => (AzureDevOpsConstants.UrnOrgPrefix, o.OrgName),
            _ => throw new ArgumentException("Unknown scope", nameof(scope))
        };

        return string.Format(
            CultureInfo.InvariantCulture, "{0}.{1}:{2}/{3}",
            Constants.GitConfiguration.Credential.SectionName,
            AzureDevOpsConstants.UrnScheme, k, v
        );
    }

    private static void WriteOrClear(IGitConfiguration config, GitConfigurationLevel level, string key, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            config.Unset(level, key);
        else
            config.Set(level, key, value);
    }

    private static string TryGet(IGitConfiguration config, GitConfigurationLevel level, string key) =>
        config.TryGet(level, GitConfigurationType.Raw, key, out string value) ? value : null;
}

public static class AzureReposBindingManagerExtensions
{
    /// <summary>
    /// Resolve the account to use for a credential request against the given Azure DevOps
    /// organization and/or Microsoft Entra tenant.
    /// </summary>
    /// <remarks>
    /// Precedence (most specific to least): Org local, Org global, Tenant local, Tenant global.
    /// Tenant lookup is skipped when <paramref name="tenantId"/> is <see langword="null"/>
    /// (caller couldn't resolve the org's tenant).
    /// </remarks>
    public static IEntraAccount ResolveAccountBinding(this IAzureReposBindingManager manager, string orgName, string tenantId)
    {
        EnsureArgument.NotNull(manager, nameof(manager));
        EnsureArgument.NotNullOrWhiteSpace(orgName, nameof(orgName));

        // Local binding for an org
        IEntraAccount account = manager.GetAccount(AzureReposBindingScope.ForOrg(orgName, isLocal: true));
        if (account is not null) return account;

        // Global binding for an org
        account = manager.GetAccount(AzureReposBindingScope.ForOrg(orgName, isLocal: false));
        if (account is not null) return account;

        // Look for a less scoped tenant-binding
        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            // Local binding for a tenant
            account = manager.GetAccount(AzureReposBindingScope.ForTenant(tenantId, isLocal: true));
            if (account is not null) return account;

            // Global binding for a tenant
            account = manager.GetAccount(AzureReposBindingScope.ForTenant(tenantId, isLocal: false));
            if (account is not null) return account;
        }

        // No binding available!
        return null;
    }
}
