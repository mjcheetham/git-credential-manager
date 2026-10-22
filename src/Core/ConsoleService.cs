using System;
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
    void WriteInfo(string message);
    void WriteWarning(string message);
    void WriteError(string message);
    void WriteFatal(string message);
    void WriteLine(string message);

    /// <summary>
    /// Prompt the user for a selection on the controlling terminal.
    /// </summary>
    T ShowPrompt<T>(IPrompt<T> prompt);

    /// <summary>
    /// Prompt the user for a selection on the controlling terminal.
    /// </summary>
    Task<T> ShowPromptAsync<T>(IPrompt<T> prompt, CancellationToken ct = default);

    /// <summary>
    /// When any key is pressed on the controlling terminal, run the provided action.
    /// </summary>
    /// <param name="action">Action to invoke when any key is pressed.</param>
    /// <returns>Disposable object that can be used to stop the pending action.</returns>
    IDisposable RunOnKeyPress(Action action);
}

public class ConsoleService : IConsoleService
{
    private readonly Lazy<IAnsiConsole> _ttyConsole;
    private readonly Lazy<IAnsiConsole> _stderrConsole;

    public ConsoleService(IStandardStreams streams)
        : this(AnsiConsoleFactory.CreateForTty, () => AnsiConsoleFactory.CreateForWriter(streams.Error, streams.IsErrorRedirected))
    { }

    public ConsoleService(Func<IAnsiConsole> ttyConsoleFunc, Func<IAnsiConsole> stderrConsoleFunc)
    {
        _ttyConsole = new Lazy<IAnsiConsole>(ttyConsoleFunc);
        _stderrConsole = new Lazy<IAnsiConsole>(stderrConsoleFunc);
    }

    public void WriteInfo(string message) => _stderrConsole.Value.MarkupLine($"[blue]info:[/] {Markup.Escape(message)}");

    public void WriteWarning(string message) => _stderrConsole.Value.MarkupLine($"[yellow]warning:[/] {Markup.Escape(message)}");

    public void WriteError(string message) => _stderrConsole.Value.MarkupLine($"[red]error:[/] {Markup.Escape(message)}");

    public void WriteFatal(string message) => _stderrConsole.Value.MarkupLine($"[red]fatal:[/] {Markup.Escape(message)}");

    public void WriteLine(string message) => _stderrConsole.Value.WriteLine(message);

    public T ShowPrompt<T>(IPrompt<T> prompt) =>
        prompt.Show(_ttyConsole.Value);

    public Task<T> ShowPromptAsync<T>(IPrompt<T> prompt, CancellationToken ct = default) =>
        prompt.ShowAsync(_ttyConsole.Value, ct);

    public IDisposable RunOnKeyPress(Action action)
    {
        return new OnAnyKeyTask(_ttyConsole.Value, action);
    }
}

public class OnAnyKeyTask : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly IAnsiConsole _console;
    private readonly Action _action;

    public OnAnyKeyTask(IAnsiConsole console, Action action)
    {
        _console = console;
        _action = action;

        Task.Run(Intercept, _cts.Token);
    }

    private void Intercept()
    {
        _console.ExclusivityMode.Run<object>(() =>
        {
            while (!_cts.IsCancellationRequested)
            {
                // We spin here until we know that a key is available because calling ReadKey or ReadKeyAsync will
                // block the thread until a key is pressed, which would prevent cancellation from being observed.
                while (!_console.Input.IsKeyAvailable())
                {
                    Thread.Sleep(250);
                }

                // This blocks until a key is pressed, but we know a key is available so it should return immediately.
                _console.Input.ReadKey(intercept: true);

                // We don't care what key was pressed, we just want to run the action.
                _action();
                break;
            }

            return null;
        });
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
