using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GitCredentialManager;
using GitCredentialManager.Authentication.Entra;

namespace Microsoft.AzureRepos.Accounts;

public enum EntraAccountResolutionStatus
{
    Found,
    NotFound,
    Ambiguous
}

public sealed class EntraAccountResolution
{
    public EntraAccountResolution(
        EntraAccountResolutionStatus status,
        IEntraAccount account,
        IReadOnlyList<IEntraAccount> matches)
    {
        Status = status;
        Account = account;
        Matches = matches ?? Array.Empty<IEntraAccount>();
    }

    public EntraAccountResolutionStatus Status { get; }
    public IEntraAccount Account { get; }
    public IReadOnlyList<IEntraAccount> Matches { get; }
}

public interface IEntraAccountResolver
{
    Task<IReadOnlyList<IEntraAccount>> GetAccountsAsync();
    Task<EntraAccountResolution> ResolveAsync(AccountReference reference);
    Task<EntraAccountResolution> ResolveByIdAsync(string homeAccountId);
    Task<EntraAccountResolution> ResolveByUserNameAsync(string userName);
}

public sealed class EntraAccountResolver : IEntraAccountResolver
{
    private readonly Lazy<Task<IReadOnlyList<IEntraAccount>>> _accounts;

    public EntraAccountResolver(IEntraAuthentication authentication)
    {
        EnsureArgument.NotNull(authentication, nameof(authentication));
        _accounts = new Lazy<Task<IReadOnlyList<IEntraAccount>>>(
            () => authentication.GetUserAccountsAsync(CancellationToken.None));
    }

    public Task<IReadOnlyList<IEntraAccount>> GetAccountsAsync() => _accounts.Value;

    public Task<EntraAccountResolution> ResolveAsync(AccountReference reference)
    {
        EnsureArgument.NotNull(reference, nameof(reference));

        return reference.IsLegacy
            ? ResolveByUserNameAsync(reference.UserName)
            : ResolveByIdAsync(reference.HomeAccountId);
    }

    public async Task<EntraAccountResolution> ResolveByIdAsync(string homeAccountId)
    {
        EnsureArgument.NotNullOrWhiteSpace(homeAccountId, nameof(homeAccountId));

        IReadOnlyList<IEntraAccount> accounts = await GetAccountsAsync();
        IEntraAccount[] matches = accounts
            .Where(x => StringComparer.OrdinalIgnoreCase.Equals(x.HomeAccountId, homeAccountId))
            .ToArray();

        return CreateResult(matches);
    }

    public async Task<EntraAccountResolution> ResolveByUserNameAsync(string userName)
    {
        EnsureArgument.NotNullOrWhiteSpace(userName, nameof(userName));

        IReadOnlyList<IEntraAccount> accounts = await GetAccountsAsync();
        IEntraAccount[] matches = accounts
            .Where(x => StringComparer.OrdinalIgnoreCase.Equals(x.UserName, userName))
            .ToArray();

        return CreateResult(matches);
    }

    private static EntraAccountResolution CreateResult(IEntraAccount[] matches)
    {
        return matches.Length switch
        {
            0 => new EntraAccountResolution(
                EntraAccountResolutionStatus.NotFound, null, matches),
            1 => new EntraAccountResolution(
                EntraAccountResolutionStatus.Found, matches[0], matches),
            _ => new EntraAccountResolution(
                EntraAccountResolutionStatus.Ambiguous, null, matches)
        };
    }
}
