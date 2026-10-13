using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace GitCredentialManager;

public partial class Application
{
    string IConfigurableComponent.Name => "Git Credential Manager";

    Task IConfigurableComponent.ConfigureAsync(ConfigurationTarget target)
    {
        string helperKey =
            $"{Constants.GitConfiguration.Credential.SectionName}.{Constants.GitConfiguration.Credential.Helper}";
        string appPath = GetGitConfigAppPath(target);

        GitConfigurationLevel configLevel = target.Scope == ConfigurationScope.System
            ? GitConfigurationLevel.System
            : GitConfigurationLevel.Global;

        Context.Trace.WriteLine($"Configuring for config level '{configLevel}'.");

        IGit git = target.GetGit(Context);
        IGitConfiguration config = git.GetConfiguration();

        // We are looking for the following to be set in the config:
        //
        // [credential]
        //     ...                # any number of helper entries (possibly none)
        //     helper =           # an empty value to reset/clear any previous entries (if applicable)
        //     helper = {appPath} # the expected executable value & directly following the empty value
        //     ...                # any number of helper entries (possibly none, but not the empty value '')
        //
        string[] currentValues = config.GetAll(configLevel, GitConfigurationType.Raw, helperKey).ToArray();

        // Try to locate an existing app entry with a blank reset/clear entry immediately preceding,
        // and no other blank empty/clear entries following (which effectively disable us).
        int appIndex = Array.FindLastIndex(currentValues, x => Context.FileSystem.IsSamePath(x, appPath));
        int lastEmptyIndex = Array.FindLastIndex(currentValues, string.IsNullOrWhiteSpace);
        if (appIndex > 0 && string.IsNullOrWhiteSpace(currentValues[appIndex - 1]) && lastEmptyIndex < appIndex)
        {
            Context.Trace.WriteLine("Credential helper configuration is already set correctly.");
        }
        else
        {
            Context.Trace.WriteLine("Updating Git credential helper configuration...");

            // Clear any existing app entries in the configuration
            config.UnsetAll(configLevel, helperKey, Regex.Escape(appPath));

            // Reload updated helper settings (unset only clears entries in primary file, ignores includes and alternatives)
            currentValues = config.GetAll(configLevel, GitConfigurationType.Raw, helperKey).ToArray();

            // Add an empty value for `credential.helper`, which has the effect of clearing any helper value
            // from any lower-level Git configuration, then add a second value which is the actual executable path.
            if ((currentValues.Length == 0) || !string.IsNullOrWhiteSpace(currentValues.Last()))
            {
                config.Add(configLevel, helperKey, string.Empty);
            }

            config.Add(configLevel, helperKey, appPath);
        }

        return Task.CompletedTask;
    }

    Task IConfigurableComponent.UnconfigureAsync(ConfigurationTarget target)
    {
        string helperKey =
            $"{Constants.GitConfiguration.Credential.SectionName}.{Constants.GitConfiguration.Credential.Helper}";
        string appPath = GetGitConfigAppPath(target);

        GitConfigurationLevel configLevel = target.Scope == ConfigurationScope.System
            ? GitConfigurationLevel.System
            : GitConfigurationLevel.Global;

        Context.Trace.WriteLine($"Unconfiguring for config level '{configLevel}'.");

        IGit git = target.GetGit(Context);
        IGitConfiguration config = git.GetConfiguration();

        // We are looking for the following to be set in the config:
        //
        // [credential]
        //     ...                 # any number of helper entries (possibly none)
        //     helper =            # an empty value to reset/clear any previous entries (if applicable)
        //     helper = {appPath} # the expected executable value & directly following the empty value
        //     ...                 # any number of helper entries (possibly none)
        //
        // We should remove the {appPath} entry, and any blank entries immediately preceding IFF there are no more entries following.
        //
        Context.Trace.WriteLine("Removing Git credential helper configuration...");

        string[] currentValues = config.GetAll(configLevel, GitConfigurationType.Raw, helperKey).ToArray();

        int appIndex = Array.FindIndex(currentValues, x => Context.FileSystem.IsSamePath(x, appPath));
        if (appIndex > -1)
        {
            // Check for the presence of a blank entry immediately preceding an app entry in the last position
            if (appIndex > 0 && appIndex == currentValues.Length - 1 &&
                string.IsNullOrWhiteSpace(currentValues[appIndex - 1]))
            {
                // Clear the blank entry
                config.UnsetAll(configLevel, helperKey, Constants.RegexPatterns.Empty);
            }

            // Clear app entry
            string appEntryValue = currentValues[appIndex];
            config.UnsetAll(configLevel, helperKey, Regex.Escape(appEntryValue));
        }

        return Task.CompletedTask;
    }

    private string GetGitConfigAppPath(ConfigurationTarget target)
    {
        string path = Context.ApplicationPath;

        if (!string.IsNullOrWhiteSpace(target.Host.WslDistribution))
        {
            path = WslUtils.ConvertToUnixPath(Context.FileSystem, path);
        }
        else if (PlatformUtils.IsWindows())
        {
            // On Windows we must use UNIX style path separators
            path = path.Replace('\\', '/');
        }

        // We must escape characters like ' ', '(', and ')'
        return path
            .Replace(" ", "\\ ")
            .Replace("(", "\\(")
            .Replace(")", "\\)");;
    }
}
