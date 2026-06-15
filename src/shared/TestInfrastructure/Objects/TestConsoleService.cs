using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Testing;

namespace GitCredentialManager.Tests.Objects;

public class TestConsoleService : IConsoleService
{
    public IDictionary<string, string> Prompts { get; } = new Dictionary<string, string>();
    public IDictionary<string, string> SecretPrompts { get; } = new Dictionary<string, string>();
    public IList<string> WrittenMessages { get; } = new List<string>();

    public TestConsole InteractiveConsole { get; } = new TestConsole();
    public TestConsole MessagesConsole { get; } = new TestConsole();

    public TestConsoleService()
    {
        // Selection prompts (driven via PushSelection) require an interactive console.
        InteractiveConsole.Profile.Capabilities.Interactive = true;
    }

    public IAnsiConsole Interactive => InteractiveConsole;

    public IAnsiConsole Messages => MessagesConsole;

    public void WriteInfo(string message) => WrittenMessages.Add(message);

    public void WriteWarning(string message) => WrittenMessages.Add(message);

    public void WriteError(string message) => WrittenMessages.Add(message);

    public void WriteFatal(string message) => WrittenMessages.Add(message);

    public void WriteLine(string message) => WrittenMessages.Add(message);

    public Task<string> PromptAsync(string label, CancellationToken ct = default)
    {
        if (!Prompts.TryGetValue(label, out string result))
        {
            throw new Exception($"No result has been configured for prompt text '{label}'");
        }

        return Task.FromResult(result);
    }

    public Task<string> PromptSecretAsync(string label, CancellationToken ct = default)
    {
        if (!SecretPrompts.TryGetValue(label, out string result))
        {
            throw new Exception($"No result has been configured for secret prompt text '{label}'");
        }

        return Task.FromResult(result);
    }

    /// <summary>
    /// Queue the keystrokes needed to select the choice at the given zero-based
    /// <paramref name="index"/> in the next Spectre selection prompt shown on
    /// <see cref="Interactive"/>.
    /// </summary>
    public void PushSelection(int index)
    {
        for (int i = 0; i < index; i++)
        {
            InteractiveConsole.Input.PushKey(ConsoleKey.DownArrow);
        }

        InteractiveConsole.Input.PushKey(ConsoleKey.Enter);
    }
}
