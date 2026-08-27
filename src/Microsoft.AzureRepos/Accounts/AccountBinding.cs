using GitCredentialManager;

namespace Microsoft.AzureRepos.Accounts;

public sealed class AccountBinding
{
    private AccountBinding(
        AccountBindingTarget target,
        AccountBindingScope scope,
        AccountBindingState state,
        AccountReference account,
        string rawAccountId,
        string rawUserName,
        string invalidReason)
    {
        Target = target;
        Scope = scope;
        State = state;
        Account = account;
        RawAccountId = rawAccountId;
        RawUserName = rawUserName;
        InvalidReason = invalidReason;
    }

    public AccountBindingTarget Target { get; }
    public AccountBindingScope Scope { get; }
    public AccountBindingState State { get; }
    public AccountReference Account { get; }
    public string RawAccountId { get; }
    public string RawUserName { get; }
    public string InvalidReason { get; }

    public static AccountBinding Bound(
        AccountBindingTarget target,
        AccountBindingScope scope,
        AccountReference account)
    {
        EnsureArgument.NotNull(target, nameof(target));
        EnsureArgument.NotNull(account, nameof(account));

        return new AccountBinding(
            target, scope, AccountBindingState.Bound, account,
            account.HomeAccountId, account.UserName, null);
    }

    public static AccountBinding NoInherit(AccountBindingTarget target)
    {
        EnsureArgument.NotNull(target, nameof(target));

        return new AccountBinding(
            target, AccountBindingScope.Local, AccountBindingState.NoInherit,
            null, null, string.Empty, null);
    }

    public static AccountBinding Invalid(
        AccountBindingTarget target,
        AccountBindingScope scope,
        string rawAccountId,
        string rawUserName,
        string reason)
    {
        EnsureArgument.NotNull(target, nameof(target));
        EnsureArgument.NotNullOrWhiteSpace(reason, nameof(reason));

        return new AccountBinding(
            target, scope, AccountBindingState.Invalid, null,
            rawAccountId, rawUserName, reason);
    }
}
