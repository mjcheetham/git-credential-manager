using GitCredentialManager.Tty;
using Spectre.Console;
using Spectre.Console.Testing;
using Xunit;

namespace GitCredentialManager.Tests.Tty;

public class DeviceCodePanelTests
{
    [Theory]
    [InlineData(80, 24)]
    [InlineData(80, 60)]
    [InlineData(120, 60)]
    public void Render_FitsPackedQrHeight(int terminalWidth, int terminalHeight)
    {
        using var console = new TestConsole();
        console.Profile.Width = terminalWidth;
        console.Profile.Height = terminalHeight;

        console.Write(new DeviceCodePanel("https://github.com/login/device", "TEST-CODE"));

        Assert.Equal(19, console.Lines.Count);
        Assert.All(console.Lines, line => Assert.Equal(80, line.Length));
        Assert.Contains("https://github.com/login/device", console.Output);
        Assert.Contains("TEST-CODE", console.Output);
        Assert.Contains("Press Ctrl+C to cancel.", console.Output);
    }

    [Theory]
    [InlineData(false, ColorSystem.NoColors)]
    [InlineData(false, ColorSystem.TrueColor)]
    [InlineData(true, ColorSystem.NoColors)]
    public void Render_WithoutAnsiColors_DoesNotStyleInstructions(bool ansi, ColorSystem colorSystem)
    {
        using var console = new TestConsole { EmitAnsiSequences = true };
        console.Profile.Capabilities.Ansi = ansi;
        console.Profile.Capabilities.ColorSystem = colorSystem;
        console.Profile.Capabilities.Unicode = true;

        console.Write(new DeviceCodePanel("https://github.com/login/device", "TEST-CODE"));

        Assert.StartsWith(ansi ? "\u256d" : "+", console.Output);
        Assert.Contains('\u2588', console.Output);
        Assert.DoesNotContain('\u001b', console.Output);
    }

    [Theory]
    [InlineData(ColorSystem.Standard)]
    [InlineData(ColorSystem.EightBit)]
    [InlineData(ColorSystem.TrueColor)]
    public void Render_WithAnsiColors_StylesInstructionsAndBorder(ColorSystem colorSystem)
    {
        using var console = new TestConsole { EmitAnsiSequences = true };
        console.Profile.Capabilities.Ansi = true;
        console.Profile.Capabilities.ColorSystem = colorSystem;
        console.Profile.Capabilities.Unicode = true;

        console.Write(new DeviceCodePanel("https://github.com/login/device", "TEST-CODE"));

        Assert.StartsWith("\u256d", console.Output);
        Assert.Contains('\u001b', console.Output);
        Assert.Contains("TEST-CODE", console.Output);
        Assert.Contains('\u2588', console.Output);
    }
}
