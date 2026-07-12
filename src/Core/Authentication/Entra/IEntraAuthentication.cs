using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GitCredentialManager.Authentication.Entra;

public interface IEntraAuthentication
{
    /// <summary>
    /// Get the list of user accounts that have previously signed in to the application.
    /// </summary>
    Task<IReadOnlyList<IEntraAccount>> GetUserAccountsAsync(CancellationToken ct = default);

    /// <summary>
    /// Remove the given user account from the configured user token cache.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if the account existed and was successfully removed, <see langword="false"/> otherwise.
    /// </returns>
    Task<bool> RemoveUserAccountAsync(IEntraAccount account);

    /// <summary>
    /// Acquire an access token for a service principal.
    /// </summary>
    Task<IEntraAuthenticationResult> GetTokenForServicePrincipalAsync(
        string[] scopes,
        ServicePrincipalIdentity sp,
        CancellationToken ct = default
    );

    /// <summary>
    /// Acquire an access token for a managed identity.
    /// </summary>
    Task<IEntraAuthenticationResult> GetTokenForManagedIdentityAsync(
        string resource,
        ManagedIdentity mi,
        CancellationToken ct = default
    );

    /// <summary>
    /// Acquire an access token using workload federation.
    /// </summary>
    Task<IEntraAuthenticationResult> GetTokenUsingWorkloadFederationAsync(
        string[] scopes,
        WorkloadFederationOptions fedOpts,
        CancellationToken ct = default
    );
}

public interface IEntraAuthenticationResult
{
    string AccessToken { get; }
    IEntraAccount Account { get; }
}
