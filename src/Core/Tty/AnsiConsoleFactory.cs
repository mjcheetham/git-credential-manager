using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GitCredentialManager.Interop.Linux;
using GitCredentialManager.Interop.MacOS;
using GitCredentialManager.Interop.Posix;
using GitCredentialManager.Interop.Windows;
using Spectre.Console;

namespace GitCredentialManager.Tty;

/// <summary>
/// Constructs the <see cref="IAnsiConsole"/> used for rich interactive prompts.
/// </summary>
/// <remarks>
/// The credential helper's stdin and stdout are reserved for the Git credential
/// protocol; we cannot let Spectre.Console talk to them. Real platform implementations
/// route Spectre over the TTY bypass (<c>/dev/tty</c> on POSIX, <c>CONIN$</c>/<c>CONOUT$</c>
/// on Windows). When no TTY is reachable, the factory returns a no-op console:
/// prompts immediately return null and rendered output is discarded. Callers must
/// treat a null prompt result as "no user available" and respond accordingly
/// (typically by returning <c>Credential.NotFound</c>).
/// </remarks>
public static class AnsiConsoleFactory
{
    /// <summary>
    /// Construct an <see cref="IAnsiConsole"/> for the current process.
    /// </summary>
    /// <remarks>
    /// Attempts to open the platform TTY bypass for output and input. Falls
    /// back to a headless no-op console when output is unavailable; if only
    /// input is unavailable, reads report no key while TTY output remains
    /// available.
    /// </remarks>
    public static IAnsiConsole CreateForTty()
    {
        using var _ = Trace2.StartRegion("ansi_console", "create_tty");
        IAnsiConsoleOutput output = TryCreatePlatformOutput();
        if (output is null)
        {
            return CreateHeadless();
        }

        IAnsiConsole inner = AnsiConsole.Create(
            new AnsiConsoleSettings
            {
                Out = output,
                Ansi = AnsiSupport.Yes,
                ColorSystem = ColorSystemSupport.Detect,
                Interactive = InteractionSupport.Yes,
            }
        );

        IAnsiConsoleInput input = TryCreatePlatformInput() ?? new NullAnsiConsoleInput();
        return new AnsiConsoleWithInput(inner, input);
    }

    /// <summary>
    /// Construct an output-only <see cref="IAnsiConsole"/> over a text writer stream,
    /// for diagnostics and other messages.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="CreateForTty"/> this never opens the controlling TTY: standard error
    /// is always available (and capturable via <c>2&gt;</c>), so messages are never
    /// silently dropped even when no terminal is attached. The console carries no input.
    /// Styling follows whether the writer is a standard handle, is redirected, and supports ANSI:
    /// coloured when supported, plain text otherwise.
    /// </remarks>
    public static IAnsiConsole CreateForWriter(TextWriter writer) => CreateForWriter(writer, IsRedirected(writer), null);

    internal static IAnsiConsole CreateForWriter(TextWriter writer, bool isRedirected, bool? ansiSupport)
    {
        using var _ = Trace2.StartRegion("ansi_console", "create_writer");

        // Detection also attempts to enable VT processing on non-redirected
        // Windows standard handles, so it must receive the original writer.
        bool ansi = ansiSupport ?? AnsiCapabilities.Create(writer).Ansi;

        // Disable colours when the writer is redirected, or if we don't have ANSI support.
        bool useColors = ansi && !isRedirected;

        return AnsiConsole.Create(
            new AnsiConsoleSettings
            {
                Out = new TextWriterAnsiConsoleOutput(writer, isRedirected),
                Ansi = ansi ? AnsiSupport.Yes : AnsiSupport.No,
                ColorSystem = useColors ? ColorSystemSupport.Detect : ColorSystemSupport.NoColors,
                Interactive = InteractionSupport.No,
            }
        );
    }

    /// <summary>
    /// Construct an <see cref="IAnsiConsole"/> that discards output and reports no
    /// available input. Useful when no controlling terminal is reachable.
    /// </summary>
    internal static IAnsiConsole CreateHeadless()
    {
        using var _ = Trace2.StartRegion("ansi_console", "create_headless");
        IAnsiConsole inner = AnsiConsole.Create(
            new AnsiConsoleSettings
            {
                Out = new NullAnsiConsoleOutput(),
                Ansi = AnsiSupport.No,
                ColorSystem = ColorSystemSupport.NoColors,
                Interactive = InteractionSupport.No,
                Enrichment =
                {
                    UseDefaultEnrichers = false // do not enrich headless for CI/CD pipelines
                }
            }
        );

        return new AnsiConsoleWithInput(inner, new NullAnsiConsoleInput());
    }

    private static bool IsRedirected(TextWriter writer)
    {
        if (writer is null)
        {
            return false;
        }

        if (writer == Console.Error)
        {
            return Console.IsErrorRedirected;
        }

        if (writer == Console.Out)
        {
            return Console.IsOutputRedirected;
        }

        return false;
    }

    private static IAnsiConsoleOutput TryCreatePlatformOutput()
    {
        try
        {
            if (PlatformUtils.IsWindows())
            {
                return new WindowsAnsiConsoleOutput();
            }
            if (PlatformUtils.IsPosix())
            {
                return new PosixAnsiConsoleOutput();
            }
        }
        catch (IOException)
        {
            // No controlling TTY — fall back to headless.
        }
        catch (PlatformNotSupportedException)
        {
            // Unknown platform — fall back to headless.
        }
        return null;
    }

    private static IAnsiConsoleInput TryCreatePlatformInput()
    {
        try
        {
            if (PlatformUtils.IsWindows())
            {
                return new WindowsAnsiConsoleInput();
            }
            if (PlatformUtils.IsMacOS())
            {
                return new MacOSAnsiConsoleInput();
            }
            if (PlatformUtils.IsLinux())
            {
                return new LinuxAnsiConsoleInput();
            }
        }
        catch (IOException)
        {
            // No controlling TTY or termios call failed — fall back to no-op input.
        }
        catch (PlatformNotSupportedException)
        {
        }
        return null;
    }

    private sealed class NullAnsiConsoleOutput : IAnsiConsoleOutput
    {
        public TextWriter Writer { get; } = TextWriter.Null;
        public bool IsTerminal => false;
        public int Width => 80;
        public int Height => 24;
        public void SetEncoding(Encoding encoding) { }
    }

    private sealed class TextWriterAnsiConsoleOutput : IAnsiConsoleOutput
    {
        private readonly bool _isRedirected;

        public TextWriterAnsiConsoleOutput(TextWriter writer, bool isRedirected)
        {
            Writer = writer;
            _isRedirected = isRedirected;
        }

        public TextWriter Writer { get; }
        public bool IsTerminal => !_isRedirected;
        public int Width => IsTerminal ? TryGet(() => Console.WindowWidth, 80) : 80;
        public int Height => IsTerminal ? TryGet(() => Console.WindowHeight, 24) : 24;
        public void SetEncoding(Encoding encoding) { }

        private static int TryGet(Func<int> get, int fallback)
        {
            try { return get(); }
            catch { return fallback; }
        }
    }

    private sealed class NullAnsiConsoleInput : IAnsiConsoleInput
    {
        public bool IsKeyAvailable() => false;
        public ConsoleKeyInfo? ReadKey(bool intercept) => null;
        public Task<ConsoleKeyInfo?> ReadKeyAsync(bool intercept, CancellationToken cancellationToken)
        {
            return Task.FromResult<ConsoleKeyInfo?>(null);
        }
    }
}
