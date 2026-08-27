using System;
using Microsoft.AzureRepos.Accounts;
using Xunit;

namespace Microsoft.AzureRepos.Tests.Accounts;

public class AccountBindingConfigurationTests
{
    [Theory]
    [InlineData(AccountBindingProperty.AccountId, "accountId")]
    [InlineData(AccountBindingProperty.UserName, "username")]
    internal void AccountBindingConfiguration_GetKey_Organization_CanonicalizesCase(
        AccountBindingProperty property,
        string propertyName)
    {
        AccountBindingTarget target = AccountBindingTarget.ForOrganization("MyOrg");

        string actual = AccountBindingConfiguration.GetKey(target, property);

        Assert.Equal($"credential.azrepos:org/myorg.{propertyName}", actual);
    }

    [Theory]
    [InlineData(AccountBindingProperty.AccountId, "accountId")]
    [InlineData(AccountBindingProperty.UserName, "username")]
    internal void AccountBindingConfiguration_GetKey_Tenant_UsesCanonicalGuid(
        AccountBindingProperty property,
        string propertyName)
    {
        var tenantId = Guid.Parse("AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE");
        AccountBindingTarget target = AccountBindingTarget.ForTenant(tenantId);

        string actual = AccountBindingConfiguration.GetKey(target, property);

        Assert.Equal(
            $"credential.azrepos:tenant/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee.{propertyName}",
            actual);
    }

    [Theory]
    [InlineData("credential.azrepos:org/MyOrg.accountId", AccountBindingProperty.AccountId)]
    [InlineData("Credential.AZREPOS:ORG/MyOrg.USERNAME", AccountBindingProperty.UserName)]
    internal void AccountBindingConfiguration_TryParseKey_Organization_PreservesLegacyCase(
        string key,
        AccountBindingProperty expectedProperty)
    {
        bool result = AccountBindingConfiguration.TryParseKey(
            key, out AccountBindingTarget target, out AccountBindingProperty property);

        Assert.True(result);
        Assert.Equal(AccountBindingTargetKind.Organization, target.Kind);
        Assert.Equal("MyOrg", target.Organization);
        Assert.Equal(expectedProperty, property);
    }

    [Fact]
    public void AccountBindingConfiguration_TryParseKey_Tenant_ReturnsGuid()
    {
        const string key =
            "credential.azrepos:tenant/AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE.accountId";
        var expected = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        bool result = AccountBindingConfiguration.TryParseKey(
            key, out AccountBindingTarget target, out AccountBindingProperty property);

        Assert.True(result);
        Assert.Equal(AccountBindingTargetKind.Tenant, target.Kind);
        Assert.Equal(expected, target.TenantId);
        Assert.Equal(AccountBindingProperty.AccountId, property);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("credential.azrepos:tenant/not-a-guid.username")]
    [InlineData("credential.azrepos:tenant/00000000-0000-0000-0000-000000000000.username")]
    [InlineData("credential.azrepos:org/.username")]
    [InlineData("credential.azrepos:org/a/b.username")]
    [InlineData("credential.azrepos:other/value.username")]
    [InlineData("credential.azrepos:org/name.other")]
    [InlineData("other.azrepos:org/name.username")]
    [InlineData("credential.https://example.com.username")]
    public void AccountBindingConfiguration_TryParseKey_Invalid_ReturnsFalse(string key)
    {
        bool result = AccountBindingConfiguration.TryParseKey(
            key, out AccountBindingTarget target, out _);

        Assert.False(result);
        Assert.Null(target);
    }
}
