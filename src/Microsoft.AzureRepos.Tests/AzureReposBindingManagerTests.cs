using System;
using System.Collections.Generic;
using System.Linq;
using GitCredentialManager.Authentication;
using GitCredentialManager.Authentication.Entra;
using GitCredentialManager.Tests.Objects;
using Xunit;

namespace Microsoft.AzureRepos.Tests;

public class AzureReposBindingManagerTests
{
    private const string OrgName = "contoso";
    private const string TenantId = "00000000-0000-0000-0000-000000000001";
    private const string HomeAccountId = "00000000-0000-0000-0000-000000000002.00000000-0000-0000-0000-000000000001";
    private const string UserName = "alice@example.com";

    private static string OrgAccountIdKey() => $"credential.azrepos:org/{OrgName}.accountid";
    private static string OrgUserNameKey()  => $"credential.azrepos:org/{OrgName}.username";
    private static string TenantAccountIdKey() => $"credential.azrepos:tenant/{TenantId}.accountid";
    private static string TenantUserNameKey()  => $"credential.azrepos:tenant/{TenantId}.username";

    private static EntraAccount Account(string hai = HomeAccountId, string upn = UserName) =>
        new(hai, upn);

    // -------- Bind --------

    [Fact]
    public void Bind_OrgGlobal_WithBothFields_WritesBothKeysAtGlobal()
    {
        var git = new TestGit();
        var manager = new AzureReposBindingManager(new NullTrace(), git);

        manager.Bind(AzureReposBindingScope.ForOrg(OrgName), Account());

        Assert.Equal(HomeAccountId, git.Configuration.Global[OrgAccountIdKey()].Single());
        Assert.Equal(UserName,      git.Configuration.Global[OrgUserNameKey()].Single());
        Assert.False(git.Configuration.Local.ContainsKey(OrgAccountIdKey()));
        Assert.False(git.Configuration.Local.ContainsKey(OrgUserNameKey()));
    }

    [Fact]
    public void Bind_HomeAccountIdOnly_WritesAccountIdAndClearsUserName()
    {
        var git = new TestGit();
        git.Configuration.Global[OrgUserNameKey()] = new List<string> { "stale@example.com" };
        var manager = new AzureReposBindingManager(new NullTrace(), git);

        manager.Bind(AzureReposBindingScope.ForOrg(OrgName), Account(upn: null));

        Assert.Equal(HomeAccountId, git.Configuration.Global[OrgAccountIdKey()].Single());
        Assert.False(git.Configuration.Global.ContainsKey(OrgUserNameKey()));
    }

    [Fact]
    public void Bind_UserNameOnly_WritesUserNameAndClearsAccountId()
    {
        var git = new TestGit();
        git.Configuration.Global[OrgAccountIdKey()] = new List<string> { "stale-id" };
        var manager = new AzureReposBindingManager(new NullTrace(), git);

        manager.Bind(AzureReposBindingScope.ForOrg(OrgName), Account(hai: null));

        Assert.Equal(UserName, git.Configuration.Global[OrgUserNameKey()].Single());
        Assert.False(git.Configuration.Global.ContainsKey(OrgAccountIdKey()));
    }

    [Fact]
    public void Bind_NeitherField_Throws()
    {
        var git = new TestGit();
        var manager = new AzureReposBindingManager(new NullTrace(), git);

        Assert.Throws<ArgumentException>(() =>
            manager.Bind(AzureReposBindingScope.ForOrg(OrgName), new EntraAccount(null, null)));
    }

    [Fact]
    public void Bind_OrgLocal_WritesAtLocal()
    {
        var git = new TestGit();
        var manager = new AzureReposBindingManager(new NullTrace(), git);

        manager.Bind(AzureReposBindingScope.ForOrg(OrgName, isLocal: true), Account());

        Assert.Equal(HomeAccountId, git.Configuration.Local[OrgAccountIdKey()].Single());
        Assert.False(git.Configuration.Global.ContainsKey(OrgAccountIdKey()));
    }

    [Fact]
    public void Bind_TenantGlobal_WritesAtGlobal()
    {
        var git = new TestGit();
        var manager = new AzureReposBindingManager(new NullTrace(), git);

        manager.Bind(AzureReposBindingScope.ForTenant(TenantId), Account());

        Assert.Equal(HomeAccountId, git.Configuration.Global[TenantAccountIdKey()].Single());
        Assert.Equal(UserName,      git.Configuration.Global[TenantUserNameKey()].Single());
    }

    [Fact]
    public void Bind_LocalScope_OutsideRepository_DoesNothing()
    {
        var git = new TestGit(insideRepo: false);
        var manager = new AzureReposBindingManager(new NullTrace(), git);

        manager.Bind(AzureReposBindingScope.ForOrg(OrgName, isLocal: true), Account());

        Assert.False(git.Configuration.Local.ContainsKey(OrgAccountIdKey()));
        Assert.False(git.Configuration.Local.ContainsKey(OrgUserNameKey()));
    }

    // -------- Unbind --------

    [Fact]
    public void Unbind_OrgGlobal_RemovesBothKeysAtGlobalOnly()
    {
        var git = new TestGit();
        git.Configuration.Global[OrgAccountIdKey()] = new List<string> { HomeAccountId };
        git.Configuration.Global[OrgUserNameKey()]  = new List<string> { UserName };
        git.Configuration.Local[OrgAccountIdKey()]  = new List<string> { HomeAccountId };
        git.Configuration.Local[OrgUserNameKey()]   = new List<string> { UserName };
        var manager = new AzureReposBindingManager(new NullTrace(), git);

        manager.Unbind(AzureReposBindingScope.ForOrg(OrgName));

        Assert.False(git.Configuration.Global.ContainsKey(OrgAccountIdKey()));
        Assert.False(git.Configuration.Global.ContainsKey(OrgUserNameKey()));
        Assert.True(git.Configuration.Local.ContainsKey(OrgAccountIdKey()));
        Assert.True(git.Configuration.Local.ContainsKey(OrgUserNameKey()));
    }

    [Fact]
    public void Unbind_LocalScope_OutsideRepository_DoesNothing()
    {
        var git = new TestGit(insideRepo: false);
        git.Configuration.Local[OrgAccountIdKey()] = new List<string> { HomeAccountId };
        var manager = new AzureReposBindingManager(new NullTrace(), git);

        manager.Unbind(AzureReposBindingScope.ForOrg(OrgName, isLocal: true));

        Assert.True(git.Configuration.Local.ContainsKey(OrgAccountIdKey()));
    }

    // -------- GetAccount --------

    [Fact]
    public void GetAccount_BothKeys_ReturnsBothFields()
    {
        var git = new TestGit();
        git.Configuration.Global[OrgAccountIdKey()] = new List<string> { HomeAccountId };
        git.Configuration.Global[OrgUserNameKey()]  = new List<string> { UserName };
        var manager = new AzureReposBindingManager(new NullTrace(), git);

        IEntraAccount account = manager.GetAccount(AzureReposBindingScope.ForOrg(OrgName));

        Assert.NotNull(account);
        Assert.Equal(HomeAccountId, account.HomeAccountId);
        Assert.Equal(UserName,      account.UserName);
    }

    [Fact]
    public void GetAccount_AccountIdOnly_ReturnsHomeAccountIdField()
    {
        var git = new TestGit();
        git.Configuration.Global[OrgAccountIdKey()] = new List<string> { HomeAccountId };
        var manager = new AzureReposBindingManager(new NullTrace(), git);

        IEntraAccount account = manager.GetAccount(AzureReposBindingScope.ForOrg(OrgName));

        Assert.NotNull(account);
        Assert.Equal(HomeAccountId, account.HomeAccountId);
        Assert.Null(account.UserName);
    }

    [Fact]
    public void GetAccount_UserNameOnly_ReturnsUserNameField()
    {
        // Both legacy `.username`-only and new UPN-only bindings read the same way.
        var git = new TestGit();
        git.Configuration.Global[OrgUserNameKey()] = new List<string> { UserName };
        var manager = new AzureReposBindingManager(new NullTrace(), git);

        IEntraAccount account = manager.GetAccount(AzureReposBindingScope.ForOrg(OrgName));

        Assert.NotNull(account);
        Assert.Null(account.HomeAccountId);
        Assert.Equal(UserName, account.UserName);
    }

    [Fact]
    public void GetAccount_TenantWithUserNameOnly_FallsBack()
    {
        // Tenant scope didn't exist before the rewrite, so this case only ever arises from
        // a user editing git config by hand — but the read path treats it symmetrically.
        var git = new TestGit();
        git.Configuration.Global[TenantUserNameKey()] = new List<string> { UserName };
        var manager = new AzureReposBindingManager(new NullTrace(), git);

        IEntraAccount account = manager.GetAccount(AzureReposBindingScope.ForTenant(TenantId));

        Assert.NotNull(account);
        Assert.Equal(UserName, account.UserName);
    }

    [Fact]
    public void GetAccount_NoEntry_ReturnsNull()
    {
        var git = new TestGit();
        var manager = new AzureReposBindingManager(new NullTrace(), git);

        Assert.Null(manager.GetAccount(AzureReposBindingScope.ForOrg(OrgName)));
        Assert.Null(manager.GetAccount(AzureReposBindingScope.ForTenant(TenantId)));
    }

    [Fact]
    public void GetAccount_LocalScope_OutsideRepository_ReturnsNull()
    {
        var git = new TestGit(insideRepo: false);
        git.Configuration.Local[OrgAccountIdKey()] = new List<string> { HomeAccountId };
        var manager = new AzureReposBindingManager(new NullTrace(), git);

        Assert.Null(manager.GetAccount(AzureReposBindingScope.ForOrg(OrgName, isLocal: true)));
    }

    // -------- GetBindings --------

    [Fact]
    public void GetBindings_EnumeratesEveryStoredBinding()
    {
        var git = new TestGit();
        git.Configuration.Global[OrgAccountIdKey()]    = new List<string> { HomeAccountId };
        git.Configuration.Global[OrgUserNameKey()]     = new List<string> { UserName };
        git.Configuration.Global[TenantAccountIdKey()] = new List<string> { "tenant-id" };
        git.Configuration.Local[OrgUserNameKey()]      = new List<string> { "local-only@example.com" };
        var manager = new AzureReposBindingManager(new NullTrace(), git);

        List<AzureReposBinding> bindings = manager.GetBindings().ToList();

        Assert.Equal(3, bindings.Count);
        Assert.Contains(bindings, b =>
            b.Scope is AzureReposBindingScope.Org { OrgName: OrgName, IsLocal: false } &&
            b.Account.HomeAccountId == HomeAccountId && b.Account.UserName == UserName);
        Assert.Contains(bindings, b =>
            b.Scope is AzureReposBindingScope.Tenant { TenantId: TenantId, IsLocal: false } &&
            b.Account.HomeAccountId == "tenant-id" && b.Account.UserName is null);
        Assert.Contains(bindings, b =>
            b.Scope is AzureReposBindingScope.Org { OrgName: OrgName, IsLocal: true } &&
            b.Account.HomeAccountId is null && b.Account.UserName == "local-only@example.com");
    }

    [Fact]
    public void GetBindings_OutsideRepository_SkipsLocalEntries()
    {
        var git = new TestGit(insideRepo: false);
        git.Configuration.Global[OrgAccountIdKey()] = new List<string> { HomeAccountId };
        git.Configuration.Local[OrgAccountIdKey()]  = new List<string> { "ignored" };
        var manager = new AzureReposBindingManager(new NullTrace(), git);

        List<AzureReposBinding> bindings = manager.GetBindings().ToList();

        Assert.Single(bindings);
        Assert.False(bindings[0].Scope.IsLocal);
    }

    // -------- ResolveAccountBinding (extension) --------

    [Fact]
    public void ResolveAccountBinding_PrefersLocalOrgOverGlobalOrg()
    {
        var git = new TestGit();
        git.Configuration.Global[OrgAccountIdKey()] = new List<string> { HomeAccountId };
        git.Configuration.Local[OrgAccountIdKey()]  = new List<string> { "local-id" };
        var manager = new AzureReposBindingManager(new NullTrace(), git);

        IEntraAccount resolved = manager.ResolveAccountBinding(OrgName, tenantId: null);

        Assert.Equal("local-id", resolved.HomeAccountId);
    }

    [Fact]
    public void ResolveAccountBinding_FallsBackToGlobalWhenNoLocal()
    {
        var git = new TestGit();
        git.Configuration.Global[OrgAccountIdKey()] = new List<string> { HomeAccountId };
        var manager = new AzureReposBindingManager(new NullTrace(), git);

        IEntraAccount resolved = manager.ResolveAccountBinding(OrgName, tenantId: null);

        Assert.Equal(HomeAccountId, resolved.HomeAccountId);
    }

    [Fact]
    public void ResolveAccountBinding_FallsBackToTenantWhenNoOrgBinding()
    {
        var git = new TestGit();
        git.Configuration.Global[TenantAccountIdKey()] = new List<string> { "tenant-id" };
        var manager = new AzureReposBindingManager(new NullTrace(), git);

        IEntraAccount resolved = manager.ResolveAccountBinding(OrgName, TenantId);

        Assert.Equal("tenant-id", resolved.HomeAccountId);
    }

    [Fact]
    public void ResolveAccountBinding_OrgWinsOverTenant()
    {
        var git = new TestGit();
        git.Configuration.Global[OrgAccountIdKey()]    = new List<string> { HomeAccountId };
        git.Configuration.Global[TenantAccountIdKey()] = new List<string> { "tenant-id" };
        var manager = new AzureReposBindingManager(new NullTrace(), git);

        IEntraAccount resolved = manager.ResolveAccountBinding(OrgName, TenantId);

        Assert.Equal(HomeAccountId, resolved.HomeAccountId);
    }

    [Fact]
    public void ResolveAccountBinding_LocalTenantWinsOverGlobalTenant()
    {
        var git = new TestGit();
        git.Configuration.Global[TenantAccountIdKey()] = new List<string> { "global-tenant" };
        git.Configuration.Local[TenantAccountIdKey()]  = new List<string> { "local-tenant" };
        var manager = new AzureReposBindingManager(new NullTrace(), git);

        IEntraAccount resolved = manager.ResolveAccountBinding(OrgName, TenantId);

        Assert.Equal("local-tenant", resolved.HomeAccountId);
    }

    [Fact]
    public void ResolveAccountBinding_NoMatch_ReturnsNull()
    {
        var git = new TestGit();
        var manager = new AzureReposBindingManager(new NullTrace(), git);

        Assert.Null(manager.ResolveAccountBinding(OrgName, TenantId));
    }
}
