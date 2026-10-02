using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Linq;
using System.Threading.Tasks;
using GitCredentialManager.Tty;
using Spectre.Console;

namespace GitCredentialManager.Commands;

public class InstallCommand(ICommandContext context)
    : InstallationCommand(context, "install", "Set up Git Credential Manager")
{
    protected override Task<int> ExecuteInternalAsync(SetupTargetHost host, SetupTargetConfig config)
    {
        Context.Console.MarkupLineInterpolated(
            host.IsLocalhost
                ? $"[b]Configuring this computer[/] [i dim]({Context.Environment.HostName})[/]"
                : (FormattableString)$"[b]Configuring {host.WslDistroName}[/] [i dim](WSL)[/]");

        Context.Console.WriteLine(
            $"INSTALL({host.WslDistroName ?? Context.Environment.HostName} / {config})");

        return Task.FromResult(0);
    }
}

public class UninstallCommand(ICommandContext context)
    : InstallationCommand(context, "uninstall", "Remove Git Credential Manager")
{
    protected override Task<int> ExecuteInternalAsync(SetupTargetHost host, SetupTargetConfig config)
    {
        Context.Console.MarkupLineInterpolated(
            host.IsLocalhost
                ? $"[b]Unconfiguring this computer[/] [i dim]({Context.Environment.HostName})[/]"
                : (FormattableString)$"[b]Unconfiguring {host.WslDistroName}[/] [i dim](WSL)[/]");

        Context.Console.WriteLine(
            $"UNINSTALL({host.WslDistroName ?? Context.Environment.HostName} / {config})");

        return Task.FromResult(0);
    }
}

public abstract class InstallationCommand : Command
{
    protected readonly ICommandContext Context;

    protected InstallationCommand(ICommandContext context, string name, string description) : base(name, description)
    {
        EnsureArgument.NotNull(context, nameof(context));
        Context = context;

        var interactive = new Option<bool>(["--interactive", "-i"],
            "Run in interactive mode.");

        var user = new Option<bool>(["--user"],
            "Modify the current user's configuration only (default).");

        var system = new Option<bool>(["--system"],
            "Modify the system-wide configuration instead of the current user.");

        var wslDistro = new Option<string>(["--wsl-distro", "-w"],
            "Configure a Windows Subsystem for Linux (WSL) distribution.");

        AddOption(interactive);
        AddOption(user);
        AddOption(system);

        if (PlatformUtils.IsWindows())
        {
            AddOption(wslDistro);
        }

        this.SetHandler(ExecuteAsync, interactive, user, system, wslDistro);
    }

    private async Task<int> ExecuteAsync(bool interactive, bool user, bool system, string wslDistro)
    {
        using var _ = Trace2.StartRegion("install_cmd", "run");
        Trace2.WriteData("cfg_cmd", "name", Name);
        Trace2.WriteData("cfg_cmd", "interactive", interactive.ToString().ToLowerInvariant());

        Trace2.WriteData("cfg_cmd", "arg/user", user.ToString().ToLowerInvariant());
        Trace2.WriteData("cfg_cmd", "arg/system", system.ToString().ToLowerInvariant());
        Trace2.WriteData("cfg_cmd", "arg/wsl_distro", wslDistro);

        if (user && system)
        {
            throw new ArgumentException("Cannot specify both --user and --system options.");
        }

        SetupTargetConfig config;
        if (user)
        {
            config = SetupTargetConfig.User;
        }
        else if (system)
        {
            config = SetupTargetConfig.System;
        }
        else if (interactive)
        {
            var prompt = TerminalPrompts.CreateSelection<SetupTargetConfig>()
                .Title("Which configuration scope should be modified?");
            prompt.AddChoice("Current user only", SetupTargetConfig.User);
            prompt.AddChoice("System wide", SetupTargetConfig.System);

            SelectionPromptItem<SetupTargetConfig> result = await Context.Console.ShowPromptAsync(prompt);
            config = result?.Item ?? throw new OperationCanceledException("user cancelled setup");
        }
        else // default
        {
            config = SetupTargetConfig.User;
        }

        SetupTargetHost host = SetupTargetHost.Localhost;
        if (PlatformUtils.IsWindows())
        {
            if (!string.IsNullOrWhiteSpace(wslDistro))
            {
                if (!WslUtils.IsDistributionExists(wslDistro))
                {
                    Context.Console.WriteError($"WSL distribution '{wslDistro}' does not exist.");
                    return 1;
                }

                host = SetupTargetHost.Wsl(wslDistro);
            }
            else if (interactive)
            {
                host = await AskTargetHostAsync();
            }
        }

        Trace2.WriteData("cfg_cmd", "target/config", config.ToString().ToLowerInvariant());
        if (host.IsLocalhost)
        {
            Trace2.WriteData("cfg_cmd", "target/host_type", "localhost");
        }
        else
        {
            Trace2.WriteData("cfg_cmd", "target/host_type", "wsl");
            Trace2.WriteData("cfg_cmd", "target/wsl_distro", host.WslDistroName);
        }

        return await ExecuteInternalAsync(host, config);
    }

    protected abstract Task<int> ExecuteInternalAsync(SetupTargetHost host, SetupTargetConfig config);

    protected class SetupTargetHost
    {
        public static readonly SetupTargetHost Localhost = new();
        public static SetupTargetHost Wsl(string distroName) => new(distroName);
        private SetupTargetHost(string distroName = null) => WslDistroName = distroName;
        public bool IsLocalhost => WslDistroName is null;
        public string WslDistroName { get; }
    }

    protected enum SetupTargetConfig
    {
        User,
        System
    }

    private async Task<SetupTargetHost> AskTargetHostAsync()
    {
        // On Windows the user could also wish to set up one of many WSL distributions.
        if (PlatformUtils.IsWindows())
        {
            IReadOnlyList<string> wslDistros = WslUtils.GetWslDistributions();
            if (wslDistros.Count > 0)
            {
                var wslItems = wslDistros
                    .Select(x => new SelectionPromptItem<SetupTargetHost>(x, SetupTargetHost.Wsl(x)));

                var prompt = TerminalPrompts.CreateSelection<SetupTargetHost>()
                    .Title("Which host would you like to modify?");
                prompt.AddChoice($"{Context.Environment.HostName} [i dim](This Computer)[/]", SetupTargetHost.Localhost);
                prompt.AddChoiceGroup(
                    new SelectionPromptItem<SetupTargetHost>("WSL Distributions", null),
                    wslItems
                );

                SelectionPromptItem<SetupTargetHost> result = await Context.Console.ShowPromptAsync(prompt);
                if (result is null) // prompt was cancelled
                {
                    return null;
                }

                return result.Item;
            }
        }

        return SetupTargetHost.Localhost;
    }
}
