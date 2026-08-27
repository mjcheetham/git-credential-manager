using System.Text.Json.Serialization;

namespace GitCredentialManager.Authentication.OpenIdConnect;

[JsonSerializable(typeof(WellKnownOpenIdConnectConfig))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
public partial class OidcJsonContext : JsonSerializerContext;
