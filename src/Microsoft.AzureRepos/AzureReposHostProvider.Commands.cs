using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Linq;
using System.Threading.Tasks;
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
        var clearCacheCmd = new Command("clear-cache", "Clear the Azure authority cache");
        clearCacheCmd.SetHandler(ClearCacheCmd);

        //
        // list <organization> [--show-remotes] [--verbose]
        //
        var listCmd = new Command("list", "List all user account bindings");
        var orgFilterArg =
            new Argument<string>("organization", "(optional) Filter results by Azure DevOps organization name")
            {
                Arity = ArgumentArity.ZeroOrOne
            };
        var remoteOpt = new Option<bool>("--show-remotes")
        {
            Description = "Also show Azure DevOps remote user bindings for the current repository"
        };
        var verboseOpt = new Option<bool>(new[] { "--verbose", "-v" }, "Verbose output - show remote URLs");
        listCmd.AddArgument(orgFilterArg);
        listCmd.AddOption(remoteOpt);
        listCmd.AddOption(verboseOpt);
        listCmd.SetHandler(ListCmd, orgFilterArg, remoteOpt, verboseOpt);

        //
        // bind <organization> <username> [--local]
        //
        var bindCmd = new Command("bind", "Bind a user account to an Azure DevOps organization");
        var orgArg = new Argument<string>("organization", "Azure DevOps organization name")
        {
            Arity = ArgumentArity.ExactlyOne
        };
        var userNameArg = new Argument<string>("username", "Username or email (e.g.: alice@example.com)")
        {
            Arity = ArgumentArity.ExactlyOne
        };
        var localOpt = new Option<bool>("--local", "Target the local repository Git configuration");
        bindCmd.AddArgument(orgArg);
        bindCmd.AddArgument(userNameArg);
        bindCmd.AddOption(localOpt);
        bindCmd.SetHandler(BindCmd, orgArg, userNameArg, localOpt);

        //
        // unbind <organization> [--local]
        //
        var unbindCmd = new Command("unbind")
        {
            Description = "Remove user account binding for an Azure DevOps organization",
        };
        unbindCmd.AddArgument(orgArg);
        unbindCmd.AddOption(localOpt);
        unbindCmd.SetHandler(UnbindCmd, orgArg, localOpt);

        var rootCmd = new ProviderCommand(this);
        rootCmd.AddAlias("azrepos");
        rootCmd.AddAlias("ado");
        rootCmd.AddCommand(listCmd);
        rootCmd.AddCommand(bindCmd);
        rootCmd.AddCommand(unbindCmd);
        rootCmd.AddCommand(clearCacheCmd);
        return rootCmd;
    }

    private void ClearCacheCmd()
    {
        _authorityCache.Clear();
        _context.Console.WriteLine("Authority cache cleared");
    }

    private class RemoteBinding
    {
        public string Remote { get; set; }
        public bool IsPush { get; set; }
        public Uri Uri { get; set; }
    }

    private void ListCmd(string organization, bool showRemotes, bool verbose)
    {
        // Get all organization bindings from the user manager
        IList<AzureReposBinding> bindings = _bindingManager.GetBindings(organization).ToList();
        IDictionary<string, IEnumerable<AzureReposBinding>> orgBindingMap =
            bindings.GroupBy(x => x.Organization).ToDictionary();

        // If we are asked to also show remotes we build the remote binding map
        var orgRemotesMap = new Dictionary<string, ICollection<RemoteBinding>>();
        if (showRemotes)
        {
            if (!_context.Git.IsInsideRepository())
            {
                _context.Console.WriteWarning("not inside a git repository (--show-remotes has no effect)");
            }

            static bool IsAzureDevOpsHttpRemote(string url, out Uri uri)
            {
                return Uri.TryCreate(url, UriKind.Absolute, out uri) &&
                       (StringComparer.OrdinalIgnoreCase.Equals(Uri.UriSchemeHttp, uri.Scheme) ||
                        StringComparer.OrdinalIgnoreCase.Equals(Uri.UriSchemeHttps, uri.Scheme)) &&
                       UriHelpers.IsAzureDevOpsHost(uri.Host);
            }

            foreach (GitRemote remote in _context.Git.GetRemotes())
            {
                if (IsAzureDevOpsHttpRemote(remote.FetchUrl, out Uri fetchUri))
                {
                    string fetchOrg = UriHelpers.GetOrganizationName(fetchUri);
                    var binding = new RemoteBinding { IsPush = false, Remote = remote.Name, Uri = fetchUri };
                    orgRemotesMap.Append(fetchOrg, binding);
                }

                if (IsAzureDevOpsHttpRemote(remote.PushUrl, out Uri pushUri))
                {
                    string pushOrg = UriHelpers.GetOrganizationName(pushUri);
                    var binding = new RemoteBinding { IsPush = true, Remote = remote.Name, Uri = pushUri };
                    orgRemotesMap.Append(pushOrg, binding);
                }
            }
        }

        bool isFiltered = !string.IsNullOrWhiteSpace(organization);
        string indent = isFiltered ? string.Empty : "  ";

        // Get the set of all organization names (organization names are not case sensitive)
        ISet<string> orgNames = new HashSet<string>(orgBindingMap.Keys, StringComparer.OrdinalIgnoreCase);
        orgNames.UnionWith(orgRemotesMap.Keys);

        var icmp = StringComparer.OrdinalIgnoreCase;

        foreach (string orgName in orgNames)
        {
            if (!isFiltered)
            {
                _context.Console.WriteLine($"{orgName}:");
            }

            // Print organization bindings
            foreach (AzureReposBinding binding in orgBindingMap.GetValues(orgName))
            {
                if (binding.GlobalUserName != null)
                {
                    _context.Console.WriteLine($"{indent}(global) -> {binding.GlobalUserName}");
                }

                if (binding.LocalUserName != null)
                {
                    string value = string.IsNullOrEmpty(binding.LocalUserName)
                        ? "(no inherit)"
                        : binding.LocalUserName;
                    _context.Console.WriteLine($"{indent}(local)  -> {value}");
                }
            }

            // Print remote bindings
            IEnumerable<IGrouping<string, RemoteBinding>> remoteBindingMap =
                orgRemotesMap.GetValues(orgName).GroupBy(x => x.Remote);

            foreach (var remoteBinding in remoteBindingMap)
            {
                _context.Console.WriteLine($"{indent}{remoteBinding.Key}:");
                foreach (RemoteBinding binding in remoteBinding)
                {
                    // User names in dev.azure.com URLs cannot always be used as *actual user names*
                    // because of the unfortunate decision to use this field to get the Azure DevOps
                    // organization name to be sent by Git to credential helpers.
                    //
                    // We show dev.azure.com URLs as "inherit", if there is a username that matches
                    // the organization name.
                    if (!binding.Uri.TryGetUserInfo(out string userName, out _) ||
                        UriHelpers.IsDevAzureComHost(binding.Uri.Host) && icmp.Equals(userName, orgName))
                    {
                        userName = "(inherit)";
                    }

                    string url = null;
                    if (verbose)
                    {
                        url = $"{binding.Uri.WithoutUserInfo()} ";
                    }

                    _context.Console.WriteLine(binding.IsPush
                        ? $"{indent}  {url}(push)  -> {userName}"
                        : $"{indent}  {url}(fetch) -> {userName}");
                }
            }
        }
    }

    private Task<int> BindCmd(string organization, string userName, bool local)
    {
        if (local && !_context.Git.IsInsideRepository())
        {
            _context.Console.WriteError("not inside a git repository (cannot use --local)");
            return Task.FromResult(-1);
        }

        _bindingManager.Bind(organization, userName, local);
        return Task.FromResult(0);
    }

    private Task<int> UnbindCmd(string organization, bool local)
    {
        if (local && !_context.Git.IsInsideRepository())
        {
            _context.Console.WriteError("not inside a git repository (cannot use --local)");
            return Task.FromResult(-1);
        }

        _bindingManager.Unbind(organization, local);
        return Task.FromResult(0);
    }
}
