using Spectre.Console;
using Spectre.Console.Testing;
using Xunit;

namespace GitCredentialManager.Tests;

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
        var console = new TestConsole();
        console.Profile.Width = 120;

        console.Write(new QRCode("https://microsoft.com/devicelogin"));

        Assert.NotEmpty(console.Output.Trim());
    }

    [Fact]
    public void WriteQrCode_Extension_ProducesNonEmptyOutput()
    {
        var console = new TestConsole();
        console.Profile.Width = 120;

        console.WriteQrCode("https://microsoft.com/devicelogin");

        Assert.NotEmpty(console.Output.Trim());
    }

    [Fact]
    public void QRCode_SettingContent_RegeneratesCanvas()
    {
        var qr = new QRCode("a");

        var shortConsole = new TestConsole { Profile = { Width = 200 } };
        shortConsole.Write(qr);
        string shortOutput = shortConsole.Output;

        // A much longer payload needs a larger QR matrix, so the rendered output
        // should differ once the content is updated (canvas is regenerated).
        qr.Content = "https://microsoft.com/devicelogin?code=ABCDEFGHJ&extra=padding-to-grow-the-matrix";

        var longConsole = new TestConsole { Profile = { Width = 200 } };
        longConsole.Write(qr);
        string longOutput = longConsole.Output;

        Assert.NotEqual(shortOutput, longOutput);
    }
}
