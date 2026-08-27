using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GitCredentialManager;
using GitCredentialManager.Authentication.Entra;
using GitCredentialManager.Commands;
using GitCredentialManager.Tests.Objects;
using Microsoft.AzureRepos.Accounts;
using Microsoft.AzureRepos.Commands;
using Moq;
using Xunit;

namespace Microsoft.AzureRepos.Tests.Commands;

public class AzureReposCommandTests
{
    [Fact]
    public void AzureReposCommand_Create_HasExpectedCommandsAndAliases()
    {
        ProviderCommand command = CreateCommand().Command.Create();

        Assert.Contains("azrepos", command.Aliases);
        Assert.Contains("ado", command.Aliases);
        Assert.Equal(
            new[] {"clear-cache", "list", "login", "logout", "set", "show", "unset"},
            command.Subcommands.Select(x => x.Name).OrderBy(x => x).ToArray());
    }

    [Fact]
    public void AzureReposCommand_SetNoInheritWithoutLocal_HasParseError()
    {
        ProviderCommand command = CreateCommand().Command.Create();

        var result = command.Parse(
            ["set", "--org", "org", "--no-inherit"]);

        Assert.Contains(result.Errors, x => x.Message.Contains("--no-inherit requires --local"));
    }

    [Fact]
    public void AzureReposCommand_SetNoInheritWithLocal_IsValid()
    {
        ProviderCommand command = CreateCommand().Command.Create();

        var result = command.Parse(
            ["set", "--org", "org", "--no-inherit", "--local"]);

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void AzureReposCommand_BooleanSwitchValue_HasParseError()
    {
        ProviderCommand command = CreateCommand().Command.Create();

        var result = command.Parse(
            ["set", "--org", "org", "--no-inherit", "false", "--local"]);

        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void AzureReposCommand_LogoutResourceSelectors_AreNotSupported()
    {
        ProviderCommand command = CreateCommand().Command.Create();

        var result = command.Parse(
            ["logout", "--id", "account-id", "--org", "org"]);

        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task AzureReposCommand_ListAsync_ShowsUnboundAccountAndStaleBinding()
    {
        var context = new TestCommandContext();
        var cached = new EntraAccount("cached-id", "cached@example.com");
        AccountBinding stale = AccountBinding.Bound(
            AccountBindingTarget.ForOrganization("org"),
            AccountBindingScope.Global,
            new AccountReference("stale-id", "stale@example.com"));
        var bindings = new Mock<IAccountBindingManager>(MockBehavior.Strict);
        bindings
            .Setup(x => x.GetAll(AccountBindingScope.Global, null))
            .Returns(new[] {stale});
        var accounts = new Mock<IEntraAccountResolver>(MockBehavior.Strict);
        accounts.Setup(x => x.GetAccountsAsync())
            .ReturnsAsync(new IEntraAccount[] {cached});
        accounts.Setup(x => x.ResolveAsync(stale.Account))
            .ReturnsAsync(new EntraAccountResolution(
                EntraAccountResolutionStatus.NotFound, null, Array.Empty<IEntraAccount>()));
        CommandContext command = CreateCommand(
            context: context,
            bindingManager: bindings.Object,
            accountResolver: accounts.Object);

        await command.Command.ListAsync(null, null, global: false, local: false);

        Assert.Contains(
            "cached@example.com (cached-id) [unbound]",
            context.Console.WrittenMessages);
        Assert.Contains(
            context.Console.WrittenMessages,
            x => x.Contains("stale@example.com") && x.Contains("unresolved"));
    }

    [Fact]
    public async Task AzureReposCommand_SetAsync_NoInherit_SetsLocalSentinel()
    {
        var context = new TestCommandContext();
        AccountBindingTarget target = AccountBindingTarget.ForOrganization("org");
        var targets = new Mock<IAccountBindingTargetResolver>(MockBehavior.Strict);
        targets.Setup(x => x.ResolveOrganizationAsync("org"))
            .ReturnsAsync(new AccountBindingTargetResolution(
                target, null, "https://login.microsoftonline.com/common"));
        var bindings = new Mock<IAccountBindingManager>(MockBehavior.Strict);
        bindings.Setup(x => x.SetNoInherit(target));
        CommandContext command = CreateCommand(
            context: context,
            bindingManager: bindings.Object,
            targetResolver: targets.Object);

        await command.Command.SetAsync(
            null, null, noInherit: true, "org", null, global: false, local: true);

        bindings.Verify(x => x.SetNoInherit(target), Times.Once);
        Assert.Contains(
            "Set local organization:org binding to no-inherit",
            context.Console.WrittenMessages);
    }

    [Fact]
    public async Task AzureReposCommand_SetAsync_AmbiguousUserName_Throws()
    {
        var target = AccountBindingTarget.ForOrganization("org");
        var targets = new Mock<IAccountBindingTargetResolver>(MockBehavior.Strict);
        targets.Setup(x => x.ResolveOrganizationAsync("org"))
            .ReturnsAsync(new AccountBindingTargetResolution(
                target, null, "https://login.microsoftonline.com/common"));
        var accounts = new Mock<IEntraAccountResolver>(MockBehavior.Strict);
        accounts.Setup(x => x.ResolveByUserNameAsync("alice@example.com"))
            .ReturnsAsync(new EntraAccountResolution(
                EntraAccountResolutionStatus.Ambiguous,
                null,
                new IEntraAccount[]
                {
                    new EntraAccount("first", "alice@example.com"),
                    new EntraAccount("second", "alice@example.com")
                }));
        CommandContext command = CreateCommand(
            targetResolver: targets.Object,
            accountResolver: accounts.Object);

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => command.Command.SetAsync(
                null,
                "alice@example.com",
                noInherit: false,
                "org",
                null,
                global: true,
                local: false));

        Assert.Contains("use --id", ex.Message);
    }

    [Fact]
    public async Task AzureReposCommand_LoginAsync_UsesResolvedAuthority()
    {
        var context = new TestCommandContext();
        const string authority = "https://login.microsoftonline.com/common";
        var target = AccountBindingTarget.ForOrganization("org");
        var targets = new Mock<IAccountBindingTargetResolver>(MockBehavior.Strict);
        targets.Setup(x => x.ResolveOrganizationAsync("org"))
            .ReturnsAsync(new AccountBindingTargetResolution(target, null, authority));
        var result = new Mock<IEntraAuthenticationResult>(MockBehavior.Strict);
        result.SetupGet(x => x.Account)
            .Returns(new EntraAccount("account-id", "alice@example.com"));
        var authentication = new Mock<IEntraAuthentication>(MockBehavior.Strict);
        authentication
            .Setup(x => x.GetTokenForUserAsync(
                AzureDevOpsConstants.AzureDevOpsDefaultScopes,
                authority,
                null,
                InteractionMode.Auto,
                CancellationToken.None))
            .ReturnsAsync(result.Object);
        CommandContext command = CreateCommand(
            context: context,
            authentication: authentication.Object,
            targetResolver: targets.Object);

        await command.Command.LoginAsync(null, "org");

        Assert.Contains(
            "Signed in as alice@example.com (account-id)",
            context.Console.WrittenMessages);
    }

    [Fact]
    public async Task AzureReposCommand_LogoutAsync_RemovesResolvedAccount()
    {
        var context = new TestCommandContext();
        var account = new EntraAccount("account-id", "alice@example.com");
        var accounts = new Mock<IEntraAccountResolver>(MockBehavior.Strict);
        accounts.Setup(x => x.ResolveByIdAsync("account-id"))
            .ReturnsAsync(new EntraAccountResolution(
                EntraAccountResolutionStatus.Found, account, new[] {account}));
        var authentication = new Mock<IEntraAuthentication>(MockBehavior.Strict);
        authentication.Setup(x => x.RemoveUserAccountAsync(account)).ReturnsAsync(true);
        CommandContext command = CreateCommand(
            context: context,
            authentication: authentication.Object,
            accountResolver: accounts.Object);

        await command.Command.LogoutAsync("account-id", null);

        authentication.Verify(x => x.RemoveUserAccountAsync(account), Times.Once);
        Assert.Contains(
            "Signed out alice@example.com (account-id)",
            context.Console.WrittenMessages);
    }

    private static CommandContext CreateCommand(
        TestCommandContext context = null,
        IEntraAuthentication authentication = null,
        IEntraAccountResolver accountResolver = null,
        IAccountBindingTargetResolver targetResolver = null,
        IAccountBindingManager bindingManager = null)
    {
        context ??= new TestCommandContext();
        var provider = new Mock<IHostProvider>();
        provider.SetupGet(x => x.Id).Returns("azure-repos");
        provider.SetupGet(x => x.Name).Returns("Azure Repos");

        authentication ??= Mock.Of<IEntraAuthentication>();
        accountResolver ??= Mock.Of<IEntraAccountResolver>();
        targetResolver ??= Mock.Of<IAccountBindingTargetResolver>();
        bindingManager ??= Mock.Of<IAccountBindingManager>();

        return new CommandContext(
            new AzureReposCommand(
                provider.Object,
                context,
                authentication,
                accountResolver,
                targetResolver,
                bindingManager,
                Mock.Of<IAzureDevOpsAuthorityCache>()),
            context);
    }

    private sealed record CommandContext(
        AzureReposCommand Command,
        TestCommandContext Context);
}
