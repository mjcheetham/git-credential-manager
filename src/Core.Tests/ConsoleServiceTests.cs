using System;
using System.IO;
using System.Text;
using GitCredentialManager.Tty;
using Spectre.Console;
using Xunit;

namespace GitCredentialManager.Tests;

public class ConsoleServiceTests
{
    [Fact]
    public void ConsoleService_WriteMethods_RouteToErrorConsoleWriter()
    {
        using var err = new StringWriter();
        var console = new ConsoleService(
            () => throw new InvalidOperationException("Output-only rendering must not open the TTY."),
            () => AnsiConsoleFactory.CreateForWriter(err, isRedirected: true, ansiSupport: false));

        console.Write(new Text("renderable-[marker]"));
        console.WriteInfo("info-[marker]");
        console.WriteWarning("warn-[marker]");
        console.WriteError("error-[marker]");
        console.WriteFatal("fatal-[marker]");
        console.WriteLine("line-[marker]");

        string output = err.ToString();
        Assert.Contains("renderable-[marker]", output);
        Assert.Contains("info-[marker]", output);
        Assert.Contains("warn-[marker]", output);
        Assert.Contains("error-[marker]", output);
        Assert.Contains("fatal-[marker]", output);
        Assert.Contains("line-[marker]", output);
    }

    [Fact]
    public void ConsoleService_WriteFatal_FlushesErrorWriter()
    {
        using var ms = new MemoryStream();
        using var sw = new StreamWriter(ms, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };

        var console = new ConsoleService(
            AnsiConsoleFactory.CreateHeadless,
            () => AnsiConsoleFactory.CreateForWriter(sw, isRedirected: true, ansiSupport: false));

        console.WriteFatal("fatal-marker");

        string output = new UTF8Encoding(false).GetString(ms.ToArray());
        Assert.Contains("fatal-marker", output);
    }
}
