using System.Text.Json.Serialization;

namespace GitCredentialManager.Authentication.OpenIdConnect;

public class WellKnownOpenIdConnectConfig
{
    [JsonPropertyName("issuer")]
    public string Issuer { get; set; }
    [JsonPropertyName("authorization_endpoint")]
    public string AuthorizationEndpoint { get; set; }
    [JsonPropertyName("token_endpoint")]
    public string TokenEndpoint { get; set; }
    [JsonPropertyName("device_authorization_endpoint")]
    public string DeviceAuthorizationEndpoint { get; set; }
    [JsonPropertyName("userinfo_endpoint")]
    public string UserInfoEndpoint { get; set; }
}
