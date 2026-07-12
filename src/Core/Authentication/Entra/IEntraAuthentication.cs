using System.Threading;
using System.Threading.Tasks;

namespace GitCredentialManager.Authentication.Entra;

public interface IEntraAuthentication
{
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
}

public interface IEntraAuthenticationResult
{
    string AccessToken { get; }
}
