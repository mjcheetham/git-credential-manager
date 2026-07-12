using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Identity.Client;

namespace GitCredentialManager.Authentication.Entra;

public partial class EntraAuthentication
{
    public async Task<IEntraAuthenticationResult> GetTokenForServicePrincipalAsync(
        string[] scopes, ServicePrincipalIdentity sp, CancellationToken ct = default)
    {
        Context.Trace.WriteLine($"Creating confidential client for service principal '{sp.Id}' in tenant '{sp.TenantId}'...");
        var builder = ConfidentialClientApplicationBuilder.Create(sp.Id)
            .WithTenantId(sp.TenantId)
            .WithHttpClientFactory(_httpFactory)
            .WithTraceLogging(Context);

        if (sp.Certificate is not null)
        {
            Context.Trace.WriteLine($"Using service principal certificate: {sp.Certificate.Thumbprint}");
            builder.WithCertificate(sp.Certificate);
        }
        else if (sp.ClientSecret is not null)
        {
            Context.Trace.WriteLineSecrets("Using service principal secret: {0}", [sp.ClientSecret]);
            builder.WithClientSecret(sp.ClientSecret);
        }
        else
        {
            throw new ArgumentException($"Service principal '{sp.Id}' must have either a certificate or client secret.", nameof(sp));
        }

        Context.Trace.WriteLine($"SendX5C is '{sp.SendX5C}'");

        IConfidentialClientApplication app = builder.Build();
        await RegisterCacheAsync(app);

        Context.Trace.WriteLine($"Acquiring token for service principal with scopes '{string.Join(", ", scopes)}'...");
        AuthenticationResult result = await app.AcquireTokenForClient(scopes)
            .WithSendX5C(sp.SendX5C)
            .ExecuteAsync(ct);

        return AuthResult.FromMsalResult(result);
    }

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
