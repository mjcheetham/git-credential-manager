using QRCoder;
using Spectre.Console;
using Spectre.Console.Rendering;
using Spectre.Console.Testing;
using Xunit;
using QRCode = GitCredentialManager.Tty.QRCode;

namespace GitCredentialManager.Tests.Tty;

public class QRCodeTests
{
    [Fact]
    public void QRCode_Content_RoundTrips()
    {
        var qr = new QRCode("https://example.com");

        Assert.Equal("https://example.com", qr.Content);
    }

    [Fact]
    public void QRCode_Render_ProducesNonEmptyOutput()
    {
        using var console = new TestConsole();
        console.Profile.Width = 120;

        console.Write(new QRCode("https://microsoft.com/devicelogin"));

        Assert.NotEmpty(console.Output.Trim());
    }

    [Theory]
    [InlineData(false, ColorSystem.TrueColor, true)]
    [InlineData(false, ColorSystem.NoColors, true)]
    [InlineData(true, ColorSystem.NoColors, true)]
    [InlineData(false, ColorSystem.TrueColor, false)]
    [InlineData(true, ColorSystem.NoColors, false)]
    [InlineData(true, ColorSystem.Standard, true)]
    [InlineData(true, ColorSystem.EightBit, true)]
    [InlineData(true, ColorSystem.TrueColor, true)]
    public void QRCode_Render_PacksModulesIntoUnstyledHalfBlocks(
        bool ansi, ColorSystem colorSystem, bool unicode)
    {
        const string content = "https://github.com/login/device";
        using var console = new TestConsole { EmitAnsiSequences = true };
        console.Profile.Width = 120;
        console.Profile.Capabilities.Ansi = ansi;
        console.Profile.Capabilities.ColorSystem = colorSystem;
        console.Profile.Capabilities.Unicode = unicode;

        using var generator = new QRCodeGenerator();
        using QRCodeData data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.L);
        int moduleWidth = data.ModuleMatrix[0].Length;
        int moduleHeight = data.ModuleMatrix.Count;

        var qr = new QRCode(content);
        RenderOptions options = RenderOptions.Create(console);
        Measurement measurement = qr.Measure(options, console.Profile.Width);
        console.Write(qr);

        Assert.Equal(moduleWidth, measurement.Max);
        Assert.Equal((moduleHeight + 1) / 2, console.Lines.Count);
        Assert.Equal(console.Lines.Count, Segment.SplitLines(qr.Render(options, measurement.Max)).Count);
        Assert.All(console.Lines, line => Assert.Equal(moduleWidth, line.Length));
        Assert.DoesNotContain('\u001b', console.Output);

        for (int y = 0; y < moduleHeight; y += 2)
        {
            for (int x = 0; x < moduleWidth; x++)
            {
                char block = console.Lines[y / 2][x];
                Assert.Contains(block, new[] { ' ', '\u2588', '\u2580', '\u2584' });
                Assert.Equal(data.ModuleMatrix[y][x], block is '\u2588' or '\u2580');
                Assert.Equal(y + 1 < moduleHeight && data.ModuleMatrix[y + 1][x],
                    block is '\u2588' or '\u2584');
            }
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void QRCode_SettingContent_RegeneratesRendering(bool ansi)
    {
        var qr = new QRCode("a");

        using var shortConsole = new TestConsole { Profile = { Width = 200 } };
        shortConsole.Profile.Capabilities.Ansi = ansi;
        shortConsole.Write(qr);
        string shortOutput = shortConsole.Output;

        // A much longer payload needs a larger QR matrix, so the rendered output
        // should differ once the content is updated.
        const string content = "https://microsoft.com/devicelogin?code=ABCDEFGHJ&extra=padding-to-grow-the-matrix";
        qr.Content = content;

        using var longConsole = new TestConsole { Profile = { Width = 200 } };
        longConsole.Profile.Capabilities.Ansi = ansi;
        longConsole.Write(qr);
        string longOutput = longConsole.Output;

        using var expectedConsole = new TestConsole { Profile = { Width = 200 } };
        expectedConsole.Profile.Capabilities.Ansi = ansi;
        expectedConsole.Write(new QRCode(content));

        Assert.Equal(content, qr.Content);
        Assert.Equal(expectedConsole.Output, longOutput);
        Assert.NotEqual(shortOutput, longOutput);
    }
}
