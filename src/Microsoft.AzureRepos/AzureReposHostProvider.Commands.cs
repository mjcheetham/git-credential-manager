using GitCredentialManager.Commands;
using Microsoft.AzureRepos.Accounts;
using Microsoft.AzureRepos.Commands;

namespace Microsoft.AzureRepos;

public partial class AzureReposHostProvider
{
    ProviderCommand ICommandProvider.CreateCommand()
    {
        var accountResolver = new EntraAccountResolver(_entraAuth.Value);
        var command = new AzureReposCommand(
            this,
            _context,
            _entraAuth.Value,
            accountResolver,
            _bindingTargetResolver,
            _bindingManager,
            _authorityCache);
        return command.Create();
    }
}
