using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using GitCredentialManager.Authentication;
using GitCredentialManager.Authentication.OAuth;
using GitCredentialManager.Tests.Objects;
using Xunit;

namespace GitCredentialManager.Tests.Authentication;

public class OAuthAuthenticationTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task GetTokenByDeviceCodeAsync_Terminal_WritesPanelBeforePolling(
        bool guiPromptsEnabled, bool desktopSession)
    {
        const string verificationUrl = "https://example.com/device";
        const string userCode = "TEST-CODE";
        var context = new TestCommandContext
        {
            Settings = { IsGuiPromptsEnabled = guiPromptsEnabled },
            SessionManager = { IsDesktopSession = desktopSession },
        };
        var endpoints = new OAuth2ServerEndpoints(
            new Uri("https://example.com/authorize"),
            new Uri("https://example.com/token"))
        {
            DeviceAuthorizationEndpoint = new Uri("https://example.com/device/code"),
        };
        using var handler = new TestHttpMessageHandler { ThrowOnUnexpectedRequest = true };
        handler.Setup(HttpMethod.Post, endpoints.DeviceAuthorizationEndpoint, HttpStatusCode.OK,
            $$"""
            {
                "device_code": "test-device-code",
                "user_code": "{{userCode}}",
                "verification_uri": "{{verificationUrl}}",
                "expires_in": 900
            }
            """);
        handler.Setup(HttpMethod.Post, endpoints.TokenEndpoint, _ =>
        {
            string output = context.Console.StdErrConsole.Output;
            Assert.Contains(verificationUrl, output);
            Assert.Contains(userCode, output);
            Assert.Contains('\u2588', output);
            Assert.Contains("Press Ctrl+C to cancel.", output);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"access_token":"test-token","token_type":"Bearer"}"""),
            };
        });
        using var httpClient = new HttpClient(handler);
        var client = new OAuth2Client(httpClient, endpoints, "test-client");
        var auth = new OAuthAuthentication(context);

        OAuth2TokenResult result = await auth.GetTokenByDeviceCodeAsync(client, ["read"]);

        Assert.Equal("test-token", result.AccessToken);
        Assert.Empty(context.Console.TtyConsole.Output);
        Assert.Empty(context.Console.WrittenMessages);
        Assert.Empty(context.Streams.Out.ToString());
        handler.AssertRequest(HttpMethod.Post, endpoints.TokenEndpoint, 1);
    }
}
