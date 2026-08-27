using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GitCredentialManager.Authentication.Entra;
using Microsoft.AzureRepos.Accounts;
using Moq;
using Xunit;

namespace Microsoft.AzureRepos.Tests.Accounts;

public class EntraAccountResolverTests
{
    [Fact]
    public async Task EntraAccountResolver_ResolveAsync_AccountId_DoesNotFallbackToUserName()
    {
        var cached = new EntraAccount("cached-id", "alice@example.com");
        var authentication = CreateAuthentication(cached);
        var resolver = new EntraAccountResolver(authentication.Object);
        var reference = new AccountReference("missing-id", "alice@example.com");

        EntraAccountResolution result = await resolver.ResolveAsync(reference);

        Assert.Equal(EntraAccountResolutionStatus.NotFound, result.Status);
        Assert.Null(result.Account);
        authentication.Verify(
            x => x.GetUserAccountsAsync(CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task EntraAccountResolver_ResolveAsync_LegacyReference_UsesUserName()
    {
        var cached = new EntraAccount("account-id", "alice@example.com");
        var authentication = CreateAuthentication(cached);
        var resolver = new EntraAccountResolver(authentication.Object);
        var reference = new AccountReference(null, "ALICE@example.com");

        EntraAccountResolution result = await resolver.ResolveAsync(reference);

        Assert.Equal(EntraAccountResolutionStatus.Found, result.Status);
        Assert.Same(cached, result.Account);
    }

    [Fact]
    public async Task EntraAccountResolver_ResolveByIdAsync_IsCaseInsensitive()
    {
        var cached = new EntraAccount("ACCOUNT-ID", "alice@example.com");
        var resolver = new EntraAccountResolver(CreateAuthentication(cached).Object);

        EntraAccountResolution result = await resolver.ResolveByIdAsync("account-id");

        Assert.Equal(EntraAccountResolutionStatus.Found, result.Status);
        Assert.Same(cached, result.Account);
    }

    [Fact]
    public async Task EntraAccountResolver_ResolveByUserNameAsync_Duplicate_IsAmbiguous()
    {
        var first = new EntraAccount("first-id", "alice@example.com");
        var second = new EntraAccount("second-id", "ALICE@example.com");
        var resolver = new EntraAccountResolver(CreateAuthentication(first, second).Object);

        EntraAccountResolution result =
            await resolver.ResolveByUserNameAsync("alice@example.com");

        Assert.Equal(EntraAccountResolutionStatus.Ambiguous, result.Status);
        Assert.Null(result.Account);
        Assert.Equal(2, result.Matches.Count);
    }

    [Fact]
    public async Task EntraAccountResolver_GetAccountsAsync_LoadsCacheOnce()
    {
        var cached = new EntraAccount("account-id", "alice@example.com");
        var authentication = CreateAuthentication(cached);
        var resolver = new EntraAccountResolver(authentication.Object);

        IReadOnlyList<IEntraAccount> first = await resolver.GetAccountsAsync();
        IReadOnlyList<IEntraAccount> second = await resolver.GetAccountsAsync();
        await resolver.ResolveByIdAsync(cached.HomeAccountId);
        await resolver.ResolveByUserNameAsync(cached.UserName);

        Assert.Same(first, second);
        authentication.Verify(
            x => x.GetUserAccountsAsync(CancellationToken.None), Times.Once);
    }

    private static Mock<IEntraAuthentication> CreateAuthentication(
        params IEntraAccount[] accounts)
    {
        var authentication = new Mock<IEntraAuthentication>(MockBehavior.Strict);
        authentication
            .Setup(x => x.GetUserAccountsAsync(CancellationToken.None))
            .ReturnsAsync(accounts);
        return authentication;
    }
}
