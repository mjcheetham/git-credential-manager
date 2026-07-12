using System.Threading;
using System.Threading.Tasks;
using Microsoft.Identity.Client;

namespace GitCredentialManager.Authentication.Entra;

public partial class EntraAuthentication
{
    public async Task<IEntraAuthenticationResult> GetTokenForManagedIdentityAsync(
        string resource, ManagedIdentity mi, CancellationToken ct = default)
    {
        Context.Trace.WriteLine($"Creating confidential client for managed identity '{mi.Id}'...");
        var builder = ManagedIdentityApplicationBuilder.Create(mi)
            .WithHttpClientFactory(_httpFactory)
            .WithTraceLogging(Context);

        IManagedIdentityApplication app = builder.Build();

        Context.Trace.WriteLine($"Acquiring token for managed identity with resource '{resource}'...");
        AuthenticationResult result = await app.AcquireTokenForManagedIdentity(resource)
            .ExecuteAsync(ct);

        return AuthResult.FromMsalResult(result);
    }
}
