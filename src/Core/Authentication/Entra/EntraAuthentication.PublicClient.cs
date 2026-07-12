using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Identity.Client;

namespace GitCredentialManager.Authentication.Entra;

public record PublicClientConfig
{
    /// <summary>
    /// Application (client) ID of the Entra application registration.
    /// </summary>
    public string ClientId { get; init; }

    /// <summary>
    /// Use the shared Microsoft Developer Tools identity cache.
    /// </summary>
    /// <remarks>
    /// If <see langword="true"/> then use the token cache shared by Microsoft
    /// developer tools such as the Azure PowerShell CLI. Otherwise, use the
    /// Git Credential Manager cache, used only by GCM.
    /// </remarks>
    public bool UseSharedCache { get; init; }
}

public partial class EntraAuthentication
{
    private readonly PublicClientConfig _publicClientConfig;
    private PublicClientApplicationBuilder _publicBuilder;

    public async Task<IReadOnlyList<IEntraAccount>> GetUserAccountsAsync(CancellationToken ct = default)
    {
        IPublicClientApplication app = GetPublicAppBuilder().Build();
        await RegisterCacheAsync(app);

        IEnumerable<IAccount> accounts = await app.GetAccountsAsync();
        return accounts.Select(EntraAccount.FromMsalAccount).ToList().AsReadOnly();
    }

    public async Task<bool> RemoveUserAccountAsync(IEntraAccount account)
    {
        IPublicClientApplication app = GetPublicAppBuilder().Build();
        await RegisterCacheAsync(app);

        IAccount msalAccount = await ResolveAccountAsync(app, account);
        if (msalAccount is null)
        {
            return false;
        }

        Context.Trace.WriteLine(
            $"Removing account '{msalAccount.HomeAccountId.Identifier}' ({msalAccount.Username}) from the cache...");
        await app.RemoveAsync(msalAccount);
        return true;
    }

    private async Task<IAccount> ResolveAccountAsync(IPublicClientApplication app, IEntraAccount account)
    {
        // If we have been handed a wrapped MSAL account there is no need to search the cache again
        if (account is EntraAccount { MsalAccount: not null } wrapped)
        {
            Context.Trace.WriteLine($"Account '{account.HomeAccountId}' ({account.UserName}) is already from cache.");
            return wrapped.MsalAccount;
        }

        // Pull all account from the cache and search for the closest match, first by HomeAccountId, and then by UPN.
        Context.Trace.WriteLine("Getting all cached accounts...");
        IReadOnlyList<IAccount> accounts = (await app.GetAccountsAsync()).ToList();
        if (accounts.Count == 0)
        {
            Context.Trace.WriteLine("No cached accounts available.");
            return null;
        }

        Context.Trace.WriteLine($"Found {accounts.Count} cached accounts.");

        Context.Trace.WriteLine($"Checking cached accounts for ID '{account.HomeAccountId}'...");
        if (!string.IsNullOrWhiteSpace(account.HomeAccountId))
        {
            IAccount byId = accounts.FirstOrDefault(a =>
                StringComparer.OrdinalIgnoreCase.Equals(a.HomeAccountId?.Identifier, account.HomeAccountId));
            if (byId != null && !string.IsNullOrWhiteSpace(account.UserName) &&
                !StringComparer.OrdinalIgnoreCase.Equals(byId.Username, account.UserName))
            {
                Context.Trace.WriteLine(
                    $"Cached account UPN '{byId.Username}' differs from supplied UPN '{account.UserName}' " +
                    $"for HomeAccountId '{account.HomeAccountId}'; using HomeAccountId.");
            }

            Context.Trace.WriteLine($"Matched account by ID '{byId?.HomeAccountId}' ({byId?.Username}).)");
            return byId;
        }

        Context.Trace.WriteLine($"Checking cached accounts for UPN '{account.UserName}'...");
        if (!string.IsNullOrWhiteSpace(account.UserName))
        {
            IAccount[] matchedByName = accounts
                .Where(a => StringComparer.OrdinalIgnoreCase.Equals(a.Username, account.UserName))
                .ToArray();
            if (matchedByName.Length > 1)
            {
                Context.Trace.WriteLine(
                    $"{matchedByName.Length} cached accounts share UPN '{account.UserName}'; using the first " +
                    "(provide a HomeAccountId to disambiguate).");
            }

            IAccount byName = matchedByName.FirstOrDefault();
            if (byName is not null)
            {
                Context.Trace.WriteLine($"Matched account by UPN '{byName.HomeAccountId}' ({byName.Username}).)");
                return byName;
            }
        }

        Context.Trace.WriteLine("No cached account found.");
        return null;
    }

    private PublicClientApplicationBuilder GetPublicAppBuilder()
    {
        if (_publicClientConfig is null)
        {
            throw new InvalidOperationException(
                "Public client configuration is required for user authentication.");
        }

        if (_publicBuilder is null)
        {
            Context.Trace.WriteLine("Creating public client application builder...");
            _publicBuilder = PublicClientApplicationBuilder.Create(_publicClientConfig.ClientId)
                .WithHttpClientFactory(_httpFactory)
                .WithTraceLogging(Context)
                .WithLegacyCacheCompatibility(false)
                .WithDefaultRedirectUri();
        }

        return _publicBuilder;
    }
}
