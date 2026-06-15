using System.Threading;
using System.Threading.Tasks;
using GitCredentialManager.Tty;
using Spectre.Console;

namespace GitCredentialManager;

/// <summary>
/// The user-facing console. Routes output-only messages (diagnostics, QR codes) to
/// standard error, and interactive prompts — which need to read input — to the
/// controlling terminal.
/// </summary>
/// <remarks>
/// Git's stdin/stdout are reserved for the credential protocol, so neither sink may use
/// them. Standard error is always available and capturable, so messages survive even
/// when no terminal is attached; only prompts require a TTY, since only they read input.
/// </remarks>
public interface IConsoleService
{
    /// <summary>
    /// The interactive console over the controlling terminal (input + output). Use for
    /// advanced interactive prompts not covered by the helpers below, such as selection
    /// menus. Falls back to a no-op console when no terminal is reachable.
    /// </summary>
    IAnsiConsole Interactive { get; }

    /// <summary>
    /// The messages console over standard error (output-only). Use for advanced message
    /// rendering not covered by the <c>Write*</c> helpers below.
    /// </summary>
    IAnsiConsole Messages { get; }

    void WriteInfo(string message);
    void WriteWarning(string message);
    void WriteError(string message);
    void WriteFatal(string message);
    void WriteLine(string message);

    /// <summary>
    /// Prompt the user for a line of text on the controlling terminal.
    /// </summary>
    Task<string> PromptAsync(string label, CancellationToken ct = default);

    /// <summary>
    /// Prompt the user for a secret (masked) line of text on the controlling terminal.
    /// </summary>
    Task<string> PromptSecretAsync(string label, CancellationToken ct = default);
}

public class ConsoleService : IConsoleService
{
    public ConsoleService(IStandardStreams streams)
        : this(AnsiConsoleFactory.Create(), AnsiConsoleFactory.CreateForWriter(streams.Error, streams.IsErrorRedirected))
    { }

    public ConsoleService(IAnsiConsole interactive, IAnsiConsole messages)
    {
        Interactive = interactive;
        Messages = messages;
    }

    public IAnsiConsole Interactive { get; }

    public IAnsiConsole Messages { get; }

    public void WriteInfo(string message) => Messages.MarkupLine($"[blue]info:[/] {message}");

    public void WriteWarning(string message) => Messages.MarkupLine($"[yellow]warning:[/] {message}");

    public void WriteError(string message) => Messages.MarkupLine($"[red]error:[/] {message}");

    public void WriteFatal(string message) => Messages.MarkupLine($"[red]fatal:[/] {message}");

    public void WriteLine(string message) => Messages.WriteLine(message);

    public Task<string> PromptAsync(string label, CancellationToken ct = default) =>
        new TextPrompt<string>(label).AllowEmpty().ShowAsync(Interactive, ct);

    public Task<string> PromptSecretAsync(string label, CancellationToken ct = default) =>
        new TextPrompt<string>(label).AllowEmpty().Secret(null).ShowAsync(Interactive, ct);
}
