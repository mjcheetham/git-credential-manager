using System;
using System.Threading.Tasks;
using GitCredentialManager.Authentication.Entra;
using GitCredentialManager.Concurrency;
using GitCredentialManager.Tests.Objects;
using Moq;
using Xunit;

namespace GitCredentialManager.Tests.Authentication.Entra;

public class EntraAuthenticationTests
{
    [Theory]
    [InlineData(null, null, true)]
    [InlineData(null, "true", true)]
    [InlineData(null, "false", false)]
    [InlineData(null, "FALSE", false)]
    [InlineData(null, "0", false)]
    [InlineData(null, "no", false)]
    [InlineData(null, "off", false)]
    [InlineData(null, "invalid", true)]
    [InlineData(null, "", true)]
    [InlineData(null, " ", true)]
    [InlineData("true", null, true)]
    [InlineData("false", null, false)]
    [InlineData("FALSE", null, false)]
    [InlineData("0", null, false)]
    [InlineData("no", null, false)]
    [InlineData("off", null, false)]
    [InlineData("invalid", null, true)]
    [InlineData("", null, true)]
    [InlineData(" ", null, true)]
    [InlineData("false", "true", false)]
    [InlineData("true", "false", true)]
    [InlineData("invalid", "false", true)]
    [InlineData("", "false", true)]
    public void IsBrokerEnabled(string environmentValue, string gitConfigValue, bool expected)
    {
        var context = new TestCommandContext();
        if (gitConfigValue is not null)
        {
            string key =
                $"{Constants.GitConfiguration.Credential.SectionName}.{Constants.GitConfiguration.Credential.MsAuthUseBroker}";
            context.Git.Configuration.Global[key] = [gitConfigValue];
        }
        if (environmentValue is not null)
        {
            context.Environment.Variables[Constants.EnvironmentVariables.MsAuthUseBroker] = environmentValue;
        }
        var entraAuth = new EntraAuthentication(context);

        Assert.Equal(expected, entraAuth.IsBrokerEnabled());
    }

    [Fact]
    public async Task GetTokenForUserAsync_NoInteraction_ThrowsException()
    {
        const string authority = "https://login.microsoftonline.com/common";
        const string clientId = "C9E8FDA6-1D46-484C-917C-3DBD518F27C3";
        string[] scopes = ["user.read"];

        var context = new TestCommandContext
        {
            Settings = { IsInteractionAllowed = false },
        };
        var interactiveLock = new Mock<ILock>(MockBehavior.Strict);
        interactiveLock.Setup(x => x.Dispose());
        context.LockFactory
            .Setup(x => x.CreateLock(LockScope.CurrentUser, It.IsAny<string>()))
            .Returns(interactiveLock.Object);
        var config = new PublicClientConfig
        {
            ClientId = clientId,
        };
        var entraAuth = new EntraAuthentication(context, config);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => entraAuth.GetTokenForUserAsync(scopes, authority));
        interactiveLock.Verify(x => x.Dispose(), Times.Once);
        interactiveLock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetUserAccountsAsync_NoPublicClientConfig_ThrowsException()
    {
        var entraAuth = new EntraAuthentication(new TestCommandContext());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => entraAuth.GetUserAccountsAsync());
    }

    [Theory]
    [InlineData(
        "C9E8FDA6-1D46-484C-917C-3DBD518F27C3",
        null,
        null,
        "entra_interactive_9044cec50791ab855074691c91efc7666ff7dcbfe447cd4a1584834966e8f8fd"
    )]
    [InlineData(
        "C9E8FDA6-1D46-484C-917C-3DBD518F27C3",
        "https://login.microsoftonline.com/7114EF4A-7CB4-4F5F-88EF-1C21AC662D04",
        null,
        "entra_interactive_b6a378243c4f9b09eeb71dac92ef3f8b98b4d01fbcca45b430aa2a0f91760b5f")]
    [InlineData(
        "C9E8FDA6-1D46-484C-917C-3DBD518F27C3",
        null,
        "8F439F31-D7BC-47A7-A070-D97550E6E851.665B29EC-9C32-4E5C-B574-3BD751C58BD8",
        "entra_interactive_c290ae0d60b05d6b40c40992436f60338c160a96df3cd6ba6efa3520ac5fe0f8")]
    [InlineData(
        "C9E8FDA6-1D46-484C-917C-3DBD518F27C3",
        "https://login.microsoftonline.com/7114EF4A-7CB4-4F5F-88EF-1C21AC662D04",
        "8F439F31-D7BC-47A7-A070-D97550E6E851.665B29EC-9C32-4E5C-B574-3BD751C58BD8",
        "entra_interactive_f4def38b2b43aac4b0fbbfa96b39e03603c8681d2bb40ebd3ca3ee1c284ed51c")]
    [InlineData(
        "c9e8fda6-1d46-484c-917c-3dbd518f27c3",
        "hTTps://LoGiN.miCroSOftONliNe.CoM/7114ef4A-7cB4-4f5f-88ef-1c21ac662d04",
        "8f439f31-d7bc-47a7-a070-d97550e6e851.665b29ec-9c32-4e5c-b574-3bd751c58bd8",
        "entra_interactive_f4def38b2b43aac4b0fbbfa96b39e03603c8681d2bb40ebd3ca3ee1c284ed51c")]
    public void GetInteractiveLockName_ReturnsExpectedLockName(string clientId, string authority, string homeAccountId, string expectedName)
    {
        var entraAuth = new EntraAuthentication(
            new TestCommandContext(),
            new PublicClientConfig
            {
                ClientId = clientId,
            }
        );

        IEntraAccount account = null;
        if (homeAccountId is not null)
        {
            account = new EntraAccount(homeAccountId, "test-user");
        }

        string actualName = entraAuth.GetInteractiveLockName(authority, account);

        // Lock name should be deterministic:
        //   1. The literal prefix "entra_interactive_"
        //   2. Lowercased hex representation of the SHA-256 hash of:
        //       a. the client ID (required; lowercased)
        //       b. the authority (if provided; lowercased)
        //       c. home account ID (if provided; lowercased)
        Assert.Equal(expectedName, actualName);
    }
}
