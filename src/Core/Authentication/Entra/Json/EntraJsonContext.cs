using System.Text.Json.Serialization;

namespace GitCredentialManager.Authentication.Entra.Json;

[JsonSerializable(typeof(EntraErrorJson))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
public partial class EntraJsonContext : JsonSerializerContext;
