using System.CommandLine;
using GitCredentialManager;
using GitCredentialManager.Commands;

namespace Microsoft.AzureRepos;

public partial class AzureReposHostProvider
{
    ProviderCommand ICommandProvider.CreateCommand()
    {
        //
        // clear-cache
        //
        var clearCacheCmd = new Command("clear-cache", "Clear the Azure DevOps authority cache");
        clearCacheCmd.SetHandler(ClearCacheCmd);

        //
        // list [--org <org> | --tenant <tenant>]
        //     [--local | --global]
        //
        var listCmd = new Command("list", "List all Microsoft Entra accounts in the cache.");
        var listTenantOpt = new Option<string>(["--tenant", "-t"], "Tenant ID or name to filter results to. (optional)");
        var listOrgOpt = new Option<string>(["--org", "-o"], "Azure DevOps organization name to filter results to. (optional)");
        listCmd.AddOptionSet(OptionArity.ZeroOrOne, listTenantOpt, listOrgOpt);
        var listGlobalOpt = new Option<bool>("--global", "List accounts for all repositories. (default)");
        var listLocalOpt = new Option<bool>("--local", "List accounts for the current repository only.");
        listCmd.AddOptionSet(OptionArity.ZeroOrOne, listGlobalOpt, listLocalOpt);
        listCmd.SetHandler(ListCmd, listTenantOpt, listOrgOpt, listGlobalOpt, listLocalOpt);

        //
        // login [--org <org> | --tenant <tenant>]
        //     [--local | --global]
        //
        var loginCmd = new Command("login", "Sign in to a Microsoft Entra account.");
        var loginTenantOpt = new Option<string>(["--tenant", "-t"], "Tenant ID or name to use with this account.");
        var loginOrgOpt = new Option<string>(["--org", "-o"], "Azure DevOps organization name to use with this account.");
        loginCmd.AddOptionSet(OptionArity.ZeroOrOne, loginTenantOpt, loginOrgOpt);
        var loginGlobalOpt = new Option<bool>("--global", "Sign in for all repositories. (default)");
        var loginLocalOpt = new Option<bool>("--local", "Sign in for the current repository only.");
        loginCmd.AddOptionSet(OptionArity.ZeroOrOne, loginGlobalOpt, loginLocalOpt);
        loginCmd.SetHandler(LoginCmd, loginTenantOpt, loginOrgOpt, loginGlobalOpt, loginLocalOpt);

        //
        // logout --tenant <tenant> (--id <account-id> | --username <username>)
        //     [--local | --global]
        // logout --org <org> (--id <account-id> | --username <username>)
        //     [--local | --global]
        //
        var logoutCmd = new Command("logout", "Sign out of a Microsoft Entra account.");
        var logoutIdOpt = new Option<string>("--id", "Account ID to sign out.");
        var logoutUserNameOpt = new Option<string>(["--username", "-u"], "Username (UPN) of the account to sign out.");
        var logoutOrgOpt = new Option<string>(["--org", "-o"], "Azure DevOps organization name to sign out from.");
        var logoutTenantOpt = new Option<string>(["--tenant", "-t"], "Tenant ID or name to sign out from.");
        var logoutGlobalOpt = new Option<bool>("--global", "Sign out for all repositories. (default)");
        var logoutLocalOpt = new Option<bool>("--local", "Sign out for the current repository only.");
        logoutCmd.AddOptionSet(OptionArity.ExactlyOne, logoutIdOpt, logoutUserNameOpt);
        logoutCmd.AddOptionSet(OptionArity.ExactlyOne, logoutOrgOpt, logoutTenantOpt);
        logoutCmd.AddOptionSet(OptionArity.ZeroOrOne, logoutGlobalOpt, logoutLocalOpt);
        logoutCmd.SetHandler(
            LogoutCmd, logoutIdOpt, logoutUserNameOpt, logoutOrgOpt, logoutTenantOpt,
            logoutGlobalOpt, logoutLocalOpt);

        //
        // set --tenant <tenant>  (--id <account-id> | --username <username>)
        //     [--local | --global]
        // set --org <org> (--id <account-id> | --username <username>)
        //     [--local | --global]
        //
        var setCmd = new Command("set", "Set which Microsoft Entra account should be used for Azure Repos authentication.");
        var setIdOpt = new Option<string>("--id", "Account ID to use.");
        var setUserNameOpt = new Option<string>(["--username", "-u"], "Username (UPN) of the account to use.");
        var setOrgOpt = new Option<string>(["--org", "-o"], "Azure DevOps organization name to use an account with.");
        var setTenantOpt = new Option<string>(["--tenant", "-t"], "Tenant ID or name to use an account with.");
        var setGlobalOpt = new Option<bool>("--global", "Set the account for all repositories. (default)");
        var setLocalOpt = new Option<bool>("--local", "Set the account for the current repository only.");
        setCmd.AddOptionSet(OptionArity.ExactlyOne, setIdOpt, setUserNameOpt);
        setCmd.AddOptionSet(OptionArity.ExactlyOne, setOrgOpt, setTenantOpt);
        setCmd.AddOptionSet(OptionArity.ZeroOrOne, setGlobalOpt, setLocalOpt);
        setCmd.SetHandler(
            SetCmd, setIdOpt, setUserNameOpt, setOrgOpt, setTenantOpt,
            setGlobalOpt, setLocalOpt);

        //
        // unset --tenant <tenant>
        //     [--local | --global]
        // unset --org <org>
        //     [--local | --global]
        //
        var unsetCmd = new Command("unset", "Forget a Microsoft Entra account for Azure Repos authentication.");
        var unsetOrgOpt = new Option<string>(["--org", "-o"], "Azure DevOps organization name to forget an account with.");
        var unsetTenantOpt = new Option<string>(["--tenant", "-t"], "Tenant ID or name to forget an account with.");
        var unsetGlobalOpt = new Option<bool>("--global", "Forget the account for all repositories. (default)");
        var unsetLocalOpt = new Option<bool>("--local", "Forget the account for the current repository only.");
        unsetCmd.AddOptionSet(OptionArity.ExactlyOne, unsetOrgOpt, unsetTenantOpt);
        unsetCmd.AddOptionSet(OptionArity.ZeroOrOne, unsetGlobalOpt, unsetLocalOpt);
        unsetCmd.SetHandler(UnsetCmd, unsetOrgOpt, unsetTenantOpt, unsetGlobalOpt, unsetLocalOpt);

        var rootCmd = new ProviderCommand(this);
        rootCmd.AddAlias("azrepos");
        rootCmd.AddAlias("ado");
        rootCmd.AddCommand(clearCacheCmd);
        rootCmd.AddCommand(listCmd);
        rootCmd.AddCommand(loginCmd);
        rootCmd.AddCommand(logoutCmd);
        rootCmd.AddCommand(setCmd);
        rootCmd.AddCommand(unsetCmd);
        return rootCmd;
    }

    private void ListCmd(string tenant, string org, bool global, bool local)
    {
    }

    private void LoginCmd(string tenant, string org, bool global, bool local)
    {
    }

    private void LogoutCmd(string accountId, string userName, string org, string tenant, bool global, bool local)
    {
    }

    private void SetCmd(string accountId, string userName, string org, string tenant, bool global, bool local)
    {
    }

    private void UnsetCmd(string org, string tenant, bool global, bool local)
    {
    }

    private void ClearCacheCmd()
    {
        _authorityCache.Clear();
        _context.Console.WriteLine("Authority cache cleared");
    }
}
