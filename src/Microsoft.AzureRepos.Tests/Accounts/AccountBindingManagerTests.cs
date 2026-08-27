using System;
using System.Collections.Generic;
using System.Linq;
using GitCredentialManager;
using GitCredentialManager.Authentication.Entra;
using GitCredentialManager.Tests.Objects;
using Microsoft.AzureRepos.Accounts;
using Xunit;

namespace Microsoft.AzureRepos.Tests.Accounts;

public class AccountBindingManagerTests
{
    [Fact]
    public void AccountBindingManager_Set_Global_WritesCanonicalIdentity()
    {
        var git = new TestGit();
        var manager = CreateManager(git);
        AccountBindingTarget target = AccountBindingTarget.ForOrganization("MyOrg");
        var account = new EntraAccount("account-id", "alice@example.com");

        manager.Set(target, AccountBindingScope.Global, account);

        Assert.Equal("account-id", GetValue(
            git.Configuration.Global, "credential.azrepos:org/myorg.accountId"));
        Assert.Equal("alice@example.com", GetValue(
            git.Configuration.Global, "credential.azrepos:org/myorg.username"));
    }

    [Fact]
    public void AccountBindingManager_Set_AlreadyCorrect_DoesNotWrite()
    {
        var git = new TestGit();
        SetValue(git.Configuration.Global, "credential.azrepos:org/org.accountId", "account-id");
        SetValue(git.Configuration.Global, "credential.azrepos:org/org.username", "alice@example.com");
        var manager = CreateManager(git);

        manager.Set(
            AccountBindingTarget.ForOrganization("org"),
            AccountBindingScope.Global,
            new EntraAccount("account-id", "alice@example.com"));

        Assert.Equal(0, git.Configuration.SetCallCount);
        Assert.Equal(2, git.Configuration.Global.Count);
    }

    [Fact]
    public void AccountBindingManager_Set_Tenant_RoundTripsGuidTarget()
    {
        var tenantId = Guid.NewGuid();
        var git = new TestGit();
        var manager = CreateManager(git);

        manager.Set(
            AccountBindingTarget.ForTenant(tenantId),
            AccountBindingScope.Global,
            new EntraAccount("account-id", "alice@example.com"));

        AccountBinding binding = manager.Get(
            AccountBindingTarget.ForTenant(tenantId),
            AccountBindingScope.Global);
        Assert.Equal(AccountBindingTarget.ForTenant(tenantId), binding.Target);
        Assert.Equal("account-id", binding.Account.HomeAccountId);
    }

    [Fact]
    public void AccountBindingManager_Set_LegacyCase_RewritesCanonicalKeys()
    {
        var git = new TestGit();
        SetValue(git.Configuration.Global, "credential.azrepos:org/MyOrg.accountId", "account-id");
        SetValue(git.Configuration.Global, "credential.azrepos:org/MyOrg.username", "alice@example.com");
        var manager = CreateManager(git);

        manager.Set(
            AccountBindingTarget.ForOrganization("myorg"),
            AccountBindingScope.Global,
            new EntraAccount("account-id", "alice@example.com"));

        Assert.False(git.Configuration.Global.ContainsKey(
            "credential.azrepos:org/MyOrg.accountId"));
        Assert.False(git.Configuration.Global.ContainsKey(
            "credential.azrepos:org/MyOrg.username"));
        Assert.Equal("account-id", GetValue(
            git.Configuration.Global, "credential.azrepos:org/myorg.accountId"));
        Assert.Equal("alice@example.com", GetValue(
            git.Configuration.Global, "credential.azrepos:org/myorg.username"));
    }

    [Fact]
    public void AccountBindingManager_SetNoInherit_RemovesAccountIdAndWritesEmptyUserName()
    {
        var git = new TestGit();
        SetValue(git.Configuration.Local, "credential.azrepos:org/org.accountId", "account-id");
        SetValue(git.Configuration.Local, "credential.azrepos:org/org.username", "alice@example.com");
        var manager = CreateManager(git);

        manager.SetNoInherit(AccountBindingTarget.ForOrganization("org"));

        Assert.Single(git.Configuration.Local);
        Assert.Equal(string.Empty, GetValue(
            git.Configuration.Local, "credential.azrepos:org/org.username"));
    }

    [Fact]
    public void AccountBindingManager_SetNoInherit_OutsideRepository_Throws()
    {
        var manager = CreateManager(new TestGit(insideRepo: false));

        Assert.Throws<InvalidOperationException>(() =>
            manager.SetNoInherit(AccountBindingTarget.ForOrganization("org")));
    }

    [Fact]
    public void AccountBindingManager_SetNoInherit_AlreadyCorrect_DoesNotWrite()
    {
        var git = new TestGit();
        SetValue(git.Configuration.Local, "credential.azrepos:org/org.username", string.Empty);
        var manager = CreateManager(git);

        manager.SetNoInherit(AccountBindingTarget.ForOrganization("org"));

        Assert.Equal(0, git.Configuration.SetCallCount);
        Assert.Single(git.Configuration.Local);
    }

    [Fact]
    public void AccountBindingManager_Set_LocalOutsideRepository_Throws()
    {
        var manager = CreateManager(new TestGit(insideRepo: false));

        Assert.Throws<InvalidOperationException>(() =>
            manager.Set(
                AccountBindingTarget.ForOrganization("org"),
                AccountBindingScope.Local,
                new EntraAccount("account-id", "alice@example.com")));
    }

    [Fact]
    public void AccountBindingManager_Get_LegacyUserName_ReturnsBoundRecord()
    {
        var git = new TestGit();
        SetValue(git.Configuration.Global, "credential.azrepos:org/Org.username", "alice@example.com");
        var manager = CreateManager(git);

        AccountBinding binding = manager.Get(
            AccountBindingTarget.ForOrganization("org"),
            AccountBindingScope.Global);

        Assert.Equal(AccountBindingState.Bound, binding.State);
        Assert.True(binding.Account.IsLegacy);
        Assert.Equal("alice@example.com", binding.Account.UserName);
    }

    [Fact]
    public void AccountBindingManager_Get_EmptyLocalUserName_ReturnsNoInherit()
    {
        var git = new TestGit();
        SetValue(git.Configuration.Local, "credential.azrepos:org/org.username", string.Empty);
        var manager = CreateManager(git);

        AccountBinding binding = manager.Get(
            AccountBindingTarget.ForOrganization("org"),
            AccountBindingScope.Local);

        Assert.Equal(AccountBindingState.NoInherit, binding.State);
    }

    [Fact]
    public void AccountBindingManager_Get_EmptyGlobalUserName_ReturnsInvalid()
    {
        var git = new TestGit();
        SetValue(git.Configuration.Global, "credential.azrepos:org/org.username", string.Empty);
        var manager = CreateManager(git);

        AccountBinding binding = manager.Get(
            AccountBindingTarget.ForOrganization("org"),
            AccountBindingScope.Global);

        Assert.Equal(AccountBindingState.Invalid, binding.State);
    }

    [Fact]
    public void AccountBindingManager_Get_EmptyAccountId_ReturnsInvalid()
    {
        var git = new TestGit();
        SetValue(git.Configuration.Global, "credential.azrepos:org/org.accountId", string.Empty);
        SetValue(git.Configuration.Global, "credential.azrepos:org/org.username", "alice@example.com");
        var manager = CreateManager(git);

        AccountBinding binding = manager.Get(
            AccountBindingTarget.ForOrganization("org"),
            AccountBindingScope.Global);

        Assert.Equal(AccountBindingState.Invalid, binding.State);
        Assert.Equal(string.Empty, binding.RawAccountId);
    }

    [Fact]
    public void AccountBindingManager_Get_MultipleValues_ReturnsInvalid()
    {
        var git = new TestGit();
        git.Configuration.Global["credential.azrepos:org/org.username"] =
            new List<string> {"alice@example.com", "bob@example.com"};
        var manager = CreateManager(git);

        AccountBinding binding = manager.Get(
            AccountBindingTarget.ForOrganization("org"),
            AccountBindingScope.Global);

        Assert.Equal(AccountBindingState.Invalid, binding.State);
        Assert.Contains("multiple", binding.InvalidReason);
    }

    [Fact]
    public void AccountBindingManager_Get_IdenticalCaseVariants_Coalesces()
    {
        var git = new TestGit();
        SetBound(git.Configuration.Global, "MyOrg", "account-id", "alice@example.com");
        SetBound(git.Configuration.Global, "myorg", "account-id", "alice@example.com");
        var manager = CreateManager(git);

        AccountBinding binding = manager.Get(
            AccountBindingTarget.ForOrganization("MYORG"),
            AccountBindingScope.Global);

        Assert.Equal(AccountBindingState.Bound, binding.State);
        Assert.Equal("myorg", binding.Target.Organization);
        Assert.Equal("account-id", binding.Account.HomeAccountId);
    }

    [Fact]
    public void AccountBindingManager_Get_ConflictingCaseVariants_ReturnsInvalid()
    {
        var git = new TestGit();
        SetBound(git.Configuration.Global, "MyOrg", "first-id", "alice@example.com");
        SetBound(git.Configuration.Global, "myorg", "second-id", "bob@example.com");
        var manager = CreateManager(git);

        AccountBinding binding = manager.Get(
            AccountBindingTarget.ForOrganization("MYORG"),
            AccountBindingScope.Global);

        Assert.Equal(AccountBindingState.Invalid, binding.State);
        Assert.Contains("Conflicting", binding.InvalidReason);
    }

    [Fact]
    public void AccountBindingManager_Unset_RemovesAllCaseVariants()
    {
        var git = new TestGit();
        SetBound(git.Configuration.Global, "MyOrg", "first-id", "alice@example.com");
        SetBound(git.Configuration.Global, "myorg", "second-id", "bob@example.com");
        var manager = CreateManager(git);

        manager.Unset(
            AccountBindingTarget.ForOrganization("MYORG"),
            AccountBindingScope.Global);

        Assert.Empty(git.Configuration.Global);
    }

    [Fact]
    public void AccountBindingManager_Resolve_GlobalOrganizationBeatsLocalTenant()
    {
        var tenantId = Guid.NewGuid();
        var git = new TestGit();
        SetBound(git.Configuration.Global, "org", "org-id", "org@example.com");
        SetBound(git.Configuration.Local, tenantId, "tenant-id", "tenant@example.com");
        var manager = CreateManager(git);

        AccountBindingResult result = manager.Resolve("org", tenantId);

        Assert.Equal("org-id", result.SelectedBinding.Account.HomeAccountId);
        Assert.Equal(AccountBindingTargetKind.Organization, result.SelectedBinding.Target.Kind);
    }

    [Fact]
    public void AccountBindingManager_Resolve_LocalOrganizationBeatsGlobalOrganization()
    {
        var git = new TestGit();
        SetBound(git.Configuration.Local, "org", "local-id", "local@example.com");
        SetBound(git.Configuration.Global, "org", "global-id", "global@example.com");
        var manager = CreateManager(git);

        AccountBindingResult result = manager.Resolve("org");

        Assert.Equal("local-id", result.SelectedBinding.Account.HomeAccountId);
    }

    [Fact]
    public void AccountBindingManager_Resolve_LocalTenantBeatsGlobalTenant()
    {
        var tenantId = Guid.NewGuid();
        var git = new TestGit();
        SetBound(git.Configuration.Local, tenantId, "local-id", "local@example.com");
        SetBound(git.Configuration.Global, tenantId, "global-id", "global@example.com");
        var manager = CreateManager(git);

        AccountBindingResult result = manager.Resolve("org", tenantId);

        Assert.Equal("local-id", result.SelectedBinding.Account.HomeAccountId);
    }

    [Fact]
    public void AccountBindingManager_Resolve_GlobalTenant_IsFallback()
    {
        var tenantId = Guid.NewGuid();
        var git = new TestGit();
        SetBound(git.Configuration.Global, tenantId, "global-id", "global@example.com");
        var manager = CreateManager(git);

        AccountBindingResult result = manager.Resolve("org", tenantId);

        Assert.Equal("global-id", result.SelectedBinding.Account.HomeAccountId);
        Assert.Equal(AccountBindingTargetKind.Tenant, result.SelectedBinding.Target.Kind);
    }

    [Fact]
    public void AccountBindingManager_Resolve_LocalOrganizationNoInheritSuppressesTenant()
    {
        var tenantId = Guid.NewGuid();
        var git = new TestGit();
        SetValue(git.Configuration.Local, "credential.azrepos:org/org.username", string.Empty);
        SetBound(git.Configuration.Global, tenantId, "tenant-id", "tenant@example.com");
        var manager = CreateManager(git);

        AccountBindingResult result = manager.Resolve("org", tenantId);

        Assert.True(result.IsInheritanceSuppressed);
        Assert.Equal(
            AccountBindingTargetKind.Organization,
            result.SuppressingBinding.Target.Kind);
    }

    [Fact]
    public void AccountBindingManager_Resolve_LocalTenantNoInheritSuppressesGlobalTenant()
    {
        var tenantId = Guid.NewGuid();
        var git = new TestGit();
        SetValue(
            git.Configuration.Local,
            $"credential.azrepos:tenant/{tenantId:D}.username",
            string.Empty);
        SetBound(git.Configuration.Global, tenantId, "tenant-id", "tenant@example.com");
        var manager = CreateManager(git);

        AccountBindingResult result = manager.Resolve("org", tenantId);

        Assert.True(result.IsInheritanceSuppressed);
        Assert.Equal(AccountBindingTarget.ForTenant(tenantId), result.SuppressingBinding.Target);
    }

    [Fact]
    public void AccountBindingManager_Resolve_InvalidLocalOrganization_ContinuesToGlobal()
    {
        var git = new TestGit();
        SetValue(git.Configuration.Local, "credential.azrepos:org/org.accountId", string.Empty);
        SetValue(git.Configuration.Local, "credential.azrepos:org/org.username", "bad@example.com");
        SetBound(git.Configuration.Global, "org", "account-id", "alice@example.com");
        var manager = CreateManager(git);

        AccountBindingResult result = manager.Resolve("org");

        Assert.Equal("account-id", result.SelectedBinding.Account.HomeAccountId);
        Assert.Single(result.InvalidBindings);
        Assert.Equal(AccountBindingScope.Local, result.InvalidBindings[0].Scope);
    }

    [Fact]
    public void AccountBindingManager_GetAll_LocalOutsideRepository_Throws()
    {
        var manager = CreateManager(new TestGit(insideRepo: false));

        Assert.Throws<InvalidOperationException>(() =>
            manager.GetAll(AccountBindingScope.Local));
    }

    private static AccountBindingManager CreateManager(TestGit git)
    {
        return new AccountBindingManager(new NullTrace(), git);
    }

    private static void SetBound(
        IDictionary<string, IList<string>> config,
        string organization,
        string accountId,
        string userName)
    {
        SetValue(config, $"credential.azrepos:org/{organization}.accountId", accountId);
        SetValue(config, $"credential.azrepos:org/{organization}.username", userName);
    }

    private static void SetBound(
        IDictionary<string, IList<string>> config,
        Guid tenantId,
        string accountId,
        string userName)
    {
        SetValue(config, $"credential.azrepos:tenant/{tenantId:D}.accountId", accountId);
        SetValue(config, $"credential.azrepos:tenant/{tenantId:D}.username", userName);
    }

    private static void SetValue(
        IDictionary<string, IList<string>> config,
        string key,
        string value)
    {
        config[key] = new List<string> {value};
    }

    private static string GetValue(
        IDictionary<string, IList<string>> config,
        string key)
    {
        Assert.True(config.TryGetValue(key, out var values));
        return Assert.Single(values);
    }
}
