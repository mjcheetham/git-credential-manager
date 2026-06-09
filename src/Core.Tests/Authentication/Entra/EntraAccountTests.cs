using System;
using GitCredentialManager.Authentication.Entra;
using Xunit;

namespace GitCredentialManager.Tests.Authentication.Entra;

public class EntraAccountTests
{
    [Fact]
    public void Equals_DifferentCase_ReturnsTrue()
    {
        var left = new EntraAccount("HOME-ID", "User@Example.com");
        var right = new EntraAccount("home-id", "user@example.com");

        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void Equals_DifferentHomeId_ReturnsFalse()
    {
        var left = new EntraAccount("home-id-1", "user@example.com");
        var right = new EntraAccount("home-id-2", "user@example.com");

        Assert.NotEqual(left, right);
    }

    [Theory]
    // Classic Entra ID: two GUIDs
    [InlineData("00000000-0000-0000-0000-000000000002.00000000-0000-0000-0000-000000000001")]
    [InlineData("12345678-1234-1234-1234-123456789abc.fedcba98-7654-3210-fedc-ba9876543210")]
    // Non-GUID object id; tenant id is still a GUID — split on LAST dot
    [InlineData("contoso.onmicrosoft.com.00000000-0000-0000-0000-000000000001")]
    [InlineData("b2c-policy.tenant.00000000-0000-0000-0000-000000000001")]
    public void EntraAccount_IsHomeAccountIdShape_ObjectIdAnyAndGuidTenantId_True(string value)
    {
        Assert.True(EntraAccount.IsHomeAccountIdShape(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("alice@example.com")]
    [InlineData("alice")] // ADFS-shape / bare word
    [InlineData("00000000-0000-0000-0000-000000000002")] // single guid, no dot
    [InlineData("00000000-0000-0000-0000-000000000002.")] // trailing dot
    [InlineData(".00000000-0000-0000-0000-000000000001")] // leading dot
    [InlineData("00000000-0000-0000-0000-000000000002.not-a-guid")] // tid is not a GUID
    [InlineData("alice@example.com.contoso")] // tid is not a GUID
    public void EntraAccount_IsHomeAccountIdShape_NotEntraHomeAccountId_False(string value)
    {
        Assert.False(EntraAccount.IsHomeAccountIdShape(value));
    }

    [Fact]
    public void EntraAccount_FromIdentifier_HomeAccountIdShape_PopulatesHomeAccountId()
    {
        const string id = "00000000-0000-0000-0000-000000000002.00000000-0000-0000-0000-000000000001";

        EntraAccount account = EntraAccount.FromIdentifier(id);

        Assert.Equal(id, account.HomeAccountId);
        Assert.Null(account.UserName);
    }

    [Theory]
    [InlineData("alice@example.com")]
    [InlineData("alice@contoso.onmicrosoft.com")]
    [InlineData("alice")] // ambiguous → UserName
    [InlineData("contoso")] // ambiguous → UserName
    [InlineData("00000000-0000-0000-0000-000000000002")] // single guid, not HomeAccountId-shaped
    public void EntraAccount_FromIdentifier_NonHomeAccountIdShape_PopulatesUserName(string id)
    {
        EntraAccount account = EntraAccount.FromIdentifier(id);

        Assert.Null(account.HomeAccountId);
        Assert.Equal(id, account.UserName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EntraAccount_FromIdentifier_NullOrWhitespace_Throws(string id)
    {
        Assert.ThrowsAny<ArgumentException>(() => EntraAccount.FromIdentifier(id));
    }
}
