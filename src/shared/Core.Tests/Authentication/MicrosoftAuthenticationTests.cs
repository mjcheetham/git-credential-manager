using System;
using System.Threading.Tasks;
using GitCredentialManager.Authentication;
using GitCredentialManager.Tests.Objects;
using Microsoft.Identity.Client.AppConfig;
using Xunit;

namespace GitCredentialManager.Tests.Authentication
{
    public class MicrosoftAuthenticationTests
    {
        [Fact]
        public async Task MicrosoftAuthentication_GetTokenForUserAsync_NoInteraction_ThrowsException()
        {
            const string authority = "https://login.microsoftonline.com/common";
            const string clientId = "C9E8FDA6-1D46-484C-917C-3DBD518F27C3";
            Uri redirectUri = new Uri("https://localhost");
            string[] scopes = {"user.read"};
            IMicrosoftAccount account = null; // No account to ensure we do not use an existing token

            var context = new TestCommandContext
            {
                Settings = {IsInteractionAllowed = false},
            };

            var msAuth = new MicrosoftAuthentication(context);

            await Assert.ThrowsAsync<Trace2InvalidOperationException>(
                () => msAuth.GetTokenForUserAsync(authority, clientId, redirectUri, scopes, account, false));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("system")]
        [InlineData("SYSTEM")]
        [InlineData("sYsTeM")]
        [InlineData("00000000-0000-0000-0000-000000000000")]
        [InlineData("id://00000000-0000-0000-0000-000000000000")]
        [InlineData("ID://00000000-0000-0000-0000-000000000000")]
        [InlineData("Id://00000000-0000-0000-0000-000000000000")]
        public void MicrosoftAuthentication_GetManagedIdentity_ValidSystemId_ReturnsSystemId(string str)
        {
            ManagedIdentityId actual = MicrosoftAuthentication.GetManagedIdentity(str);
            Assert.Equal(ManagedIdentityId.SystemAssigned, actual);
        }

        [Theory]
        [InlineData("8B49DCA0-1298-4A0D-AD6D-934E40230839")]
        [InlineData("id://8B49DCA0-1298-4A0D-AD6D-934E40230839")]
        [InlineData("ID://8B49DCA0-1298-4A0D-AD6D-934E40230839")]
        [InlineData("Id://8B49DCA0-1298-4A0D-AD6D-934E40230839")]
        [InlineData("resource://8B49DCA0-1298-4A0D-AD6D-934E40230839")]
        [InlineData("RESOURCE://8B49DCA0-1298-4A0D-AD6D-934E40230839")]
        [InlineData("rEsOuRcE://8B49DCA0-1298-4A0D-AD6D-934E40230839")]
        [InlineData("resource://00000000-0000-0000-0000-000000000000")]
        public void MicrosoftAuthentication_GetManagedIdentity_ValidUserIdByClientId_ReturnsUserId(string str)
        {
            ManagedIdentityId actual = MicrosoftAuthentication.GetManagedIdentity(str);
            Assert.NotNull(actual);
            Assert.NotEqual(ManagedIdentityId.SystemAssigned, actual);
        }

        [Theory]
        [InlineData("unknown://8B49DCA0-1298-4A0D-AD6D-934E40230839")]
        [InlineData("this is a string")]
        public void MicrosoftAuthentication_GetManagedIdentity_Invalid_ThrowsArgumentException(string str)
        {
            Assert.Throws<ArgumentException>(() => MicrosoftAuthentication.GetManagedIdentity(str));
        }
    }

    public class MicrosoftAccountTests
    {
        [Theory]
        // Classic Entra ID: two GUIDs
        [InlineData("00000000-0000-0000-0000-000000000002.00000000-0000-0000-0000-000000000001")]
        [InlineData("12345678-1234-1234-1234-123456789abc.fedcba98-7654-3210-fedc-ba9876543210")]
        // Non-GUID object id; tenant id is still a GUID — split on LAST dot
        [InlineData("contoso.onmicrosoft.com.00000000-0000-0000-0000-000000000001")]
        [InlineData("b2c-policy.tenant.00000000-0000-0000-0000-000000000001")]
        public void MicrosoftAccount_IsHomeAccountIdShape_ObjectIdAnyAndGuidTenantId_True(string value)
        {
            Assert.True(MicrosoftAccount.IsHomeAccountIdShape(value));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("alice@example.com")]
        [InlineData("alice")]                                                                  // ADFS-shape / bare word
        [InlineData("00000000-0000-0000-0000-000000000002")]                                   // single guid, no dot
        [InlineData("00000000-0000-0000-0000-000000000002.")]                                  // trailing dot
        [InlineData(".00000000-0000-0000-0000-000000000001")]                                  // leading dot
        [InlineData("00000000-0000-0000-0000-000000000002.not-a-guid")]                        // tid is not a GUID
        [InlineData("alice@example.com.contoso")]                                              // tid is not a GUID
        public void MicrosoftAccount_IsHomeAccountIdShape_NotEntraHomeAccountId_False(string value)
        {
            Assert.False(MicrosoftAccount.IsHomeAccountIdShape(value));
        }

        [Fact]
        public void MicrosoftAccount_FromIdentifier_HomeAccountIdShape_PopulatesHomeAccountId()
        {
            const string id = "00000000-0000-0000-0000-000000000002.00000000-0000-0000-0000-000000000001";

            MicrosoftAccount account = MicrosoftAccount.FromIdentifier(id);

            Assert.Equal(id, account.HomeAccountId);
            Assert.Null(account.UserName);
        }

        [Theory]
        [InlineData("alice@example.com")]
        [InlineData("alice@contoso.onmicrosoft.com")]
        [InlineData("alice")]                                  // ambiguous → UserName
        [InlineData("contoso")]                                // ambiguous → UserName
        [InlineData("00000000-0000-0000-0000-000000000002")]   // single guid, not HomeAccountId-shaped
        public void MicrosoftAccount_FromIdentifier_NonHomeAccountIdShape_PopulatesUserName(string id)
        {
            MicrosoftAccount account = MicrosoftAccount.FromIdentifier(id);

            Assert.Null(account.HomeAccountId);
            Assert.Equal(id, account.UserName);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void MicrosoftAccount_FromIdentifier_NullOrWhitespace_Throws(string id)
        {
            Assert.ThrowsAny<ArgumentException>(() => MicrosoftAccount.FromIdentifier(id));
        }
    }
}
