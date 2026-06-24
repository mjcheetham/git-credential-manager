using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Animation;
using GitCredentialManager;
using GitCredentialManager.Authentication.OAuth;
using GitCredentialManager.Tests.Objects;
using Moq;
using Moq.Protected;
using Xunit;

namespace GitHub.Tests
{
    public class GitHubAuthenticationTests
    {
        [Fact]
        public async Task GitHubAuthentication_GetAuthenticationAsync_AuthenticationModesNone_ThrowsException()
        {
            var context = new TestCommandContext();
            var auth = new GitHubAuthentication(context);
            await Assert.ThrowsAsync<ArgumentException>("modes",
                () => auth.GetAuthenticationAsync(null, null, AuthenticationModes.None)
            );
        }

        [Theory]
        [InlineData(AuthenticationModes.Browser)]
        [InlineData(AuthenticationModes.Device)]
        public async Task GitHubAuthentication_GetAuthenticationAsync_SingleChoice_TerminalAndInteractionNotRequired(GitHub.AuthenticationModes modes)
        {
            var context = new TestCommandContext();
            context.Settings.IsTerminalPromptsEnabled = false;
            context.Settings.IsInteractionAllowed = false;
            context.SessionManager.IsDesktopSession = true; // necessary for browser
            context.FileSystem.Files["/usr/local/bin/GitHub.UI"] = new byte[0];
            context.FileSystem.Files[@"C:\Program Files\Git Credential Manager Core\GitHub.UI.exe"] = new byte[0];
            var auth = new GitHubAuthentication(context);
            var result = await auth.GetAuthenticationAsync(null, null, modes);
            Assert.Equal(modes, result.AuthenticationMode);
        }

        [Fact]
        public async Task GitHubAuthentication_GetAuthenticationAsync_TerminalPromptsDisabled_Throws()
        {
            var context = new TestCommandContext();
            context.Settings.IsTerminalPromptsEnabled = false;
            var auth = new GitHubAuthentication(context);
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => auth.GetAuthenticationAsync(null, null, AuthenticationModes.All)
            );
            Assert.Equal("Cannot prompt because terminal prompts have been disabled.", exception.Message);
        }

        // reproduces https://github.com/git-ecosystem/git-credential-manager/issues/453
        [Fact]
        public async Task GitHubAuthentication_GetAuthenticationAsync_Terminal()
        {
            var context = new TestCommandContext();
            context.FileSystem.Files["/usr/local/bin/GitHub.UI"] = new byte[0];
            context.FileSystem.Files[@"C:\Program Files\Git Credential Manager Core\GitHub.UI.exe"] = new byte[0];
            var auth = new GitHubAuthentication(context);
            context.Console.PushSelection(0);
            var result = await auth.GetAuthenticationAsync(null, null, AuthenticationModes.All);
            Assert.Equal(AuthenticationModes.Device, result.AuthenticationMode);
        }

        [Fact]
        public async Task GitHubAuthentication_GetAuthenticationAsync_Pat_DoesNotEchoToken()
        {
            const string token = "secret-token";
            var context = new TestCommandContext();
            context.Console.PushText(token);
            var auth = new GitHubAuthentication(context);

            AuthenticationPromptResult result = await auth.GetAuthenticationAsync(
                new Uri("https://github.com"), "octocat", AuthenticationModes.Pat);

            Assert.Equal(AuthenticationModes.Pat, result.AuthenticationMode);
            Assert.Equal(token, result.Credential.Password);
            Assert.DoesNotContain(token, context.Console.TtyConsole.Output);
        }

        [Fact]
        public async Task GitHubAuthentication_GetAuthenticationAsync_AuthenticationModesAll_RequiresInteraction()
        {
            var context = new TestCommandContext();
            context.Settings.IsInteractionAllowed = false;
            var auth = new GitHubAuthentication(context);
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => auth.GetAuthenticationAsync(new Uri("https://github.com"), null, AuthenticationModes.All)
            );
            Assert.Equal("Cannot prompt because user interactivity has been disabled.", exception.Message);
        }

        [Fact]
        public async Task GitHubAuthentication_GetAuthenticationAsync_Helper_Basic()
        {
            const string unixHelperPath = "/usr/local/bin/GitHub.UI";
            const string windowsHelperPath = @"C:\Program Files\Git Credential Manager\GitHub.UI.exe";
            string helperPath = PlatformUtils.IsWindows() ? windowsHelperPath : unixHelperPath;

            var context = new TestCommandContext();
            context.FileSystem.Files[helperPath] = Array.Empty<byte>();
            context.SessionManager.IsDesktopSession = true;
            context.Environment.Variables[GitHubConstants.EnvironmentVariables.AuthenticationHelper] = helperPath;
            var auth = new Mock<GitHubAuthentication>(MockBehavior.Strict, context);
            auth.Setup(x => x.InvokeHelperAsync(It.IsAny<string>(), "prompt --all", It.IsAny<StreamReader>(), It.IsAny<System.Threading.CancellationToken>()))
            .Returns(Task.FromResult<IDictionary<string, string>>(
                new Dictionary<string, string>
                {
                    ["mode"] = "basic",
                    ["username"] = "tim",
                    ["password"] = "hunter2"
                }));
            var result = await auth.Object.GetAuthenticationAsync(new Uri("https://github.com"), null, AuthenticationModes.All);
            Assert.Equal(AuthenticationModes.Basic, result.AuthenticationMode);
            Assert.Equal("tim", result.Credential.Account);
            Assert.Equal("hunter2", result.Credential.Password);
        }

        [Theory]
        [InlineData(false, true)]
        [InlineData(true, false)]
        public async Task GitHubAuthentication_GetOAuthTokenViaDeviceCodeAsync_Terminal_WritesPanelBeforePolling(
            bool guiPromptsEnabled, bool desktopSession)
        {
            const string verificationUrl = "https://github.com/login/device";
            const string userCode = "TEST-CODE";
            var context = new TestCommandContext
            {
                Settings = { IsGuiPromptsEnabled = guiPromptsEnabled },
                SessionManager = { IsDesktopSession = desktopSession },
            };
            var targetUri = new Uri("https://github.com");
            var deviceEndpoint = new Uri(targetUri, GitHubConstants.OAuthDeviceEndpointRelativeUri);
            var tokenEndpoint = new Uri(targetUri, GitHubConstants.OAuthTokenEndpointRelativeUri);
            using var handler = new TestHttpMessageHandler { ThrowOnUnexpectedRequest = true };
            context.HttpClientFactory.MessageHandler = handler;
            handler.Setup(HttpMethod.Post, deviceEndpoint, HttpStatusCode.OK,
                $$"""
                {
                    "device_code": "test-device-code",
                    "user_code": "{{userCode}}",
                    "verification_uri": "{{verificationUrl}}",
                    "expires_in": 900
                }
                """);
            handler.Setup(HttpMethod.Post, tokenEndpoint, _ =>
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
            using var auth = new GitHubAuthentication(context);

            OAuth2TokenResult result = await auth.GetOAuthTokenViaDeviceCodeAsync(targetUri, ["repo"]);

            Assert.Equal("test-token", result.AccessToken);
            Assert.Empty(context.Console.TtyConsole.Output);
            Assert.Empty(context.Console.WrittenMessages);
            Assert.Empty(context.Streams.Out.ToString());
            handler.AssertRequest(HttpMethod.Post, tokenEndpoint, 1);
        }
    }
}
