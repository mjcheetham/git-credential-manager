using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GitCredentialManager.Tests.Objects;
using GitCredentialManager.Tty;
using Spectre.Console;
using Xunit;

namespace GitCredentialManager.Tests.Tty;

public class AnsiConsoleFactoryTests
{
    [Fact]
    public void Create_ReturnsNonNullConsole()
    {
        IAnsiConsole console = AnsiConsoleFactory.CreateForTty();

        Assert.NotNull(console);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CreateForWriter_UsesAnsiSupportAndRedirection(bool isRedirected, bool ansiSupport)
    {
        using var writer = new StringWriter();
        IAnsiConsole console = AnsiConsoleFactory.CreateForWriter(writer, isRedirected, ansiSupport);

        Assert.Equal(ansiSupport, console.Profile.Capabilities.Ansi);
        Assert.Equal(!isRedirected, console.Profile.Out.IsTerminal);
        Assert.False(console.Profile.Capabilities.Interactive);

        console.MarkupLine("[red]error[/]");

        Assert.Contains("error", writer.ToString());
        if (isRedirected || !ansiSupport)
        {
            Assert.Equal(ColorSystem.NoColors, console.Profile.Capabilities.ColorSystem);
            Assert.DoesNotContain('\u001b', writer.ToString());
        }
    }

    [Fact]
    public void CreateForWriter_CustomWriter_UsesDetectedAnsi()
    {
        using var writer = new StringWriter();
        bool supportsAnsi = AnsiCapabilities.Create(writer).Ansi;
        IAnsiConsole console = AnsiConsoleFactory.CreateForWriter(writer);

        Assert.Equal(supportsAnsi, console.Profile.Capabilities.Ansi);
        Assert.Same(writer, console.Profile.Out.Writer);
        Assert.True(console.Profile.Out.Width > 0);
        Assert.True(console.Profile.Out.Height > 0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CreateForWriter_StandardStream_PreservesIdentityAndRedirection(bool standardError)
    {
        TextWriter writer = standardError ? Console.Error : Console.Out;
        bool isRedirected = standardError ? Console.IsErrorRedirected : Console.IsOutputRedirected;
        bool supportsAnsi = AnsiCapabilities.Create(writer).Ansi;

        IAnsiConsole console = AnsiConsoleFactory.CreateForWriter(writer);

        Assert.Same(writer, console.Profile.Out.Writer);
        Assert.Equal(!isRedirected, console.Profile.Out.IsTerminal);
        Assert.Equal(supportsAnsi, console.Profile.Capabilities.Ansi);
        if (isRedirected || !supportsAnsi)
        {
            Assert.Equal(ColorSystem.NoColors, console.Profile.Capabilities.ColorSystem);
        }
    }

    [Fact]
    public void CreateHeadless_ReadKey_ReturnsNull()
    {
        IAnsiConsole console = AnsiConsoleFactory.CreateHeadless();

        Assert.Null(console.Input.ReadKey(intercept: true));
    }

    [Fact]
    public void CreateHeadless_IsKeyAvailable_ReturnsFalse()
    {
        IAnsiConsole console = AnsiConsoleFactory.CreateHeadless();

        Assert.False(console.Input.IsKeyAvailable());
    }

    [Fact]
    public async Task CreateHeadless_ReadKeyAsync_ReturnsNull()
    {
        IAnsiConsole console = AnsiConsoleFactory.CreateHeadless();

        ConsoleKeyInfo? key = await console.Input.ReadKeyAsync(intercept: true, CancellationToken.None);

        Assert.Null(key);
    }

    [Fact]
    public async Task CreateHeadless_ReadKeyAsync_RespectsCancellation()
    {
        IAnsiConsole console = AnsiConsoleFactory.CreateHeadless();

        // Pre-cancelled token: the no-op input still returns null immediately
        // (it doesn't honour the token because there's nothing to block on),
        // so the contract here is "doesn't hang and produces a defined result".
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        ConsoleKeyInfo? key = await console.Input.ReadKeyAsync(intercept: true, cts.Token);

        Assert.Null(key);
    }

    [Fact]
    public void CreateHeadless_Output_IsNotTerminal()
    {
        IAnsiConsole console = AnsiConsoleFactory.CreateHeadless();

        Assert.False(console.Profile.Out.IsTerminal);
    }

    [Fact]
    public void CreateHeadless_Output_IsNoColor()
    {
        IAnsiConsole console = AnsiConsoleFactory.CreateHeadless();

        Assert.False(console.Profile.Capabilities.Ansi);
    }

    [Fact]
    public void CreateHeadless_Output_Write_DoesNotThrow()
    {
        IAnsiConsole console = AnsiConsoleFactory.CreateHeadless();

        // Discarded into TextWriter.Null; verifies the wiring doesn't trip on
        // ANSI / markup processing when the writer can't accept escape codes.
        console.MarkupLine("[red]error[/] in [bold]headless[/] mode");
    }

    [Fact]
    public void TestCommandContext_ExposesConsole()
    {
        ICommandContext ctx = new TestCommandContext();

        Assert.NotNull(ctx.Console);
    }
}
