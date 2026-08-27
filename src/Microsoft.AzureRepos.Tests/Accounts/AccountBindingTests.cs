using System;
using Microsoft.AzureRepos.Accounts;
using Xunit;

namespace Microsoft.AzureRepos.Tests.Accounts;

public class AccountBindingTests
{
    [Fact]
    public void AccountBindingTarget_Organization_IsCaseInsensitive()
    {
        AccountBindingTarget first = AccountBindingTarget.ForOrganization("MyOrg");
        AccountBindingTarget second = AccountBindingTarget.ForOrganization("myorg");

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.Equal("MyOrg", first.Organization);
        Assert.Equal(AccountBindingTargetKind.Organization, first.Kind);
    }

    [Fact]
    public void AccountBindingTarget_Tenant_UsesGuid()
    {
        var tenantId = Guid.NewGuid();

        AccountBindingTarget target = AccountBindingTarget.ForTenant(tenantId);

        Assert.Equal(AccountBindingTargetKind.Tenant, target.Kind);
        Assert.Equal(tenantId, target.TenantId);
        Assert.Equal(tenantId.ToString("D"), target.ToString());
    }

    [Fact]
    public void AccountBindingTarget_EmptyTenant_Throws()
    {
        Assert.Throws<ArgumentException>(() => AccountBindingTarget.ForTenant(Guid.Empty));
    }

    [Fact]
    public void AccountReference_LegacyUsername_HasNoAccountId()
    {
        var account = new AccountReference(null, "alice@example.com");

        Assert.True(account.IsLegacy);
        Assert.Null(account.HomeAccountId);
        Assert.Equal("alice@example.com", account.UserName);
    }

    [Fact]
    public void AccountReference_NoIdentity_Throws()
    {
        Assert.Throws<ArgumentException>(() => new AccountReference(null, null));
    }

    [Fact]
    public void AccountBinding_NoInherit_IsAlwaysLocal()
    {
        AccountBinding binding = AccountBinding.NoInherit(
            AccountBindingTarget.ForOrganization("org"));

        Assert.Equal(AccountBindingState.NoInherit, binding.State);
        Assert.Equal(AccountBindingScope.Local, binding.Scope);
        Assert.Null(binding.Account);
        Assert.Null(binding.RawAccountId);
        Assert.Equal(string.Empty, binding.RawUserName);
    }

    [Fact]
    public void AccountBinding_Invalid_PreservesRawValues()
    {
        AccountBinding binding = AccountBinding.Invalid(
            AccountBindingTarget.ForOrganization("org"),
            AccountBindingScope.Global,
            string.Empty,
            "alice@example.com",
            "Account ID is empty.");

        Assert.Equal(AccountBindingState.Invalid, binding.State);
        Assert.Equal(string.Empty, binding.RawAccountId);
        Assert.Equal("alice@example.com", binding.RawUserName);
        Assert.Equal("Account ID is empty.", binding.InvalidReason);
    }

    [Fact]
    public void AccountBindingResult_SelectedLegacyBinding_NeedsMigration()
    {
        AccountBinding binding = AccountBinding.Bound(
            AccountBindingTarget.ForOrganization("org"),
            AccountBindingScope.Global,
            new AccountReference(null, "alice@example.com"));

        var result = new AccountBindingResult(selectedBinding: binding);

        Assert.True(result.HasBinding);
        Assert.True(result.NeedsMigration);
        Assert.False(result.IsInheritanceSuppressed);
        Assert.Same(binding, result.SelectedBinding);
    }

    [Fact]
    public void AccountBindingResult_SuppressingBinding_RecordsExactSource()
    {
        AccountBinding binding = AccountBinding.NoInherit(
            AccountBindingTarget.ForTenant(Guid.NewGuid()));

        var result = new AccountBindingResult(suppressingBinding: binding);

        Assert.False(result.HasBinding);
        Assert.True(result.IsInheritanceSuppressed);
        Assert.Same(binding, result.SuppressingBinding);
    }
}
