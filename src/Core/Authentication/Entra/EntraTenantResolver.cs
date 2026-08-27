using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GitCredentialManager.Authentication.Entra.Json;
using GitCredentialManager.Authentication.OpenIdConnect;

namespace GitCredentialManager.Authentication.Entra;

public record EntraTenant
{
    public Guid Id { get; init; }
    public string Authority { get; init; }
}

public interface IEntraTenantResolver
{
    Task<EntraTenant> LookupAsync(string name);
}

public partial class EntraTenantResolver : IEntraTenantResolver
{
    private readonly Lazy<HttpClient> _http;
    private readonly Uri _baseUri;
    private readonly Dictionary<string, Guid> _tenantIdCache = new();
    private readonly Dictionary<Guid, WellKnownOpenIdConnectConfig> _oidcCache = new();

    public EntraTenantResolver(IHttpClientFactory httpFactory, string baseUrl = null)
    {
        EnsureArgument.NotNull(httpFactory, nameof(httpFactory));

        _http = new Lazy<HttpClient>(httpFactory.CreateClient);
        _baseUri = new Uri(baseUrl ?? Constants.DefaultEntraAuthorityBaseUrl);
    }

    public async Task<EntraTenant> LookupAsync(string name)
    {
        if (!_tenantIdCache.TryGetValue(name, out Guid id) || !_oidcCache.TryGetValue(id, out WellKnownOpenIdConnectConfig oidcInfo))
        {
            Uri uri = GetWellKnownOidcUri(name);
            using HttpRequestMessage request = new(HttpMethod.Get, uri);
            using HttpResponseMessage response = await _http.Value.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                EntraErrorJson error = null;
                Exception innerEx = null;
                try
                {
                    error = await response.Content.ReadFromJsonAsync<EntraErrorJson>();
                    innerEx = new Exception(error?.ErrorDescription);
                }
                catch
                {
                    // squash
                }

                if (response.StatusCode == HttpStatusCode.BadRequest && error?.Error == EntraErrorCodes.InvalidTenant)
                {
                    return null;
                }

                throw new Exception(
                    $"Failed to get OIDC configuration for tenant '{name}' from '{uri}'. Status code: {response.StatusCode}",
                    innerEx);
            }

            oidcInfo = await response.Content.ReadFromJsonAsync<WellKnownOpenIdConnectConfig>();

            Match match = TenantIdRegex.Match(oidcInfo.AuthorizationEndpoint);
            if (!match.Success)
            {
                throw new Exception($"Failed to parse tenant ID from '{oidcInfo.AuthorizationEndpoint}' for tenant '{name}' from '{uri}'.");
            }

            id = Guid.Parse(match.Groups["tenantId"].Value);
            _oidcCache[id] = oidcInfo;
            _tenantIdCache[name] = id;
        }

        return new EntraTenant
        {
            Id = id,
            Authority = oidcInfo.Issuer
        };
    }

    private Uri GetWellKnownOidcUri(string tenantNameOrId)
    {
        return new Uri(_baseUri, $"{tenantNameOrId}/v2.0/.well-known/openid-configuration");
    }

    [GeneratedRegex(@"\/(?'tenantId'[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12})\/oauth2(?:\/v2\.0)?\/authorize\/?(?:[?#].*)?$")]
    private partial Regex TenantIdRegex { get; }
}
