using System;
using System.Threading.Tasks;
using GitCredentialManager.Authentication.Entra;
using Microsoft.AzureRepos.Accounts;
using Moq;
using Xunit;

namespace Microsoft.AzureRepos.Tests.Accounts;

public class AccountBindingTargetResolverTests
{
    [Fact]
    public async Task AccountBindingTargetResolver_ResolveOrganizationAsync_TenantAuthority_ReturnsTargets()
    {
        const string organization = "MyOrg";
        var tenantId = Guid.NewGuid();
        var organizationUri = new Uri("https://dev.azure.com/MyOrg");
        string authority = $"https://login.microsoftonline.com/{tenantId:D}";
        var azureDevOps = new Mock<IAzureDevOpsRestApi>(MockBehavior.Strict);
        azureDevOps.Setup(x => x.GetAuthorityBaseUri())
            .Returns(new Uri("https://login.microsoftonline.com"));
        azureDevOps.Setup(x => x.GetAuthorityAsync(organizationUri)).ReturnsAsync(authority);
        var resolver = new AccountBindingTargetResolver(
            azureDevOps.Object, Mock.Of<IEntraTenantResolver>());

        AccountBindingTargetResolution result =
            await resolver.ResolveOrganizationAsync(organization);

        Assert.Equal(AccountBindingTarget.ForOrganization(organization), result.Target);
        Assert.Equal(AccountBindingTarget.ForTenant(tenantId), result.Tenant);
        Assert.Equal(authority, result.Authority);
    }

    [Theory]
    [InlineData("common")]
    [InlineData("organizations")]
    [InlineData("consumers")]
    public async Task AccountBindingTargetResolver_ResolveOrganizationAsync_GenericAuthority_HasNoTenant(
        string authorityName)
    {
        const string organization = "org";
        var organizationUri = new Uri("https://dev.azure.com/org");
        string authority = $"https://login.microsoftonline.com/{authorityName}";
        var azureDevOps = new Mock<IAzureDevOpsRestApi>(MockBehavior.Strict);
        azureDevOps.Setup(x => x.GetAuthorityBaseUri())
            .Returns(new Uri("https://login.microsoftonline.com"));
        azureDevOps.Setup(x => x.GetAuthorityAsync(organizationUri)).ReturnsAsync(authority);
        var resolver = new AccountBindingTargetResolver(
            azureDevOps.Object, Mock.Of<IEntraTenantResolver>());

        AccountBindingTargetResolution result =
            await resolver.ResolveOrganizationAsync(organization);

        Assert.Null(result.Tenant);
        Assert.Equal(authority, result.Authority);
    }

    [Fact]
    public async Task AccountBindingTargetResolver_ResolveOrganizationAsync_CustomBasePath_ReturnsTenant()
    {
        const string organization = "org";
        var tenantId = Guid.NewGuid();
        var organizationUri = new Uri("https://dev.azure.com/org");
        var authorityBase = new Uri("https://example.com/identity/");
        string authority = new Uri(authorityBase, $"{tenantId:D}/v2.0").ToString();
        var azureDevOps = new Mock<IAzureDevOpsRestApi>(MockBehavior.Strict);
        azureDevOps.Setup(x => x.GetAuthorityBaseUri()).Returns(authorityBase);
        azureDevOps.Setup(x => x.GetAuthorityAsync(organizationUri)).ReturnsAsync(authority);
        var resolver = new AccountBindingTargetResolver(
            azureDevOps.Object, Mock.Of<IEntraTenantResolver>());

        AccountBindingTargetResolution result =
            await resolver.ResolveOrganizationAsync(organization);

        Assert.Equal(AccountBindingTarget.ForTenant(tenantId), result.Tenant);
    }

    [Fact]
    public async Task AccountBindingTargetResolver_ResolveTenantAsync_Name_ReturnsGuidTarget()
    {
        const string tenantName = "contoso.com";
        var tenantId = Guid.NewGuid();
        string authority = $"https://login.microsoftonline.com/{tenantId:D}/v2.0";
        var tenantResolver = new Mock<IEntraTenantResolver>(MockBehavior.Strict);
        tenantResolver.Setup(x => x.LookupAsync(tenantName))
            .ReturnsAsync(new EntraTenant {Id = tenantId, Authority = authority});
        var resolver = new AccountBindingTargetResolver(
            Mock.Of<IAzureDevOpsRestApi>(), tenantResolver.Object);

        AccountBindingTargetResolution result = await resolver.ResolveTenantAsync(tenantName);

        Assert.Equal(AccountBindingTarget.ForTenant(tenantId), result.Target);
        Assert.Equal(result.Target, result.Tenant);
        Assert.Equal(authority, result.Authority);
    }

    [Fact]
    public async Task AccountBindingTargetResolver_ResolveTenantAsync_Unknown_ReturnsNull()
    {
        const string tenantName = "unknown.example.com";
        var tenantResolver = new Mock<IEntraTenantResolver>(MockBehavior.Strict);
        tenantResolver.Setup(x => x.LookupAsync(tenantName)).ReturnsAsync((EntraTenant)null);
        var resolver = new AccountBindingTargetResolver(
            Mock.Of<IAzureDevOpsRestApi>(), tenantResolver.Object);

        AccountBindingTargetResolution result = await resolver.ResolveTenantAsync(tenantName);

        Assert.Null(result);
    }
}
