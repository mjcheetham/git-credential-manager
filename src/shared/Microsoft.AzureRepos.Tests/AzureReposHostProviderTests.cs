using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GitCredentialManager;
using GitCredentialManager.Authentication;
using GitCredentialManager.Tests;
using GitCredentialManager.Tests.Objects;
using Moq;
using Xunit;

namespace Microsoft.AzureRepos.Tests
{
    public class AzureReposHostProviderTests
    {
        private static readonly string HelperKey =
            $"{Constants.GitConfiguration.Credential.SectionName}.{Constants.GitConfiguration.Credential.Helper}";
        private static readonly string AzDevUseHttpPathKey =
            $"{Constants.GitConfiguration.Credential.SectionName}.https://dev.azure.com.{Constants.GitConfiguration.Credential.UseHttpPath}";
        private static readonly string OrgName = "org";

        [Fact]
        public void AzureReposProvider_IsSupported_AzureHost_UnencryptedHttp_ReturnsTrue()
        {
            var request = new GitRequest(new Dictionary<string, string>
            {
                ["protocol"] = "http",
                ["host"] = "dev.azure.com",
                ["path"] = "org/proj/_git/repo",
            });

            var provider = new AzureReposHostProvider(new TestCommandContext());

            // We report that we support unencrypted HTTP here so that we can fail and
            // show a helpful error message in the call to `CreateCredentialAsync` instead.
            Assert.True(provider.IsSupported(request));
        }

        [Fact]
        public void AzureReposProvider_IsSupported_VisualStudioHost_UnencryptedHttp_ReturnsTrue()
        {
            var request = new GitRequest(new Dictionary<string, string>
            {
                ["protocol"] = "http",
                ["host"] = "org.visualstudio.com",
            });

            var provider = new AzureReposHostProvider(new TestCommandContext());

            // We report that we support unencrypted HTTP here so that we can fail and
            // show a helpful error message in the call to `CreateCredentialAsync` instead.
            Assert.True(provider.IsSupported(request));
        }

        [Fact]
        public void AzureReposProvider_IsSupported_AzureHost_WithPath_ReturnsTrue()
        {
            var request = new GitRequest(new Dictionary<string, string>
            {
                ["protocol"] = "https",
                ["host"] = "dev.azure.com",
                ["path"] = "org/proj/_git/repo",
            });

            var provider = new AzureReposHostProvider(new TestCommandContext());
            Assert.True(provider.IsSupported(request));
        }

        [Fact]
        public void AzureReposProvider_IsSupported_AzureHost_MissingPath_ReturnsTrue()
        {
            var request = new GitRequest(new Dictionary<string, string>
            {
                ["protocol"] = "https",
                ["host"] = "dev.azure.com",
            });

            var provider = new AzureReposHostProvider(new TestCommandContext());
            Assert.True(provider.IsSupported(request));
        }

        [Fact]
        public void AzureReposProvider_IsSupported_VisualStudioHost_ReturnsTrue()
        {
            var request = new GitRequest(new Dictionary<string, string>
            {
                ["protocol"] = "https",
                ["host"] = "org.visualstudio.com",
            });

            var provider = new AzureReposHostProvider(new TestCommandContext());
            Assert.True(provider.IsSupported(request));
        }

        [Fact]
        public void AzureReposProvider_IsSupported_VisualStudioHost_MissingOrgInHost_ReturnsFalse()
        {
            var request = new GitRequest(new Dictionary<string, string>
            {
                ["protocol"] = "https",
                ["host"] = "visualstudio.com",
            });

            var provider = new AzureReposHostProvider(new TestCommandContext());
            Assert.False(provider.IsSupported(request));
        }

        [Fact]
        public void AzureReposProvider_IsSupported_NonAzureRepos_ReturnsFalse()
        {
            var request = new GitRequest(new Dictionary<string, string>
            {
                ["protocol"] = "https",
                ["host"] = "example.com",
                ["path"] = "org/proj/_git/repo",
            });

            var provider = new AzureReposHostProvider(new TestCommandContext());
            Assert.False(provider.IsSupported(request));
        }

        [Fact]
        public async Task AzureReposProvider_GetCredentialAsync_UnencryptedHttp_ThrowsException()
        {
            var request = new GitRequest(new Dictionary<string, string>
            {
                ["protocol"] = "http",
                ["host"] = "dev.azure.com",
                ["path"] = "org/proj/_git/repo"
            });

            var context = new TestCommandContext();
            var azDevOps = Mock.Of<IAzureDevOpsRestApi>();
            var msAuth = Mock.Of<IMicrosoftAuthentication>();
            var authorityCache = Mock.Of<IAzureDevOpsAuthorityCache>();
            var userMgr = Mock.Of<IAzureReposBindingManager>();

            var provider = new AzureReposHostProvider(context, azDevOps, msAuth, authorityCache, userMgr);

            await Assert.ThrowsAsync<Trace2Exception>(() => provider.GetCredentialAsync(request));
        }

        [Fact]
        public async Task AzureReposProvider_GetCredentialAsync_JwtMode_CachedAuthority_VsComUrlUser_ReturnsCredential()
        {
            var urlAccount = "jane.doe";

            var request = new GitRequest(new Dictionary<string, string>
            {
                ["protocol"] = "https",
                ["host"] = "org.visualstudio.com",
                ["username"] = urlAccount
            });

            var expectedOrgUri = new Uri("https://org.visualstudio.com");
            var remoteUri = new Uri("https://org.visualstudio.com/");
            var authorityUrl = "https://login.microsoftonline.com/common";
            var expectedClientId = AzureDevOpsConstants.AadClientId;
            var expectedRedirectUri = AzureDevOpsConstants.AadRedirectUri;
            var expectedScopes = AzureDevOpsConstants.AzureDevOpsDefaultScopes;
            var accessToken = "ACCESS-TOKEN";
            var expectedAccount = new MicrosoftAccount(homeAccountId: null, userName: urlAccount);
            var authResult = CreateAuthResult(urlAccount, accessToken);

            var context = new TestCommandContext();

            // Use OAuth Access Tokens
            context.Environment.Variables[AzureDevOpsConstants.EnvironmentVariables.CredentialType] =
                AzureDevOpsConstants.OAuthCredentialType;

            var azDevOpsMock = new Mock<IAzureDevOpsRestApi>(MockBehavior.Strict);
            azDevOpsMock.Setup(x => x.GetAuthorityAsync(expectedOrgUri)).ReturnsAsync(authorityUrl);

            var msAuthMock = new Mock<IMicrosoftAuthentication>(MockBehavior.Strict);
            msAuthMock.Setup(x => x.GetTokenForUserAsync(authorityUrl, expectedClientId, expectedRedirectUri, expectedScopes, expectedAccount, true))
                      .ReturnsAsync(authResult);

            var authorityCacheMock = new Mock<IAzureDevOpsAuthorityCache>(MockBehavior.Strict);
            authorityCacheMock.Setup(x => x.GetAuthority(OrgName)).Returns(authorityUrl);

            var userMgrMock = new Mock<IAzureReposBindingManager>(MockBehavior.Strict);

            var provider = new AzureReposHostProvider(context, azDevOpsMock.Object, msAuthMock.Object, authorityCacheMock.Object, userMgrMock.Object);

            var result = await provider.GetCredentialAsync(request);
            ICredential credential = result.Credential;

            Assert.NotNull(credential);
            Assert.Equal(urlAccount, credential.Account);
            Assert.Equal(accessToken, credential.Password);
        }

        [Fact]
        public async Task AzureReposProvider_GetCredentialAsync_JwtMode_CachedAuthority_DevAzureUrlUser_ReturnsCredential()
        {
            var urlAccount = "jane.doe";

            var request = new GitRequest(new Dictionary<string, string>
            {
                ["protocol"] = "https",
                ["host"] = "dev.azure.com",
                ["path"] = "org/project/_git/repo",
                ["username"] = urlAccount
            });

            var expectedOrgUri = new Uri("https://dev.azure.com/org");
            var remoteUri = new Uri("https://dev.azure.com/org/project/_git/repo");
            var authorityUrl = "https://login.microsoftonline.com/common";
            var expectedClientId = AzureDevOpsConstants.AadClientId;
            var expectedRedirectUri = AzureDevOpsConstants.AadRedirectUri;
            var expectedScopes = AzureDevOpsConstants.AzureDevOpsDefaultScopes;
            var accessToken = "ACCESS-TOKEN";
            var expectedAccount = new MicrosoftAccount(homeAccountId: null, userName: urlAccount);
            var authResult = CreateAuthResult(urlAccount, accessToken);

            var context = new TestCommandContext();

            // Use OAuth Access Tokens
            context.Environment.Variables[AzureDevOpsConstants.EnvironmentVariables.CredentialType] =
                AzureDevOpsConstants.OAuthCredentialType;

            var azDevOpsMock = new Mock<IAzureDevOpsRestApi>(MockBehavior.Strict);
            azDevOpsMock.Setup(x => x.GetAuthorityAsync(expectedOrgUri)).ReturnsAsync(authorityUrl);

            var msAuthMock = new Mock<IMicrosoftAuthentication>(MockBehavior.Strict);
            msAuthMock.Setup(x => x.GetTokenForUserAsync(authorityUrl, expectedClientId, expectedRedirectUri, expectedScopes, expectedAccount, true))
                      .ReturnsAsync(authResult);

            var authorityCacheMock = new Mock<IAzureDevOpsAuthorityCache>(MockBehavior.Strict);
            authorityCacheMock.Setup(x => x.GetAuthority(OrgName)).Returns(authorityUrl);

            var userMgrMock = new Mock<IAzureReposBindingManager>(MockBehavior.Strict);

            var provider = new AzureReposHostProvider(context, azDevOpsMock.Object, msAuthMock.Object, authorityCacheMock.Object, userMgrMock.Object);

            var result = await provider.GetCredentialAsync(request);
            ICredential credential = result.Credential;

            Assert.NotNull(credential);
            Assert.Equal(urlAccount, credential.Account);
            Assert.Equal(accessToken, credential.Password);
        }

        [Fact]
        public async Task AzureReposProvider_GetCredentialAsync_JwtMode_CachedAuthority_DevAzureUrlOrgName_ReturnsCredential()
        {
            var request = new GitRequest(new Dictionary<string, string>
            {
                ["protocol"] = "https",
                ["host"] = "dev.azure.com",
                ["username"] = "org"
            });

            var expectedOrgUri = new Uri("https://dev.azure.com/org");
            var authorityUrl = "https://login.microsoftonline.com/common";
            var expectedClientId = AzureDevOpsConstants.AadClientId;
            var expectedRedirectUri = AzureDevOpsConstants.AadRedirectUri;
            var expectedScopes = AzureDevOpsConstants.AzureDevOpsDefaultScopes;
            var accessToken = "ACCESS-TOKEN";
            IMicrosoftAccount expectedAccount = null;
            var account = "jane.doe";
            var authResult = CreateAuthResult(account, accessToken);

            var context = new TestCommandContext();

            // Use OAuth Access Tokens
            context.Environment.Variables[AzureDevOpsConstants.EnvironmentVariables.CredentialType] =
                AzureDevOpsConstants.OAuthCredentialType;

            var azDevOpsMock = new Mock<IAzureDevOpsRestApi>(MockBehavior.Strict);
            azDevOpsMock.Setup(x => x.GetAuthorityAsync(expectedOrgUri)).ReturnsAsync(authorityUrl);

            var msAuthMock = new Mock<IMicrosoftAuthentication>(MockBehavior.Strict);
            msAuthMock.Setup(x => x.GetTokenForUserAsync(authorityUrl, expectedClientId, expectedRedirectUri, expectedScopes, expectedAccount, true))
                      .ReturnsAsync(authResult);

            var authorityCacheMock = new Mock<IAzureDevOpsAuthorityCache>(MockBehavior.Strict);
            authorityCacheMock.Setup(x => x.GetAuthority(OrgName)).Returns(authorityUrl);

            var userMgrMock = new Mock<IAzureReposBindingManager>(MockBehavior.Strict);
            userMgrMock.Setup(x => x.GetBinding(OrgName)).Returns((AzureReposBinding)null);

            var provider = new AzureReposHostProvider(context, azDevOpsMock.Object, msAuthMock.Object, authorityCacheMock.Object, userMgrMock.Object);

            var result = await provider.GetCredentialAsync(request);
            ICredential credential = result.Credential;

            Assert.NotNull(credential);
            Assert.Equal(account, credential.Account);
            Assert.Equal(accessToken, credential.Password);
        }

        [Fact]
        public async Task AzureReposProvider_GetCredentialAsync_JwtMode_CachedAuthority_NoUser_ReturnsCredential()
        {
            var request = new GitRequest(new Dictionary<string, string>
            {
                ["protocol"] = "https",
                ["host"] = "dev.azure.com",
                ["path"] = "org/proj/_git/repo"
            });

            var expectedOrgUri = new Uri("https://dev.azure.com/org");
            var remoteUri = new Uri("https://dev.azure.com/org/proj/_git/repo");
            var authorityUrl = "https://login.microsoftonline.com/common";
            var expectedClientId = AzureDevOpsConstants.AadClientId;
            var expectedRedirectUri = AzureDevOpsConstants.AadRedirectUri;
            var expectedScopes = AzureDevOpsConstants.AzureDevOpsDefaultScopes;
            var accessToken = "ACCESS-TOKEN";
            IMicrosoftAccount expectedAccount = null;
            var account = "john.doe";
            var authResult = CreateAuthResult(account, accessToken);

            var context = new TestCommandContext();

            // Use OAuth Access Tokens
            context.Environment.Variables[AzureDevOpsConstants.EnvironmentVariables.CredentialType] =
                AzureDevOpsConstants.OAuthCredentialType;

            var azDevOpsMock = new Mock<IAzureDevOpsRestApi>(MockBehavior.Strict);

            var msAuthMock = new Mock<IMicrosoftAuthentication>(MockBehavior.Strict);
            msAuthMock.Setup(x => x.GetTokenForUserAsync(authorityUrl, expectedClientId, expectedRedirectUri, expectedScopes, expectedAccount, true))
                      .ReturnsAsync(authResult);

            var authorityCacheMock = new Mock<IAzureDevOpsAuthorityCache>(MockBehavior.Strict);
            authorityCacheMock.Setup(x => x.GetAuthority(OrgName)).Returns(authorityUrl);

            var userMgrMock = new Mock<IAzureReposBindingManager>(MockBehavior.Strict);
            userMgrMock.Setup(x => x.GetBinding(OrgName)).Returns((AzureReposBinding)null);

            var provider = new AzureReposHostProvider(context, azDevOpsMock.Object, msAuthMock.Object, authorityCacheMock.Object, userMgrMock.Object);

            var result = await provider.GetCredentialAsync(request);
            ICredential credential = result.Credential;

            Assert.NotNull(credential);
            Assert.Equal(account, credential.Account);
            Assert.Equal(accessToken, credential.Password);
        }

        [Fact]
        public async Task AzureReposProvider_GetCredentialAsync_JwtMode_CachedAuthority_BoundUser_ReturnsCredential()
        {

            var request = new GitRequest(new Dictionary<string, string>
            {
                ["protocol"] = "https",
                ["host"] = "dev.azure.com",
                ["path"] = "org/proj/_git/repo"
            });

            var expectedOrgUri = new Uri("https://dev.azure.com/org");
            var remoteUri = new Uri("https://dev.azure.com/org/proj/_git/repo");
            var authorityUrl = "https://login.microsoftonline.com/common";
            var expectedClientId = AzureDevOpsConstants.AadClientId;
            var expectedRedirectUri = AzureDevOpsConstants.AadRedirectUri;
            var expectedScopes = AzureDevOpsConstants.AzureDevOpsDefaultScopes;
            var accessToken = "ACCESS-TOKEN";
            var account = "john.doe";
            var expectedAccount = new MicrosoftAccount(homeAccountId: null, userName: account);
            var authResult = CreateAuthResult(account, accessToken);

            var context = new TestCommandContext();

            // Use OAuth Access Tokens
            context.Environment.Variables[AzureDevOpsConstants.EnvironmentVariables.CredentialType] =
                AzureDevOpsConstants.OAuthCredentialType;

            var azDevOpsMock = new Mock<IAzureDevOpsRestApi>(MockBehavior.Strict);

            var msAuthMock = new Mock<IMicrosoftAuthentication>(MockBehavior.Strict);
            msAuthMock.Setup(x => x.GetTokenForUserAsync(authorityUrl, expectedClientId, expectedRedirectUri, expectedScopes, expectedAccount, true))
                      .ReturnsAsync(authResult);

            var authorityCacheMock = new Mock<IAzureDevOpsAuthorityCache>(MockBehavior.Strict);
            authorityCacheMock.Setup(x => x.GetAuthority(OrgName)).Returns(authorityUrl);

            var userMgrMock = new Mock<IAzureReposBindingManager>(MockBehavior.Strict);
            userMgrMock.Setup(x => x.GetBinding(OrgName))
                .Returns(new AzureReposBinding(OrgName, account, null));

            var provider = new AzureReposHostProvider(context, azDevOpsMock.Object, msAuthMock.Object, authorityCacheMock.Object, userMgrMock.Object);

            var result = await provider.GetCredentialAsync(request);
            ICredential credential = result.Credential;

            Assert.NotNull(credential);
            Assert.Equal(account, credential.Account);
            Assert.Equal(accessToken, credential.Password);
        }

        [Fact]
        public async Task AzureReposProvider_GetCredentialAsync_JwtMode_NoCachedAuthority_NoUser_ReturnsCredential()
        {
            var request = new GitRequest(new Dictionary<string, string>
            {
                ["protocol"] = "https",
                ["host"] = "dev.azure.com",
                ["path"] = "org/proj/_git/repo"
            });

            var expectedOrgUri = new Uri("https://dev.azure.com/org");
            var remoteUri = new Uri("https://dev.azure.com/org/proj/_git/repo");
            var authorityUrl = "https://login.microsoftonline.com/common";
            var expectedClientId = AzureDevOpsConstants.AadClientId;
            var expectedRedirectUri = AzureDevOpsConstants.AadRedirectUri;
            var expectedScopes = AzureDevOpsConstants.AzureDevOpsDefaultScopes;
            var accessToken = "ACCESS-TOKEN";
            IMicrosoftAccount expectedAccount = null;
            var account = "john.doe";
            var authResult = CreateAuthResult(account, accessToken);

            var context = new TestCommandContext();

            // Use OAuth Access Tokens
            context.Environment.Variables[AzureDevOpsConstants.EnvironmentVariables.CredentialType] =
                AzureDevOpsConstants.OAuthCredentialType;

            var azDevOpsMock = new Mock<IAzureDevOpsRestApi>(MockBehavior.Strict);
            azDevOpsMock.Setup(x => x.GetAuthorityAsync(expectedOrgUri)).ReturnsAsync(authorityUrl);

            var msAuthMock = new Mock<IMicrosoftAuthentication>(MockBehavior.Strict);
            msAuthMock.Setup(x => x.GetTokenForUserAsync(authorityUrl, expectedClientId, expectedRedirectUri, expectedScopes, expectedAccount, true))
                      .ReturnsAsync(authResult);

            var authorityCacheMock = new Mock<IAzureDevOpsAuthorityCache>(MockBehavior.Strict);
            authorityCacheMock.Setup(x => x.GetAuthority(It.IsAny<string>())).Returns((string)null);
            authorityCacheMock.Setup(x => x.UpdateAuthority(OrgName, authorityUrl));

            var userMgrMock = new Mock<IAzureReposBindingManager>(MockBehavior.Strict);
            userMgrMock.Setup(x => x.GetBinding(OrgName)).Returns((AzureReposBinding)null);

            var provider = new AzureReposHostProvider(context, azDevOpsMock.Object, msAuthMock.Object, authorityCacheMock.Object, userMgrMock.Object);

            var result = await provider.GetCredentialAsync(request);
            ICredential credential = result.Credential;

            Assert.NotNull(credential);
            Assert.Equal(account, credential.Account);
            Assert.Equal(accessToken, credential.Password);
        }

        [Fact]
        public async Task AzureReposProvider_GetCredentialAsync_PatMode_OrgInUserName_NoExistingPat_GeneratesCredential()
        {
            var request = new GitRequest(new Dictionary<string, string>
            {
                ["protocol"] = "https",
                ["host"] = "dev.azure.com",
                ["username"] = "org"
            });

            var expectedOrgUri = new Uri("https://dev.azure.com/org");
            var authorityUrl = "https://login.microsoftonline.com/common";
            var expectedClientId = AzureDevOpsConstants.AadClientId;
            var expectedRedirectUri = AzureDevOpsConstants.AadRedirectUri;
            var expectedScopes = AzureDevOpsConstants.AzureDevOpsDefaultScopes;
            var accessToken = "ACCESS-TOKEN";
            IMicrosoftAccount expectedAccount = null;
            var personalAccessToken = "PERSONAL-ACCESS-TOKEN";
            var account = "john.doe";
            var authResult = CreateAuthResult(account, accessToken);

            var context = new TestCommandContext();

            var azDevOpsMock = new Mock<IAzureDevOpsRestApi>(MockBehavior.Strict);
            azDevOpsMock.Setup(x => x.GetAuthorityAsync(expectedOrgUri)).ReturnsAsync(authorityUrl);
            azDevOpsMock.Setup(x => x.CreatePersonalAccessTokenAsync(expectedOrgUri, accessToken, It.IsAny<IEnumerable<string>>()))
                        .ReturnsAsync(personalAccessToken);

            var msAuthMock = new Mock<IMicrosoftAuthentication>(MockBehavior.Strict);
            msAuthMock.Setup(x => x.GetTokenForUserAsync(authorityUrl, expectedClientId, expectedRedirectUri, expectedScopes, expectedAccount, true))
                      .ReturnsAsync(authResult);

            var authorityCacheMock = new Mock<IAzureDevOpsAuthorityCache>(MockBehavior.Strict);

            var userMgrMock = new Mock<IAzureReposBindingManager>(MockBehavior.Strict);

            var provider = new AzureReposHostProvider(context, azDevOpsMock.Object, msAuthMock.Object, authorityCacheMock.Object, userMgrMock.Object);

            var result = await provider.GetCredentialAsync(request);
            ICredential credential = result.Credential;

            Assert.NotNull(credential);
            Assert.Equal(account, credential.Account);
            Assert.Equal(personalAccessToken, credential.Password);
        }

        [Fact]
        public async Task AzureReposProvider_GetCredentialAsync_PatMode_NoExistingPat_GeneratesCredential()
        {
            var request = new GitRequest(new Dictionary<string, string>
            {
                ["protocol"] = "https",
                ["host"] = "dev.azure.com",
                ["path"] = "org/proj/_git/repo"
            });

            var expectedOrgUri = new Uri("https://dev.azure.com/org");
            var remoteUri = new Uri("https://dev.azure.com/org/proj/_git/repo");
            var authorityUrl = "https://login.microsoftonline.com/common";
            var expectedClientId = AzureDevOpsConstants.AadClientId;
            var expectedRedirectUri = AzureDevOpsConstants.AadRedirectUri;
            var expectedScopes = AzureDevOpsConstants.AzureDevOpsDefaultScopes;
            var accessToken = "ACCESS-TOKEN";
            IMicrosoftAccount expectedAccount = null;
            var personalAccessToken = "PERSONAL-ACCESS-TOKEN";
            var account = "john.doe";
            var authResult = CreateAuthResult(account, accessToken);

            var context = new TestCommandContext();

            var azDevOpsMock = new Mock<IAzureDevOpsRestApi>(MockBehavior.Strict);
            azDevOpsMock.Setup(x => x.GetAuthorityAsync(expectedOrgUri)).ReturnsAsync(authorityUrl);
            azDevOpsMock.Setup(x => x.CreatePersonalAccessTokenAsync(expectedOrgUri, accessToken, It.IsAny<IEnumerable<string>>()))
                        .ReturnsAsync(personalAccessToken);

            var msAuthMock = new Mock<IMicrosoftAuthentication>(MockBehavior.Strict);
            msAuthMock.Setup(x => x.GetTokenForUserAsync(authorityUrl, expectedClientId, expectedRedirectUri, expectedScopes, expectedAccount, true))
                      .ReturnsAsync(authResult);

            var authorityCacheMock = new Mock<IAzureDevOpsAuthorityCache>(MockBehavior.Strict);

            var userMgrMock = new Mock<IAzureReposBindingManager>(MockBehavior.Strict);

            var provider = new AzureReposHostProvider(context, azDevOpsMock.Object, msAuthMock.Object, authorityCacheMock.Object, userMgrMock.Object);

            var result = await provider.GetCredentialAsync(request);
            ICredential credential = result.Credential;

            Assert.NotNull(credential);
            Assert.Equal(account, credential.Account);
            Assert.Equal(personalAccessToken, credential.Password);
        }

        [Fact]
        public async Task AzureReposProvider_GetCredentialAsync_PatMode_ExistingPat_ReturnsExistingCredential()
        {
            var request = new GitRequest(new Dictionary<string, string>
            {
                ["protocol"] = "https",
                ["host"] = "dev.azure.com",
                ["path"] = "org/proj/_git/repo"
            });

            var remoteUri = new Uri("https://dev.azure.com/org/proj/_git/repo");
            var personalAccessToken = "PERSONAL-ACCESS-TOKEN";
            const string service = "https://dev.azure.com/org";
            const string account = "john.doe";

            var context = new TestCommandContext();

            context.CredentialStore.Add(service, account, personalAccessToken);

            var azDevOps = Mock.Of<IAzureDevOpsRestApi>();
            var msAuth = Mock.Of<IMicrosoftAuthentication>();
            var authorityCache = Mock.Of<IAzureDevOpsAuthorityCache>();
            var userMgr = Mock.Of<IAzureReposBindingManager>();

            var provider = new AzureReposHostProvider(context, azDevOps, msAuth, authorityCache, userMgr);

            var result = await provider.GetCredentialAsync(request);
            ICredential credential = result.Credential;

            Assert.NotNull(credential);
            Assert.Equal(account, credential.Account);
            Assert.Equal(personalAccessToken, credential.Password);
        }

        [Fact]
        public async Task AzureReposProvider_GetCredentialAsync_ManagedIdentity_ReturnsManagedIdCredential()
        {
            var request = new GitRequest(new Dictionary<string, string>
            {
                ["protocol"] = "https",
                ["host"] = "dev.azure.com",
                ["path"] = "org/proj/_git/repo"
            });

            const string accessToken = "MANAGED-IDENTITY-TOKEN";
            const string managedIdentity = "MANAGED-IDENTITY";

            var context = new TestCommandContext
            {
                Environment =
                {
                    Variables =
                    {
                        [AzureDevOpsConstants.EnvironmentVariables.ManagedIdentity] = managedIdentity
                    }
                }
            };

            var azDevOps = Mock.Of<IAzureDevOpsRestApi>();
            var authorityCache = Mock.Of<IAzureDevOpsAuthorityCache>();
            var userMgr = Mock.Of<IAzureReposBindingManager>();
            var msAuthMock = new Mock<IMicrosoftAuthentication>();

            msAuthMock.Setup(x => x.GetTokenForManagedIdentityAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new MockMsAuthResult { AccessToken = accessToken });

            var provider = new AzureReposHostProvider(context, azDevOps, msAuthMock.Object, authorityCache, userMgr);

            var result = await provider.GetCredentialAsync(request);
            ICredential credential = result.Credential;

            Assert.NotNull(credential);
            Assert.Equal(managedIdentity, credential.Account);
            Assert.Equal(accessToken, credential.Password);

            msAuthMock.Verify(
                x => x.GetTokenForManagedIdentityAsync(managedIdentity,
                    AzureDevOpsConstants.AzureDevOpsResourceId), Times.Once);
        }

        [Fact]
        public async Task AzureReposProvider_GetCredentialAsync_WorkloadFederation_Generic_ReturnsFederationOptions()
        {
            var request = new GitRequest(new Dictionary<string, string>
            {
                ["protocol"] = "https",
                ["host"] = "dev.azure.com",
                ["path"] = "org/proj/_git/repo"
            });

            const string accessToken = "FEDERATED-IDENTITY-TOKEN";
            const string wifScenario = "generic";
            const string tenantId = "00000000-0000-0000-0000-000000000000";
            const string clientId = "11111111-1111-1111-1111-111111111111";
            const string assertion = "CLIENT-ASSERTION";

            var context = new TestCommandContext
            {
                Environment =
                {
                    Variables =
                    {
                        [AzureDevOpsConstants.EnvironmentVariables.WorkloadFederation] = wifScenario,
                        [AzureDevOpsConstants.EnvironmentVariables.WorkloadFederationTenantId] = tenantId,
                        [AzureDevOpsConstants.EnvironmentVariables.WorkloadFederationClientId] = clientId,
                        [AzureDevOpsConstants.EnvironmentVariables.WorkloadFederationAssertion] = assertion,
                    }
                }
            };

            var azDevOps = Mock.Of<IAzureDevOpsRestApi>();
            var authorityCache = Mock.Of<IAzureDevOpsAuthorityCache>();
            var userMgr = Mock.Of<IAzureReposBindingManager>();
            var msAuthMock = new Mock<IMicrosoftAuthentication>();

            msAuthMock.Setup(x => x.GetTokenUsingWorkloadFederationAsync(
                    It.IsAny<MicrosoftWorkloadFederationOptions>(), It.IsAny<string[]>()))
                .ReturnsAsync(new MockMsAuthResult { AccessToken = accessToken });

            var provider = new AzureReposHostProvider(context, azDevOps, msAuthMock.Object, authorityCache, userMgr);

            GitResponse result = await provider.GetCredentialAsync(request);
            ICredential credential = result.Credential;

            Assert.NotNull(credential);
            Assert.Equal(clientId, credential.Account);
            Assert.Equal(accessToken, credential.Password);

            msAuthMock.Verify(
                x => x.GetTokenUsingWorkloadFederationAsync(
                    It.Is<MicrosoftWorkloadFederationOptions>(
                        fed => fed.Scenario == MicrosoftWorkloadFederationScenario.Generic &&
                              fed.TenantId == tenantId &&
                              fed.ClientId == clientId &&
                              fed.Audience == MicrosoftWorkloadFederationOptions.DefaultAudience &&
                              fed.GenericClientAssertion == assertion),
                    AzureDevOpsConstants.AzureDevOpsDefaultScopes), Times.Once);
        }

        [Fact]
        public async Task AzureReposProvider_GetCredentialAsync_WorkloadFederation_GenericFileAssertion_ReadsFromFile()
        {
            var request = new GitRequest(new Dictionary<string, string>
            {
                ["protocol"] = "https",
                ["host"] = "dev.azure.com",
                ["path"] = "org/proj/_git/repo"
            });

            const string accessToken = "FEDERATED-IDENTITY-TOKEN";
            const string wifScenario = "generic";
            const string tenantId = "00000000-0000-0000-0000-000000000000";
            const string clientId = "11111111-1111-1111-1111-111111111111";
            const string assertion = "CLIENT-ASSERTION-FROM-FILE";
            const string filePath = "/tmp/assertion-token.txt";

            var context = new TestCommandContext
            {
                Environment =
                {
                    Variables =
                    {
                        [AzureDevOpsConstants.EnvironmentVariables.WorkloadFederation] = wifScenario,
                        [AzureDevOpsConstants.EnvironmentVariables.WorkloadFederationTenantId] = tenantId,
                        [AzureDevOpsConstants.EnvironmentVariables.WorkloadFederationClientId] = clientId,
                        [AzureDevOpsConstants.EnvironmentVariables.WorkloadFederationAssertion] = $"file://{filePath}",
                    }
                }
            };

            context.FileSystem.Files[filePath] = System.Text.Encoding.UTF8.GetBytes(assertion);

            var azDevOps = Mock.Of<IAzureDevOpsRestApi>();
            var authorityCache = Mock.Of<IAzureDevOpsAuthorityCache>();
            var userMgr = Mock.Of<IAzureReposBindingManager>();
            var msAuthMock = new Mock<IMicrosoftAuthentication>();

            msAuthMock.Setup(x => x.GetTokenUsingWorkloadFederationAsync(
                    It.IsAny<MicrosoftWorkloadFederationOptions>(), It.IsAny<string[]>()))
                .ReturnsAsync(new MockMsAuthResult { AccessToken = accessToken });

            var provider = new AzureReposHostProvider(context, azDevOps, msAuthMock.Object, authorityCache, userMgr);

            GitResponse result = await provider.GetCredentialAsync(request);
            ICredential credential = result.Credential;

            Assert.NotNull(credential);
            Assert.Equal(clientId, credential.Account);
            Assert.Equal(accessToken, credential.Password);

            msAuthMock.Verify(
                x => x.GetTokenUsingWorkloadFederationAsync(
                    It.Is<MicrosoftWorkloadFederationOptions>(
                        fed => fed.Scenario == MicrosoftWorkloadFederationScenario.Generic &&
                              fed.TenantId == tenantId &&
                              fed.ClientId == clientId &&
                              fed.Audience == MicrosoftWorkloadFederationOptions.DefaultAudience &&
                              fed.GenericClientAssertion == assertion),
                    AzureDevOpsConstants.AzureDevOpsDefaultScopes), Times.Once);
        }

        [Fact]
        public async Task AzureReposProvider_GetCredentialAsync_WorkloadFederation_MI_ReturnsFederationOptions()
        {
            var request = new GitRequest(new Dictionary<string, string>
            {
                ["protocol"] = "https",
                ["host"] = "dev.azure.com",
                ["path"] = "org/proj/_git/repo"
            });

            const string accessToken = "FEDERATED-IDENTITY-TOKEN";
            const string wifScenario = "managedidentity";
            const string tenantId = "00000000-0000-0000-0000-000000000000";
            const string clientId = "11111111-1111-1111-1111-111111111111";
            const string managedIdentity = "22222222-2222-2222-2222-222222222222";

            var context = new TestCommandContext
            {
                Environment =
                {
                    Variables =
                    {
                        [AzureDevOpsConstants.EnvironmentVariables.WorkloadFederation] = wifScenario,
                        [AzureDevOpsConstants.EnvironmentVariables.WorkloadFederationTenantId] = tenantId,
                        [AzureDevOpsConstants.EnvironmentVariables.WorkloadFederationClientId] = clientId,
                        [AzureDevOpsConstants.EnvironmentVariables.WorkloadFederationManagedIdentity] = managedIdentity,
                    }
                }
            };

            var azDevOps = Mock.Of<IAzureDevOpsRestApi>();
            var authorityCache = Mock.Of<IAzureDevOpsAuthorityCache>();
            var userMgr = Mock.Of<IAzureReposBindingManager>();
            var msAuthMock = new Mock<IMicrosoftAuthentication>();

            msAuthMock.Setup(x => x.GetTokenUsingWorkloadFederationAsync(
                    It.IsAny<MicrosoftWorkloadFederationOptions>(), It.IsAny<string[]>()))
                .ReturnsAsync(new MockMsAuthResult { AccessToken = accessToken });

            var provider = new AzureReposHostProvider(context, azDevOps, msAuthMock.Object, authorityCache, userMgr);

            GitResponse result = await provider.GetCredentialAsync(request);
            ICredential credential = result.Credential;

            Assert.NotNull(credential);
            Assert.Equal(clientId, credential.Account);
            Assert.Equal(accessToken, credential.Password);

            msAuthMock.Verify(
                x => x.GetTokenUsingWorkloadFederationAsync(
                    It.Is<MicrosoftWorkloadFederationOptions>(
                        fed => fed.Scenario == MicrosoftWorkloadFederationScenario.ManagedIdentity &&
                              fed.TenantId == tenantId &&
                              fed.ClientId == clientId &&
                              fed.Audience == MicrosoftWorkloadFederationOptions.DefaultAudience &&
                              fed.ManagedIdentityId == managedIdentity),
                    AzureDevOpsConstants.AzureDevOpsDefaultScopes), Times.Once);
        }

        [Fact]
        public async Task AzureReposProvider_GetCredentialAsync_WorkloadFederation_GitHubActions_ReturnsFederationOptions()
        {
            var request = new GitRequest(new Dictionary<string, string>
            {
                ["protocol"] = "https",
                ["host"] = "dev.azure.com",
                ["path"] = "org/proj/_git/repo"
            });

            const string accessToken = "FEDERATED-IDENTITY-TOKEN";
            const string wifScenario = "githubactions";
            const string tenantId = "00000000-0000-0000-0000-000000000000";
            const string clientId = "11111111-1111-1111-1111-111111111111";
            const string ghRequestUrl = "https://token.actions.example.com/oidc/example?param=value";
            const string ghRequestToken = "OIDC-TOKEN";

            var context = new TestCommandContext
            {
                Environment =
                {
                    Variables =
                    {
                        [AzureDevOpsConstants.EnvironmentVariables.WorkloadFederation] = wifScenario,
                        [AzureDevOpsConstants.EnvironmentVariables.WorkloadFederationTenantId] = tenantId,
                        [AzureDevOpsConstants.EnvironmentVariables.WorkloadFederationClientId] = clientId,
                        [Constants.EnvironmentVariables.GitHubActionsTokenRequestUrl] = ghRequestUrl,
                        [Constants.EnvironmentVariables.GitHubActionsTokenRequestToken] = ghRequestToken,
                    }
                }
            };

            var azDevOps = Mock.Of<IAzureDevOpsRestApi>();
            var authorityCache = Mock.Of<IAzureDevOpsAuthorityCache>();
            var userMgr = Mock.Of<IAzureReposBindingManager>();
            var msAuthMock = new Mock<IMicrosoftAuthentication>();

            msAuthMock.Setup(x => x.GetTokenUsingWorkloadFederationAsync(
                    It.IsAny<MicrosoftWorkloadFederationOptions>(), It.IsAny<string[]>()))
                .ReturnsAsync(new MockMsAuthResult { AccessToken = accessToken });

            var provider = new AzureReposHostProvider(context, azDevOps, msAuthMock.Object, authorityCache, userMgr);

            GitResponse result = await provider.GetCredentialAsync(request);
            ICredential credential = result.Credential;

            Assert.NotNull(credential);
            Assert.Equal(clientId, credential.Account);
            Assert.Equal(accessToken, credential.Password);

            msAuthMock.Verify(
                x => x.GetTokenUsingWorkloadFederationAsync(
                    It.Is<MicrosoftWorkloadFederationOptions>(
                        fed => fed.Scenario == MicrosoftWorkloadFederationScenario.GitHubActions &&
                              fed.TenantId == tenantId &&
                              fed.ClientId == clientId &&
                              fed.GitHubTokenRequestUrl == new Uri(ghRequestUrl) &&
                              fed.GitHubTokenRequestToken == ghRequestToken &&
                              fed.Audience == MicrosoftWorkloadFederationOptions.DefaultAudience),
                    AzureDevOpsConstants.AzureDevOpsDefaultScopes), Times.Once);
        }

        [Fact]
        public async Task AzureReposProvider_GetCredentialAsync_ServicePrincipal_ReturnsSPCredential()
        {
            var request = new GitRequest(new Dictionary<string, string>
            {
                ["protocol"] = "https",
                ["host"] = "dev.azure.com",
                ["path"] = "org/proj/_git/repo"
            });

            const string accessToken = "SP-TOKEN";
            const string tenantId = "78B1822F-107D-40A3-A29C-AB68D8066074";
            const string clientId = "49B4DC1A-58A8-4EEE-A81B-616A40D0BA64";
            const string servicePrincipal = $"{tenantId}/{clientId}";
            const string servicePrincipalSecret = "CLIENT-SECRET";

            var context = new TestCommandContext
            {
                Environment =
                {
                    Variables =
                    {
                        [AzureDevOpsConstants.EnvironmentVariables.ServicePrincipalId] = servicePrincipal,
                        [AzureDevOpsConstants.EnvironmentVariables.ServicePrincipalSecret] = servicePrincipalSecret
                    }
                }
            };

            var azDevOps = Mock.Of<IAzureDevOpsRestApi>();
            var authorityCache = Mock.Of<IAzureDevOpsAuthorityCache>();
            var userMgr = Mock.Of<IAzureReposBindingManager>();
            var msAuthMock = new Mock<IMicrosoftAuthentication>();

            msAuthMock.Setup(x =>
                    x.GetTokenForServicePrincipalAsync(It.IsAny<MicrosoftServicePrincipalIdentity>(), It.IsAny<string[]>()))
                .ReturnsAsync(new MockMsAuthResult { AccessToken = accessToken });

            var provider = new AzureReposHostProvider(context, azDevOps, msAuthMock.Object, authorityCache, userMgr);

            var result = await provider.GetCredentialAsync(request);
            ICredential credential = result.Credential;

            Assert.NotNull(credential);
            Assert.Equal(clientId, credential.Account);
            Assert.Equal(accessToken, credential.Password);

            msAuthMock.Verify(x => x.GetTokenForServicePrincipalAsync(
                It.Is<MicrosoftServicePrincipalIdentity>(sp => sp.TenantId == tenantId && sp.Id == clientId),
                It.Is<string[]>(scopes => scopes.Length == 1 && scopes[0] == AzureDevOpsConstants.AzureDevOpsDefaultScopes[0])),
                Times.Once);
        }

        [Fact]
        public async Task AzureReposHostProvider_ConfigureAsync_UseHttpPathSetTrue_DoesNothing()
        {
            var context = new TestCommandContext();
            var provider = new AzureReposHostProvider(context);

            context.Git.Configuration.Global[AzDevUseHttpPathKey] = new List<string> {"true"};

            await provider.ConfigureAsync(ConfigurationTarget.User);

            Assert.Single(context.Git.Configuration.Global);
            Assert.True(context.Git.Configuration.Global.TryGetValue(AzDevUseHttpPathKey, out IList<string> actualValues));
            Assert.Single(actualValues);
            Assert.Equal("true", actualValues[0]);
        }

        [Fact]
        public async Task AzureReposHostProvider_ConfigureAsync_UseHttpPathSetFalse_SetsUseHttpPathTrue()
        {
            var context = new TestCommandContext();
            var provider = new AzureReposHostProvider(context);

            context.Git.Configuration.Global[AzDevUseHttpPathKey] = new List<string> {"false"};

            await provider.ConfigureAsync(ConfigurationTarget.User);

            Assert.Single(context.Git.Configuration.Global);
            Assert.True(context.Git.Configuration.Global.TryGetValue(AzDevUseHttpPathKey, out IList<string> actualValues));
            Assert.Single(actualValues);
            Assert.Equal("true", actualValues[0]);
        }

        [Fact]
        public async Task AzureReposHostProvider_ConfigureAsync_UseHttpPathUnset_SetsUseHttpPathTrue()
        {
            var context = new TestCommandContext();
            var provider = new AzureReposHostProvider(context);

            await provider.ConfigureAsync(ConfigurationTarget.User);

            Assert.Single(context.Git.Configuration.Global);
            Assert.True(context.Git.Configuration.Global.TryGetValue(AzDevUseHttpPathKey, out IList<string> actualValues));
            Assert.Single(actualValues);
            Assert.Equal("true", actualValues[0]);
        }

        [Fact]
        public async Task AzureReposHostProvider_UnconfigureAsync_UseHttpPathSet_RemovesEntry()
        {
            var context = new TestCommandContext();
            var provider = new AzureReposHostProvider(context);

            context.Git.Configuration.Global[AzDevUseHttpPathKey] = new List<string> {"true"};

            await provider.UnconfigureAsync(ConfigurationTarget.User);

            Assert.Empty(context.Git.Configuration.Global);
        }

        [WindowsFact]
        public async Task AzureReposHostProvider_UnconfigureAsync_System_Windows_UseHttpPathSetAndManagerHelper_DoesNotRemoveEntry()
        {
            var context = new TestCommandContext();
            var provider = new AzureReposHostProvider(context);

            context.Git.Configuration.System[HelperKey] = new List<string> {"manager"};
            context.Git.Configuration.System[AzDevUseHttpPathKey] = new List<string> {"true"};

            await provider.UnconfigureAsync(ConfigurationTarget.System);

            Assert.True(context.Git.Configuration.System.TryGetValue(AzDevUseHttpPathKey, out IList<string> actualValues));
            Assert.Single(actualValues);
            Assert.Equal("true", actualValues[0]);
        }

        [WindowsFact]
        public async Task AzureReposHostProvider_UnconfigureAsync_System_Windows_UseHttpPathSetAndManagerCoreHelper_DoesNotRemoveEntry()
        {
            var context = new TestCommandContext();
            var provider = new AzureReposHostProvider(context);

            context.Git.Configuration.System[HelperKey] = new List<string> {"manager-core"};
            context.Git.Configuration.System[AzDevUseHttpPathKey] = new List<string> {"true"};

            await provider.UnconfigureAsync(ConfigurationTarget.System);

            Assert.True(context.Git.Configuration.System.TryGetValue(AzDevUseHttpPathKey, out IList<string> actualValues));
            Assert.Single(actualValues);
            Assert.Equal("true", actualValues[0]);
        }

        [WindowsFact]
        public async Task AzureReposHostProvider_UnconfigureAsync_System_Windows_UseHttpPathSetNoManagerCoreHelper_RemovesEntry()
        {
            var context = new TestCommandContext();
            var provider = new AzureReposHostProvider(context);

            context.Git.Configuration.System[AzDevUseHttpPathKey] = new List<string> {"true"};

            await provider.UnconfigureAsync(ConfigurationTarget.System);

            Assert.Empty(context.Git.Configuration.System);
        }

        [WindowsFact]
        public async Task AzureReposHostProvider_UnconfigureAsync_User_Windows_UseHttpPathSetAndManagerHelper_RemovesEntry()
        {
            var context = new TestCommandContext();
            var provider = new AzureReposHostProvider(context);

            context.Git.Configuration.Global[HelperKey] = new List<string> {"manager"};
            context.Git.Configuration.Global[AzDevUseHttpPathKey] = new List<string> {"true"};

            await provider.UnconfigureAsync(ConfigurationTarget.User);

            Assert.False(context.Git.Configuration.Global.TryGetValue(AzDevUseHttpPathKey, out _));
        }

        [WindowsFact]
        public async Task AzureReposHostProvider_UnconfigureAsync_User_Windows_UseHttpPathSetAndManagerCoreHelper_RemovesEntry()
        {
            var context = new TestCommandContext();
            var provider = new AzureReposHostProvider(context);

            context.Git.Configuration.Global[HelperKey] = new List<string> {"manager-core"};
            context.Git.Configuration.Global[AzDevUseHttpPathKey] = new List<string> {"true"};

            await provider.UnconfigureAsync(ConfigurationTarget.User);

            Assert.False(context.Git.Configuration.Global.TryGetValue(AzDevUseHttpPathKey, out _));
        }

        [Theory]
        [InlineData(false, null, "")]
        [InlineData(false, null, "   ")]
        [InlineData(false, null, null)]
        [InlineData(false, null, "Basic realm=\"test\"")]
        [InlineData(false, null, "Basic realm=\"https://tfsprodwcus0.app.visualstudio.com/\"")]
        [InlineData(false, null, "TFS-Federated")]
        [InlineData(true, "https://login.microsoftonline.com/79c4d065-d599-442e-b0ea-c4ab36ad63c3",
            "Bearer authorization_uri=https://login.microsoftonline.com/79c4d065-d599-442e-b0ea-c4ab36ad63c3")]
        [InlineData(true, "https://login.microsoftonline.com/79c4d065-d599-442e-b0ea-c4ab36ad63c3",
            "bEArEr auThORizAtIoN_uRi=https://login.microsoftonline.com/79c4d065-d599-442e-b0ea-c4ab36ad63c3")]
        [InlineData(true, "https://login.microsoftonline.com/79c4d065-d599-442e-b0ea-c4ab36ad63c3",
            "\"Bearer authorization_uri=https://login.microsoftonline.com/79c4d065-d599-442e-b0ea-c4ab36ad63c3\"")]
        [InlineData(true, "https://login.microsoftonline.com/79c4d065-d599-442e-b0ea-c4ab36ad63c3",
            "'Bearer authorization_uri=https://login.microsoftonline.com/79c4d065-d599-442e-b0ea-c4ab36ad63c3'")]
        [InlineData(true, "https://login.microsoftonline.com/tenant1",
            "Bearer authorization_uri=https://login.microsoftonline.com/tenant1",
            "Bearer authorization_uri=https://login.microsoftonline.com/tenant2",
            "Bearer authorization_uri=https://login.microsoftonline.com/tenant3")]
        [InlineData(true, "https://login.microsoftonline.com/79c4d065-d599-442e-b0ea-c4ab36ad63c3",
            "Bearer authorization_uri=https://login.microsoftonline.com/79c4d065-d599-442e-b0ea-c4ab36ad63c3",
            "Basic realm=\"https://tfsprodwcus0.app.visualstudio.com/\"",
            "TFS-Federated")]
        [InlineData(true, "https://login.microsoftonline.com/79c4d065-d599-442e-b0ea-c4ab36ad63c3",
            "TFS-Federated",
            "Basic realm=\"https://tfsprodwcus0.app.visualstudio.com/\"",
            "Bearer authorization_uri=https://login.microsoftonline.com/79c4d065-d599-442e-b0ea-c4ab36ad63c3")]
        public void AzureReposHostProvider_TryGetAuthorityFromHeaders(
            bool expectedResult, string expectedAuthority, params string[] headers)
        {
            bool actualResult = AzureReposHostProvider.TryGetAuthorityFromHeaders(headers, out string actualAuthority);

            Assert.Equal(expectedResult, actualResult);
            Assert.Equal(expectedAuthority, actualAuthority);
        }

        // ------------------------------------------------------------
        // AuthMethod attribution — verifies the composite key emitted
        // on GitResponse for each top-level branch.
        //
        // The reframed cache-hit chart in the public usage survey site
        // depends on these strings; changing them is a wire-format
        // change for the published aggregates.
        // ------------------------------------------------------------

        [Theory]
        [InlineData(MicrosoftAuthenticationFlow.Silent,            "oauth-silent")]
        [InlineData(MicrosoftAuthenticationFlow.BrokerSilent,      "oauth-broker-silent")]
        [InlineData(MicrosoftAuthenticationFlow.BrokerInteractive, "oauth-broker-interactive")]
        [InlineData(MicrosoftAuthenticationFlow.EmbeddedWebView,   "oauth-browser-embedded")]
        [InlineData(MicrosoftAuthenticationFlow.SystemWebView,     "oauth-browser-system")]
        [InlineData(MicrosoftAuthenticationFlow.DeviceCode,        "oauth-device")]
        public async Task AzureReposProvider_GetCredentialAsync_OAuth_RecordsExpectedAuthMethod(
            MicrosoftAuthenticationFlow flow, string expectedAuthMethod)
        {
            var request = new GitRequest(new Dictionary<string, string>
            {
                ["protocol"] = "https",
                ["host"]     = "dev.azure.com",
                ["path"]     = "org/proj/_git/repo",
            });

            var context = new TestCommandContext
            {
                Environment =
                {
                    Variables =
                    {
                        // Force the OAuth branch (default is PAT).
                        [AzureDevOpsConstants.EnvironmentVariables.CredentialType]
                            = AzureDevOpsConstants.OAuthCredentialType,
                    },
                },
            };
            // Bind so the OAuth (non-PAT) branch is taken.
            var bindingManager = new Mock<IAzureReposBindingManager>();
            bindingManager.Setup(x => x.GetBinding(It.IsAny<string>()))
                .Returns(new AzureReposBinding("org", "user@contoso.com", null));

            var authorityCache = new Mock<IAzureDevOpsAuthorityCache>();
            authorityCache.Setup(x => x.GetAuthority(It.IsAny<string>()))
                .Returns("https://login.microsoftonline.com/contoso");

            var msAuthMock = new Mock<IMicrosoftAuthentication>();
            msAuthMock.Setup(x => x.GetTokenForUserAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Uri>(),
                    It.IsAny<string[]>(), It.IsAny<IMicrosoftAccount>(), It.IsAny<bool>()))
                .ReturnsAsync(new MockMsAuthResult
                {
                    Account = new MicrosoftAccount(homeAccountId: null, userName: "user@contoso.com"),
                    AccessToken = "TOKEN",
                    Flow = flow,
                });

            var provider = new AzureReposHostProvider(
                context, Mock.Of<IAzureDevOpsRestApi>(),
                msAuthMock.Object, authorityCache.Object, bindingManager.Object);

            GitResponse result = await provider.GetCredentialAsync(request);

            Assert.Equal(expectedAuthMethod, result.Metadata.AuthMethod);
        }

        [Theory]
        [InlineData(MicrosoftAuthenticationFlow.Silent,            "pat-silent")]
        [InlineData(MicrosoftAuthenticationFlow.BrokerSilent,      "pat-broker-silent")]
        [InlineData(MicrosoftAuthenticationFlow.BrokerInteractive, "pat-broker-interactive")]
        [InlineData(MicrosoftAuthenticationFlow.EmbeddedWebView,   "pat-browser-embedded")]
        [InlineData(MicrosoftAuthenticationFlow.SystemWebView,     "pat-browser-system")]
        [InlineData(MicrosoftAuthenticationFlow.DeviceCode,        "pat-device")]
        public async Task AzureReposProvider_GetCredentialAsync_FreshPat_RecordsExpectedAuthMethod(
            MicrosoftAuthenticationFlow flow, string expectedAuthMethod)
        {
            var request = new GitRequest(new Dictionary<string, string>
            {
                ["protocol"] = "https",
                ["host"]     = "dev.azure.com",
                ["path"]     = "org/proj/_git/repo",
            });

            var context = new TestCommandContext
            {
                Environment =
                {
                    Variables =
                    {
                        // Force the PAT branch.
                        [AzureDevOpsConstants.EnvironmentVariables.CredentialType]
                            = AzureDevOpsConstants.PatCredentialType,
                    },
                },
            };

            // No cached PAT → forces a fresh mint via MSAL.
            var azDevOps = new Mock<IAzureDevOpsRestApi>();
            azDevOps.Setup(x => x.GetAuthorityAsync(It.IsAny<Uri>()))
                .ReturnsAsync("https://login.microsoftonline.com/contoso");
            azDevOps.Setup(x => x.CreatePersonalAccessTokenAsync(
                    It.IsAny<Uri>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync("PAT-VALUE");

            var msAuthMock = new Mock<IMicrosoftAuthentication>();
            msAuthMock.Setup(x => x.GetTokenForUserAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Uri>(),
                    It.IsAny<string[]>(), It.IsAny<IMicrosoftAccount>(), It.IsAny<bool>()))
                .ReturnsAsync(new MockMsAuthResult
                {
                    Account = new MicrosoftAccount(homeAccountId: null, userName: "user@contoso.com"),
                    AccessToken = "AAD-TOKEN",
                    Flow = flow,
                });

            var provider = new AzureReposHostProvider(
                context, azDevOps.Object, msAuthMock.Object,
                Mock.Of<IAzureDevOpsAuthorityCache>(),
                Mock.Of<IAzureReposBindingManager>());

            GitResponse result = await provider.GetCredentialAsync(request);

            Assert.False(result.Metadata.FromCache);
            Assert.Equal(expectedAuthMethod, result.Metadata.AuthMethod);
        }

        [Theory]
        [InlineData(MicrosoftWorkloadFederationScenario.Generic,         "wif-generic")]
        [InlineData(MicrosoftWorkloadFederationScenario.ManagedIdentity, "wif-managed-identity")]
        [InlineData(MicrosoftWorkloadFederationScenario.GitHubActions,   "wif-github-actions")]
        public async Task AzureReposProvider_GetCredentialAsync_Wif_RecordsExpectedAuthMethod(
            MicrosoftWorkloadFederationScenario scenario, string expectedAuthMethod)
        {
            string scenarioStr = scenario switch
            {
                MicrosoftWorkloadFederationScenario.Generic         => "generic",
                MicrosoftWorkloadFederationScenario.ManagedIdentity => "mi",
                MicrosoftWorkloadFederationScenario.GitHubActions   => "githubactions",
                _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
            };

            var request = new GitRequest(new Dictionary<string, string>
            {
                ["protocol"] = "https",
                ["host"]     = "dev.azure.com",
                ["path"]     = "org/proj/_git/repo",
            });

            var context = new TestCommandContext
            {
                Environment =
                {
                    Variables =
                    {
                        [AzureDevOpsConstants.EnvironmentVariables.WorkloadFederation]         = scenarioStr,
                        [AzureDevOpsConstants.EnvironmentVariables.WorkloadFederationTenantId] = "tid",
                        [AzureDevOpsConstants.EnvironmentVariables.WorkloadFederationClientId] = "cid",
                        // Each scenario reads its own specifics, but provide all
                        // so the test data isn't sensitive to which subset.
                        [AzureDevOpsConstants.EnvironmentVariables.WorkloadFederationAssertion]
                            = "ASSERTION",
                        [AzureDevOpsConstants.EnvironmentVariables.WorkloadFederationManagedIdentity]
                            = "MID",
                    },
                },
            };

            // GitHubActions reads two extra envvars from the runner.
            if (scenario == MicrosoftWorkloadFederationScenario.GitHubActions)
            {
                context.Environment.Variables[Constants.EnvironmentVariables.GitHubActionsTokenRequestUrl]
                    = "https://example/actions/token";
                context.Environment.Variables[Constants.EnvironmentVariables.GitHubActionsTokenRequestToken]
                    = "GH-TOKEN";
            }

            var msAuthMock = new Mock<IMicrosoftAuthentication>();
            msAuthMock.Setup(x => x.GetTokenUsingWorkloadFederationAsync(
                    It.IsAny<MicrosoftWorkloadFederationOptions>(), It.IsAny<string[]>()))
                .ReturnsAsync(new MockMsAuthResult { AccessToken = "TOKEN" });

            var provider = new AzureReposHostProvider(
                context, Mock.Of<IAzureDevOpsRestApi>(),
                msAuthMock.Object,
                Mock.Of<IAzureDevOpsAuthorityCache>(),
                Mock.Of<IAzureReposBindingManager>());

            GitResponse result = await provider.GetCredentialAsync(request);

            Assert.Equal(expectedAuthMethod, result.Metadata.AuthMethod);
        }

        private static IMicrosoftAuthenticationResult CreateAuthResult(string upn, string token)
        {
            return new MockMsAuthResult
            {
                Account = new MicrosoftAccount(homeAccountId: null, userName: upn),
                AccessToken = token,
            };
        }

        private class MockMsAuthResult : IMicrosoftAuthenticationResult
        {
            public string AccessToken { get; set; }
            public IMicrosoftAccount Account { get; set; }
            public MicrosoftAuthenticationFlow Flow { get; set; }
        }
    }
}
