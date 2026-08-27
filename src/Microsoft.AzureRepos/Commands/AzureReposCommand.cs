using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Linq;
using System.Threading.Tasks;
using GitCredentialManager;
using GitCredentialManager.Authentication.Entra;
using GitCredentialManager.Commands;
using Microsoft.AzureRepos.Accounts;

namespace Microsoft.AzureRepos.Commands;

public sealed class AzureReposCommand
{
    private readonly IHostProvider _provider;
    private readonly ICommandContext _context;
    private readonly IEntraAuthentication _authentication;
    private readonly IEntraAccountResolver _accountResolver;
    private readonly IAccountBindingTargetResolver _targetResolver;
    private readonly IAccountBindingManager _bindingManager;
    private readonly IAzureDevOpsAuthorityCache _authorityCache;

    public AzureReposCommand(
        IHostProvider provider,
        ICommandContext context,
        IEntraAuthentication authentication,
        IEntraAccountResolver accountResolver,
        IAccountBindingTargetResolver targetResolver,
        IAccountBindingManager bindingManager,
        IAzureDevOpsAuthorityCache authorityCache)
    {
        EnsureArgument.NotNull(provider, nameof(provider));
        EnsureArgument.NotNull(context, nameof(context));
        EnsureArgument.NotNull(authentication, nameof(authentication));
        EnsureArgument.NotNull(accountResolver, nameof(accountResolver));
        EnsureArgument.NotNull(targetResolver, nameof(targetResolver));
        EnsureArgument.NotNull(bindingManager, nameof(bindingManager));
        EnsureArgument.NotNull(authorityCache, nameof(authorityCache));

        _provider = provider;
        _context = context;
        _authentication = authentication;
        _accountResolver = accountResolver;
        _targetResolver = targetResolver;
        _bindingManager = bindingManager;
        _authorityCache = authorityCache;
    }

    public ProviderCommand Create()
    {
        var root = new ProviderCommand(_provider);
        root.AddAlias("azrepos");
        root.AddAlias("ado");

        var list = new Command("list", "List Microsoft Entra accounts and Azure Repos bindings.");
        var listTenant = new Option<string>(
            ["--tenant", "-t"], "Tenant ID or name to filter bindings to. (optional)");
        var listOrg = new Option<string>(
            ["--org", "-o"], "Azure DevOps organization name to filter bindings to. (optional)");
        var listGlobal = new Option<bool>(
            "--global", "List global bindings. (default)")
        {
            Arity = ArgumentArity.Zero
        };
        var listLocal = new Option<bool>(
            "--local", "List bindings for the current repository.")
        {
            Arity = ArgumentArity.Zero
        };
        list.AddOptionSet(OptionArity.ZeroOrOne, listTenant, listOrg);
        list.AddOptionSet(OptionArity.ZeroOrOne, listGlobal, listLocal);
        list.SetHandler(ListAsync, listTenant, listOrg, listGlobal, listLocal);

        var login = new Command("login", "Sign in to a Microsoft Entra account.");
        var loginTenant = new Option<string>(
            ["--tenant", "-t"], "Tenant ID or name to sign in to. (optional)");
        var loginOrg = new Option<string>(
            ["--org", "-o"], "Azure DevOps organization name to sign in to. (optional)");
        login.AddOptionSet(OptionArity.ZeroOrOne, loginTenant, loginOrg);
        login.SetHandler(LoginAsync, loginTenant, loginOrg);

        var logout = new Command("logout", "Sign out of a Microsoft Entra account.");
        var logoutId = new Option<string>("--id", "Account ID to sign out.");
        var logoutUserName = new Option<string>(
            ["--username", "-u"], "Username (UPN) of the account to sign out.");
        logout.AddOptionSet(OptionArity.ExactlyOne, logoutId, logoutUserName);
        logout.SetHandler(LogoutAsync, logoutId, logoutUserName);

        var set = new Command(
            "set", "Set which Microsoft Entra account Azure Repos should use.");
        var setId = new Option<string>("--id", "Account ID to use.");
        var setUserName = new Option<string>(
            ["--username", "-u"], "Username (UPN) of the account to use.");
        var setNoInherit = new Option<bool>(
            "--no-inherit", "Do not inherit an account binding at this target.")
        {
            Arity = ArgumentArity.Zero
        };
        var setOrg = new Option<string>(
            ["--org", "-o"], "Azure DevOps organization name to bind.");
        var setTenant = new Option<string>(
            ["--tenant", "-t"], "Tenant ID or name to bind.");
        var setGlobal = new Option<bool>(
            "--global", "Set the global binding. (default)")
        {
            Arity = ArgumentArity.Zero
        };
        var setLocal = new Option<bool>(
            "--local", "Set the binding for the current repository.")
        {
            Arity = ArgumentArity.Zero
        };
        set.AddOptionSet(OptionArity.ExactlyOne, setId, setUserName, setNoInherit);
        set.AddOptionSet(OptionArity.ExactlyOne, setOrg, setTenant);
        set.AddOptionSet(OptionArity.ZeroOrOne, setGlobal, setLocal);
        set.AddValidator(result =>
        {
            if (result.FindResultFor(setNoInherit) is not null &&
                result.FindResultFor(setLocal) is null)
            {
                result.ErrorMessage = "--no-inherit requires --local";
            }
        });
        set.SetHandler(
            SetAsync,
            setId,
            setUserName,
            setNoInherit,
            setOrg,
            setTenant,
            setGlobal,
            setLocal);

        var unset = new Command(
            "unset", "Remove an Azure Repos account binding.");
        var unsetOrg = new Option<string>(
            ["--org", "-o"], "Azure DevOps organization name to unbind.");
        var unsetTenant = new Option<string>(
            ["--tenant", "-t"], "Tenant ID or name to unbind.");
        var unsetGlobal = new Option<bool>(
            "--global", "Remove the global binding. (default)")
        {
            Arity = ArgumentArity.Zero
        };
        var unsetLocal = new Option<bool>(
            "--local", "Remove the binding for the current repository.")
        {
            Arity = ArgumentArity.Zero
        };
        unset.AddOptionSet(OptionArity.ExactlyOne, unsetOrg, unsetTenant);
        unset.AddOptionSet(OptionArity.ZeroOrOne, unsetGlobal, unsetLocal);
        unset.SetHandler(
            UnsetAsync, unsetOrg, unsetTenant, unsetGlobal, unsetLocal);

        var show = new Command(
            "show", "Show Azure Repos account binding resolution.");
        var showOrg = new Option<string>(
            ["--org", "-o"], "Azure DevOps organization name to inspect.");
        var showTenant = new Option<string>(
            ["--tenant", "-t"], "Tenant ID or name to inspect.");
        show.AddOptionSet(OptionArity.ExactlyOne, showOrg, showTenant);
        show.SetHandler(ShowAsync, showOrg, showTenant);

        var clearCache = new Command(
            "clear-cache", "Clear the Azure DevOps authority cache.");
        clearCache.SetHandler(ClearCache);

        root.AddCommand(list);
        root.AddCommand(login);
        root.AddCommand(logout);
        root.AddCommand(set);
        root.AddCommand(unset);
        root.AddCommand(show);
        root.AddCommand(clearCache);
        return root;
    }

    internal async Task ListAsync(
        string tenant,
        string organization,
        bool global,
        bool local)
    {
        AccountBindingScope scope = GetScope(local);
        AccountBindingTargetResolution filter = await ResolveOptionalTargetAsync(
            organization, tenant);
        IReadOnlyList<AccountBinding> bindings = _bindingManager.GetAll(scope);
        if (filter is not null)
        {
            bindings = bindings
                .Where(x => x.Target.Equals(filter.Target) ||
                            (filter.Tenant is not null && x.Target.Equals(filter.Tenant)))
                .ToArray();
        }

        IReadOnlyList<IEntraAccount> accounts = await _accountResolver.GetAccountsAsync();
        var matchedBindings = new HashSet<AccountBinding>();
        var accountBindings = new Dictionary<string, List<AccountBinding>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (AccountBinding binding in bindings.Where(x => x.State == AccountBindingState.Bound))
        {
            EntraAccountResolution resolution =
                await _accountResolver.ResolveAsync(binding.Account);
            if (resolution.Status == EntraAccountResolutionStatus.Found)
            {
                matchedBindings.Add(binding);
                string id = resolution.Account.HomeAccountId;
                if (!accountBindings.TryGetValue(id, out List<AccountBinding> list))
                {
                    list = new List<AccountBinding>();
                    accountBindings[id] = list;
                }

                list.Add(binding);
            }
        }

        foreach (IEntraAccount account in accounts
                     .OrderBy(x => x.UserName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(x => x.HomeAccountId, StringComparer.OrdinalIgnoreCase))
        {
            accountBindings.TryGetValue(account.HomeAccountId, out List<AccountBinding> attached);
            string suffix = attached is null
                ? "unbound"
                : string.Join(", ", attached.Select(FormatBindingTarget));
            _context.Console.WriteLine(
                $"{account.UserName} ({account.HomeAccountId}) [{suffix}]");
        }

        foreach (AccountBinding binding in bindings
                     .Where(x => !matchedBindings.Contains(x))
                     .OrderBy(x => x.Target.Kind)
                     .ThenBy(x => x.Target.ToString(), StringComparer.OrdinalIgnoreCase))
        {
            _context.Console.WriteLine(FormatUnresolvedBinding(binding));
        }
    }

    internal async Task LoginAsync(string tenant, string organization)
    {
        AccountBindingTargetResolution target = await ResolveOptionalTargetAsync(
            organization, tenant);
        IEntraAuthenticationResult result =
            await _authentication.GetTokenForUserAsync(
                AzureDevOpsConstants.AzureDevOpsDefaultScopes,
                target?.Authority);
        _context.Console.WriteLine(
            $"Signed in as {result.Account.UserName} ({result.Account.HomeAccountId})");
    }

    internal async Task LogoutAsync(string accountId, string userName)
    {
        EntraAccountResolution resolution = await ResolveAccountAsync(accountId, userName);
        bool removed = await _authentication.RemoveUserAccountAsync(resolution.Account);
        if (!removed)
        {
            throw new InvalidOperationException(
                $"Account '{resolution.Account.HomeAccountId}' was not found in the cache.");
        }

        _context.Console.WriteLine(
            $"Signed out {resolution.Account.UserName} ({resolution.Account.HomeAccountId})");
    }

    internal async Task SetAsync(
        string accountId,
        string userName,
        bool noInherit,
        string organization,
        string tenant,
        bool global,
        bool local)
    {
        AccountBindingTargetResolution target =
            await ResolveRequiredTargetAsync(organization, tenant);

        if (noInherit)
        {
            if (!local)
            {
                throw new InvalidOperationException("--no-inherit requires --local.");
            }

            _bindingManager.SetNoInherit(target.Target);
            _context.Console.WriteLine(
                $"Set local {FormatTarget(target.Target)} binding to no-inherit");
            return;
        }

        EntraAccountResolution resolution = await ResolveAccountAsync(accountId, userName);
        AccountBindingScope scope = GetScope(local);
        _bindingManager.Set(target.Target, scope, resolution.Account);
        _context.Console.WriteLine(
            $"Set {scope.ToString().ToLowerInvariant()} {FormatTarget(target.Target)} binding " +
            $"to {resolution.Account.UserName} ({resolution.Account.HomeAccountId})");
    }

    internal async Task UnsetAsync(
        string organization,
        string tenant,
        bool global,
        bool local)
    {
        AccountBindingTargetResolution target =
            await ResolveRequiredTargetAsync(organization, tenant);
        AccountBindingScope scope = GetScope(local);
        _bindingManager.Unset(target.Target, scope);
        _context.Console.WriteLine(
            $"Removed {scope.ToString().ToLowerInvariant()} {FormatTarget(target.Target)} binding");
    }

    internal async Task ShowAsync(string organization, string tenant)
    {
        AccountBindingTargetResolution target =
            await ResolveRequiredTargetAsync(organization, tenant);

        WriteBinding("global", _bindingManager.Get(target.Target, AccountBindingScope.Global));
        if (_context.Git.IsInsideRepository())
        {
            WriteBinding("local", _bindingManager.Get(target.Target, AccountBindingScope.Local));
        }
        else
        {
            _context.Console.WriteLine("local: unavailable outside a Git repository");
        }

        if (target.Target.Kind == AccountBindingTargetKind.Organization)
        {
            if (target.Tenant is not null)
            {
                WriteBinding(
                    "tenant global",
                    _bindingManager.Get(target.Tenant, AccountBindingScope.Global));
                if (_context.Git.IsInsideRepository())
                {
                    WriteBinding(
                        "tenant local",
                        _bindingManager.Get(target.Tenant, AccountBindingScope.Local));
                }
            }

            AccountBindingResult result =
                _bindingManager.Resolve(target.Target.Organization, target.Tenant?.TenantId);
            _context.Console.WriteLine($"effective: {FormatResult(result)}");
        }
        else
        {
            AccountBinding localBinding = _context.Git.IsInsideRepository()
                ? _bindingManager.Get(target.Target, AccountBindingScope.Local)
                : null;
            AccountBinding globalBinding =
                _bindingManager.Get(target.Target, AccountBindingScope.Global);
            _context.Console.WriteLine(
                $"effective: {FormatTenantResult(localBinding, globalBinding)}");
        }
    }

    internal void ClearCache()
    {
        _authorityCache.Clear();
        _context.Console.WriteLine("Authority cache cleared");
    }

    private async Task<AccountBindingTargetResolution> ResolveOptionalTargetAsync(
        string organization,
        string tenant)
    {
        if (!string.IsNullOrWhiteSpace(organization))
        {
            return await _targetResolver.ResolveOrganizationAsync(organization);
        }

        if (!string.IsNullOrWhiteSpace(tenant))
        {
            AccountBindingTargetResolution result =
                await _targetResolver.ResolveTenantAsync(tenant);
            return result ?? throw new InvalidOperationException(
                $"Unable to resolve Microsoft Entra tenant '{tenant}'.");
        }

        return null;
    }

    private async Task<AccountBindingTargetResolution> ResolveRequiredTargetAsync(
        string organization,
        string tenant)
    {
        return await ResolveOptionalTargetAsync(organization, tenant) ??
               throw new InvalidOperationException(
                   "An Azure DevOps organization or Microsoft Entra tenant is required.");
    }

    private async Task<EntraAccountResolution> ResolveAccountAsync(
        string accountId,
        string userName)
    {
        EntraAccountResolution resolution = !string.IsNullOrWhiteSpace(accountId)
            ? await _accountResolver.ResolveByIdAsync(accountId)
            : await _accountResolver.ResolveByUserNameAsync(userName);

        return resolution.Status switch
        {
            EntraAccountResolutionStatus.Found => resolution,
            EntraAccountResolutionStatus.NotFound => throw new InvalidOperationException(
                "No matching Microsoft Entra account was found."),
            EntraAccountResolutionStatus.Ambiguous => throw new InvalidOperationException(
                "More than one Microsoft Entra account matches that username; use --id."),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private void WriteBinding(string label, AccountBinding binding)
    {
        _context.Console.WriteLine(
            binding is null ? $"{label}: none" : $"{label}: {FormatBinding(binding)}");
    }

    private static string FormatBinding(AccountBinding binding)
    {
        return binding.State switch
        {
            AccountBindingState.Bound =>
                $"{binding.Account.UserName} ({binding.Account.HomeAccountId ?? "legacy username"})",
            AccountBindingState.NoInherit => "no-inherit",
            AccountBindingState.Invalid => $"invalid: {binding.InvalidReason}",
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private static string FormatResult(AccountBindingResult result)
    {
        if (result.SelectedBinding is not null)
        {
            return $"{FormatBindingTarget(result.SelectedBinding)} " +
                   $"{FormatBinding(result.SelectedBinding)}";
        }

        if (result.SuppressingBinding is not null)
        {
            return $"{FormatBindingTarget(result.SuppressingBinding)} no-inherit";
        }

        return "none";
    }

    private static string FormatTenantResult(
        AccountBinding localBinding,
        AccountBinding globalBinding)
    {
        if (localBinding?.State == AccountBindingState.Bound)
        {
            return $"local {FormatBinding(localBinding)}";
        }

        if (localBinding?.State == AccountBindingState.NoInherit)
        {
            return "local no-inherit";
        }

        return globalBinding?.State == AccountBindingState.Bound
            ? $"global {FormatBinding(globalBinding)}"
            : "none";
    }

    private static string FormatUnresolvedBinding(AccountBinding binding)
    {
        string target = FormatBindingTarget(binding);
        return binding.State switch
        {
            AccountBindingState.Bound =>
                $"{binding.Account.UserName ?? "(unknown)"} " +
                $"({binding.Account.HomeAccountId ?? "legacy username"}) " +
                $"[{target}; unresolved]",
            AccountBindingState.NoInherit => $"[{target}; no-inherit]",
            AccountBindingState.Invalid =>
                $"[{target}; invalid: {binding.InvalidReason}]",
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private static string FormatBindingTarget(AccountBinding binding) =>
        $"{binding.Scope.ToString().ToLowerInvariant()} {FormatTarget(binding.Target)}";

    private static string FormatTarget(AccountBindingTarget target) =>
        $"{target.Kind.ToString().ToLowerInvariant()}:{target}";

    private static AccountBindingScope GetScope(bool local) =>
        local ? AccountBindingScope.Local : AccountBindingScope.Global;
}
