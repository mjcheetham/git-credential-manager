using System.Linq;
using System.Threading.Tasks;
using GitCredentialManager;

namespace Microsoft.AzureRepos;

public partial class AzureReposHostProvider
{
    string IConfigurableComponent.Name => "Azure Repos provider";

    public Task ConfigureAsync(ConfigurationTarget target)
    {
        string useHttpPathKey =
            $"{Constants.GitConfiguration.Credential.SectionName}.https://dev.azure.com.{Constants.GitConfiguration.Credential.UseHttpPath}";

        GitConfigurationLevel configurationLevel = target == ConfigurationTarget.System
            ? GitConfigurationLevel.System
            : GitConfigurationLevel.Global;

        IGitConfiguration targetConfig = _context.Git.GetConfiguration();

        if (targetConfig.TryGet(useHttpPathKey, false, out string currentValue) && currentValue.IsTruthy())
        {
            _context.Trace.WriteLine(
                "Git configuration 'credential.useHttpPath' is already set to 'true' for https://dev.azure.com.");
        }
        else
        {
            _context.Trace.WriteLine(
                "Setting Git configuration 'credential.useHttpPath' to 'true' for https://dev.azure.com...");
            targetConfig.Set(configurationLevel, useHttpPathKey, "true");
        }

        return Task.CompletedTask;
    }

    public Task UnconfigureAsync(ConfigurationTarget target)
    {
        string helperKey =
            $"{Constants.GitConfiguration.Credential.SectionName}.{Constants.GitConfiguration.Credential.Helper}";
        string useHttpPathKey =
            $"{Constants.GitConfiguration.Credential.SectionName}.https://dev.azure.com.{Constants.GitConfiguration.Credential.UseHttpPath}";

        _context.Trace.WriteLine("Clearing Git configuration 'credential.useHttpPath' for https://dev.azure.com...");

        GitConfigurationLevel configurationLevel = target == ConfigurationTarget.System
            ? GitConfigurationLevel.System
            : GitConfigurationLevel.Global;

        IGitConfiguration targetConfig = _context.Git.GetConfiguration();

        // On Windows, if there is a "manager" or "manager-core" entry remaining in the system config then we must
        // not clear the useHttpPath option otherwise this would break the bundled version of GCM in Git for Windows.
        if (!PlatformUtils.IsWindows() || target != ConfigurationTarget.System ||
            targetConfig.GetAll(helperKey).All(x => !string.Equals(x, "manager") && !string.Equals(x, "manager-core")))
        {
            targetConfig.Unset(configurationLevel, useHttpPathKey);
        }

        return Task.CompletedTask;
    }
}
