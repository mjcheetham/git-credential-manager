using System;
using System.Threading.Tasks;
using GitCredentialManager.Authentication.Entra;
using GitCredentialManager.Tests.Objects;
using Xunit;

namespace GitCredentialManager.Tests.Authentication.Entra;

public class EntraAuthenticationTests
{
    [Fact]
    public async Task GetUserAccountsAsync_NoPublicClientConfig_ThrowsException()
    {
        var entraAuth = new EntraAuthentication(new TestCommandContext());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => entraAuth.GetUserAccountsAsync());
    }
}
