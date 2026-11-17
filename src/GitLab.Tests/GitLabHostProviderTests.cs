using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GitCredentialManager;
using GitCredentialManager.Authentication.OAuth;
using GitCredentialManager.Tests.Objects;
using Moq;
using Xunit;

namespace GitLab.Tests
{
    public class GitLabHostProviderTests
    {
        [Theory]
        [InlineData("https", "gitlab.com", true)]
        [InlineData("http", "gitlab.com", true)]
        [InlineData("https", "gitlab.example.com", true)]
        [InlineData("https", "github.com", false)]
        [InlineData("https", "github.example.com", false)]
        public void GitLabHostProvider_IsSupported(string protocol, string host, bool expected)
        {
            var request = new GitRequest(new Dictionary<string, string>
            {
                ["protocol"] = protocol,
                ["host"] = host,
            });

            var provider = new GitLabHostProvider(new TestCommandContext());
            Assert.Equal(expected, provider.IsSupported(request));
        }

        [Fact]
        public void GitLabHostProvider_GetSupportedAuthenticationModes_DotCom_ReturnsDotComModes()
        {
            Uri targetUri = GitLabConstants.GitLabDotCom;
            AuthenticationModes expected = GitLabConstants.DotComAuthenticationModes;

            var context = new TestCommandContext();
            var provider = new GitLabHostProvider(context);
            AuthenticationModes actual = provider.GetSupportedAuthenticationModes(targetUri);

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void GitLabHostProvider_GetSupportedAuthenticationModes_Custom_NoOAuthConfig_ReturnsBasicPat()
        {
            var targetUri = new Uri("https://gitlab.example.com");
            var expected = AuthenticationModes.Basic
                                          | AuthenticationModes.Pat;

            var context = new TestCommandContext();
            var provider = new GitLabHostProvider(context);
            AuthenticationModes actual = provider.GetSupportedAuthenticationModes(targetUri);

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void GitLabHostProvider_GetSupportedAuthenticationModes_Custom_WithOAuthConfig_ReturnsBasicPatBrowser()
        {
            var targetUri = new Uri("https://gitlab.example.com");
            var expected = AuthenticationModes.Basic
                                          | AuthenticationModes.Pat
                                          | AuthenticationModes.Browser;

            var context = new TestCommandContext();
            context.Environment.Variables[GitLabConstants.EnvironmentVariables.DevOAuthClientId] = "abcdefg1234567";

            var provider = new GitLabHostProvider(context);
            AuthenticationModes actual = provider.GetSupportedAuthenticationModes(targetUri);

            Assert.Equal(expected, actual);
        }

        [Fact]
        public async Task GitLabHostProvider_GetCredentialAsync_ExpiredAccessToken_RefreshesWithoutCheckingServer()
        {
            const string service = "https://gitlab.com";
            const string refreshService = "https://oauth-refresh-token.gitlab.com/";
            var request = new GitRequest(new Dictionary<string, string>
            {
                ["protocol"] = "https",
                ["host"] = "gitlab.com",
            });
            var expired = new CredentialMetadata { ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1) };

            var context = new TestCommandContext();
            context.CredentialStore.Add(service, "oauth2", "expired-access-token", expired);
            context.CredentialStore.Add(refreshService, "oauth2", "refresh-token");

            var auth = new Mock<IGitLabAuthentication>(MockBehavior.Strict);
            auth.Setup(x => x.GetOAuthTokenViaRefresh(It.IsAny<Uri>(), "refresh-token"))
                .ReturnsAsync(new OAuth2TokenResult("new-access-token", "bearer") { RefreshToken = "new-refresh-token" });

            var provider = new GitLabHostProvider(context, auth.Object);

            GitResponse response = await provider.GetCredentialAsync(request);

            Assert.Equal("new-access-token", response.Credential.Password);
            ((TestHttpMessageHandler)context.HttpClientFactory.MessageHandler).AssertNoRequests();
        }

        [Fact]
        public async Task GitLabHostProvider_GetCredentialAsync_ExpiredPersonalAccessToken_RemovesAndPrompts()
        {
            var request = new GitRequest(new Dictionary<string, string>
            {
                ["protocol"] = "https",
                ["host"] = "gitlab.com",
                ["username"] = "alice",
            });

            var context = new TestCommandContext();
            context.CredentialStore.Add("https://gitlab.com", "alice", "expired-pat",
                new CredentialMetadata { ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1) });

            var auth = new Mock<IGitLabAuthentication>(MockBehavior.Strict);
            auth.Setup(x => x.GetAuthenticationAsync(It.IsAny<Uri>(), "alice", It.IsAny<AuthenticationModes>()))
                .ReturnsAsync(new AuthenticationPromptResult(AuthenticationModes.Pat, new GitCredential("alice", "new-pat")));

            var provider = new GitLabHostProvider(context, auth.Object);

            GitResponse response = await provider.GetCredentialAsync(request);

            Assert.Equal(new GitCredential("alice", "new-pat"), response.Credential);
            Assert.False(context.CredentialStore.Contains("https://gitlab.com", "alice"));
            ((TestHttpMessageHandler)context.HttpClientFactory.MessageHandler).AssertNoRequests();
        }

        [Fact]
        public async Task GitLabHostProvider_GetCredentialAsync_ExpiredRefreshToken_DoesNotRefresh()
        {
            const string refreshService = "https://oauth-refresh-token.gitlab.com/";
            var request = new GitRequest(new Dictionary<string, string>
            {
                ["protocol"] = "https",
                ["host"] = "gitlab.com",
            });
            var expired = new CredentialMetadata { ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1) };

            var context = new TestCommandContext();
            context.CredentialStore.Add(refreshService, "oauth2", "expired-refresh-token", expired);

            var auth = new Mock<IGitLabAuthentication>(MockBehavior.Strict);
            auth.Setup(x => x.GetAuthenticationAsync(It.IsAny<Uri>(), It.IsAny<string>(), It.IsAny<AuthenticationModes>()))
                .ReturnsAsync(new AuthenticationPromptResult(AuthenticationModes.Browser));
            auth.Setup(x => x.GetOAuthTokenViaBrowserAsync(It.IsAny<Uri>(), It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync(new OAuth2TokenResult("new-access-token", "bearer") { RefreshToken = "new-refresh-token" });

            var provider = new GitLabHostProvider(context, auth.Object);

            GitResponse response = await provider.GetCredentialAsync(request);

            Assert.Equal("new-access-token", response.Credential.Password);
            auth.Verify(x => x.GetOAuthTokenViaRefresh(It.IsAny<Uri>(), It.IsAny<string>()), Times.Never);

            // New tokens are stored straight away
            Assert.True(context.CredentialStore.TryGet("https://gitlab.com", "oauth2", out var accessToken));
            Assert.Equal("new-access-token", accessToken.Password);
            Assert.True(context.CredentialStore.TryGet(refreshService, "oauth2", out var refreshToken));
            Assert.Equal("new-refresh-token", refreshToken.Password);
        }
    }
}
