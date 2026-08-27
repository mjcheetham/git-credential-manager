using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GitCredentialManager.Authentication.Entra.Json;

public static class EntraErrorCodes
{
    public const string InvalidTenant = "invalid_tenant";
}

public class EntraErrorJson
{
    [JsonPropertyName("error")]
    public string Error { get; set; }

    [JsonPropertyName("error_description")]
    public string ErrorDescription { get; set; }

    [JsonPropertyName("error_codes")]
    public IList<int> ErrorCodes { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    [JsonPropertyName("trace_id")]
    public string TraceId { get; set; }

    [JsonPropertyName("correlation_id")]
    public string CorrelationId { get; set; }

    [JsonPropertyName("error_uri")]
    public Uri ErrorUri { get; set; }
}
