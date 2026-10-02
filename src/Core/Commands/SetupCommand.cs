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

        var interactive = new Option<bool>(["--interactive", "-i"],
            "Run the setup command in interactive mode.");
        var system = new Option<bool>(["--system", "-s"],
            "Modify the system-wide Git configuration instead of the current user.");

        var wslDistro = new Option<string>(["--wsl-distro", "-w"],
            "Set up a specific WSL distribution.");

        AddOption(interactive);
        AddOption(system);

        if (PlatformUtils.IsWindows())
        {
            AddOption(wslDistro);
        }

        this.SetHandler(ExecuteAsync, interactive, system, wslDistro);
    }

    private Task<int> ExecuteAsync(bool interactive, bool system, string wslDistro)
    {
        if (interactive)
        {
            return ExecuteInteractiveAsync(wslDistro);
        }

        return Task.FromResult(127);
    }

    private class SetupTarget
    {
        public static SetupTarget Host { get; } = new();
        public static SetupTarget Wsl(string distroName) => new(distroName);
        private SetupTarget(string distroName = null) => WslDistroName = distroName;
        public bool IsHost => WslDistroName is null;
        public string WslDistroName { get; }
    }

    private async Task<int> ExecuteInteractiveAsync(string wslDistro)
    {
        if (PlatformUtils.IsWindows())
        {
            IReadOnlyList<string> wslDistros = WslUtils.GetWslDistributions();
            if (!string.IsNullOrWhiteSpace(wslDistro))
            {
                if (!wslDistros.Contains(wslDistro, StringComparer.OrdinalIgnoreCase))
                {
                    _context.Console.WriteError($"WSL distribution '{wslDistro}' not found.");
                    return 1;
                }

                return await ExecuteInteractiveWslAsync(wslDistro);
            }

            if (wslDistros.Count > 0)
            {
                string computerName =
                    _context.Environment.GetEnvironmentVariable(Constants.EnvironmentVariables.WindowsComputerName) ??
                    "localhost";
                string hostLabel = $"This Computer ({computerName})";

                var wslItems = wslDistros
                    .Select(x => new SelectionPromptItem<SetupTarget>(x, SetupTarget.Wsl(x)));

                var prompt = TerminalPrompts.CreateSelection<SetupTarget>()
                    .Title("Select a setup target");
                prompt.AddChoice(hostLabel, SetupTarget.Host);
                prompt.AddChoiceGroup(
                    new SelectionPromptItem<SetupTarget>("WSL Distributions", null),
                    wslItems
                );

                SelectionPromptItem<SetupTarget> result = await _context.Console.ShowPromptAsync(prompt);
                SetupTarget target = result?.Item;

                if (target is null)
                {
                    _context.Console.WriteInfo("No setup target selected");
                    return 1;
                }

                if (target.IsHost)
                {
                    _context.Console.MarkupLineInterpolated($"[b]Target:[/] {hostLabel}");
                }
                else
                {
                    return await ExecuteInteractiveWslAsync(target.WslDistroName);
                }
            }
        }

        return await ExecuteInteractiveHostAsync();
    }

    private Task<int> ExecuteInteractiveHostAsync()
    {
        throw new NotImplementedException();
    }

    private Task<int> ExecuteInteractiveWslAsync(string wslDistro)
    {
        _context.Console.MarkupLineInterpolated($"[b]Target:[/] {wslDistro} [i dim](WSL)[/]");
        throw new NotImplementedException();
    }
}
