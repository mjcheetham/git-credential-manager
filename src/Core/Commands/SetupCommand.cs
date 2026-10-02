using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Linq;
using System.Threading.Tasks;
using GitCredentialManager.Tty;
using Spectre.Console;

namespace GitCredentialManager.Commands;

public class SetupCommand : Command
{
    private readonly ICommandContext _context;

    public SetupCommand(ICommandContext context) : base("setup", "Setup the Git Credential Manager")
    {
        EnsureArgument.NotNull(context, nameof(context));
        _context = context;

        var remove = new Option<bool>(["--remove", "-r"],
            "Uninstall configuration for Git Credential Manager.");

        var interactive = new Option<bool>(["--interactive", "-i"],
            "Run the setup command in interactive mode.");

        var user = new Option<bool>(["--user"],
            "Modify the current user's Git configuration only (default).");

        var system = new Option<bool>(["--system"],
            "Modify the system-wide Git configuration instead of the current user.");

        var wslDistro = new Option<string>(["--wsl-distro", "-w"],
            "Set up a Windows Subsystem for Linux (WSL) distribution.");

        AddOption(remove);
        AddOption(interactive);
        AddOption(user);
        AddOption(system);

        if (PlatformUtils.IsWindows())
        {
            AddOption(wslDistro);
        }

        this.SetHandler(ExecuteAsync, remove, interactive, user, system, wslDistro);
    }

    private async Task<int> ExecuteAsync(bool remove, bool interactive, bool user, bool system, string wslDistro)
    {
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

            SelectionPromptItem<SetupTargetConfig> result = await _context.Console.ShowPromptAsync(prompt);
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
                    _context.Console.WriteError($"WSL distribution '{wslDistro}' does not exist.");
                    return 1;
                }

                host = SetupTargetHost.Wsl(wslDistro);
            }
            else if (interactive)
            {
                host = await AskTargetHostAsync();
            }
        }

        try
        {
            if (remove)
            {
                await UninstallAsync(host, config);
            }
            else
            {
                await InstallAsync(host, config);
            }
        }
        catch (Exception ex)
        {
            _context.Console.WriteError(
                $"failed to modify '{host.WslDistroName ?? _context.Environment.HostName}': {ex.Message}");
            return 1;
        }

        return 0;
    }

    private Task InstallAsync(SetupTargetHost host, SetupTargetConfig config)
    {
        _context.Console.MarkupLineInterpolated(
            host.IsLocalhost
                ? $"[b]Configuring this computer[/] [i dim]({_context.Environment.HostName})[/]"
                : (FormattableString)$"[b]Configuring {host.WslDistroName}[/] [i dim](WSL)[/]");

        _context.Console.WriteLine(
            $"INSTALL({host.WslDistroName ?? _context.Environment.HostName} / {config})");

        return Task.CompletedTask;
    }

    private Task UninstallAsync(SetupTargetHost host, SetupTargetConfig config)
    {
        _context.Console.MarkupLineInterpolated(
            host.IsLocalhost
                ? $"[b]Unconfiguring this computer[/] [i dim]({_context.Environment.HostName})[/]"
                : (FormattableString)$"[b]Unconfiguring {host.WslDistroName}[/] [i dim](WSL)[/]");

        _context.Console.WriteLine(
            $"UNINSTALL({host.WslDistroName ?? _context.Environment.HostName} / {config})");

        return Task.CompletedTask;
    }

    private class SetupTargetHost
    {
        public static readonly SetupTargetHost Localhost = new();
        public static SetupTargetHost Wsl(string distroName) => new(distroName);
        private SetupTargetHost(string distroName = null) => WslDistroName = distroName;
        public bool IsLocalhost => WslDistroName is null;
        public string WslDistroName { get; }
    }

    private enum SetupTargetConfig
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
                prompt.AddChoice($"{_context.Environment.HostName} [i dim](This Computer)[/]", SetupTargetHost.Localhost);
                prompt.AddChoiceGroup(
                    new SelectionPromptItem<SetupTargetHost>("WSL Distributions", null),
                    wslItems
                );

                SelectionPromptItem<SetupTargetHost> result = await _context.Console.ShowPromptAsync(prompt);
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
