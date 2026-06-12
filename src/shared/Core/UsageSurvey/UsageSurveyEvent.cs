using System;
using System.Text.Json.Serialization;

namespace GitCredentialManager.UsageSurvey;

/// <summary>
/// Closed-allow-list DTO for a single usage survey event. The set of fields is the
/// complete and exhaustive list of data captured by GCM usage survey. Adding a new
/// field for an event type is a breaking change for that type and requires
/// bumping the per-type version constant (e.g.
/// <see cref="Constants.UsageSurvey.GetEventVersion"/>) and the
/// <see cref="EventVersion"/> emitted alongside the event name.
/// </summary>
public sealed class UsageSurveyEvent
{
    /// <summary>
    /// Event type. Currently only <c>"get"</c> is emitted; reserved for future events.
    /// </summary>
    [JsonPropertyName("event")]
    public string Event { get; set; }

    /// <summary>
    /// Schema version of this event type. Each event type owns its own version that
    /// can evolve independently (e.g. <c>"get"</c> can be at v2 while a future
    /// <c>"diagnose"</c> event is still at v1).
    /// </summary>
    [JsonPropertyName("event_version")]
    public int EventVersion { get; set; }

    /// <summary>
    /// UTC timestamp truncated to seconds, ISO 8601 with trailing <c>Z</c>.
    /// </summary>
    [JsonPropertyName("ts")]
    public string Timestamp { get; set; }

    /// <summary>
    /// Random per-install GUID. Not derived from any machine attribute.
    /// </summary>
    [JsonPropertyName("install_id")]
    public string InstallId { get; set; }

    /// <summary>
    /// GCM version (e.g. "2.6.1").
    /// </summary>
    [JsonPropertyName("gcm_version")]
    public string GcmVersion { get; set; }

    /// <summary>
    /// OS family. One of "windows" / "macos" / "linux".
    /// </summary>
    [JsonPropertyName("os")]
    public string Os { get; set; }

    /// <summary>
    /// OS version, major.minor only. No patch level. No distro identification on Linux.
    /// </summary>
    [JsonPropertyName("os_version")]
    public string OsVersion { get; set; }

    /// <summary>
    /// CPU architecture (e.g. "x64", "arm64", "x86").
    /// </summary>
    [JsonPropertyName("arch")]
    public string Arch { get; set; }

    /// <summary>
    /// Host provider id (e.g. "github", "bitbucket", "azure-repos", "gitlab", "generic").
    /// </summary>
    [JsonPropertyName("provider")]
    public string Provider { get; set; }

    /// <summary>
    /// Host-provider-specific authentication mechanism used to obtain this credential
    /// (e.g. <c>"basic"</c>, <c>"oauth"</c>, <c>"device"</c>, <c>"pat"</c>,
    /// <c>"managed-identity"</c>, <c>"wia"</c>). Free-form string controlled by the
    /// host provider; consumers should treat unknown values as opaque. Null when the
    /// provider did not report a mechanism.
    /// </summary>
    [JsonPropertyName("auth_method")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string AuthMethod { get; set; }

    /// <summary>
    /// True if an existing credential was returned from the credential store; false if a
    /// fresh credential was generated.
    /// </summary>
    [JsonPropertyName("from_cache")]
    public bool FromCache { get; set; }
}

/// <summary>
/// Source-generated <c>System.Text.Json</c> context for <see cref="UsageSurveyEvent"/> so we
/// avoid any reflection on the producer hot path and keep the schema strictly fixed.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(UsageSurveyEvent))]
internal partial class UsageSurveyEventJsonContext : JsonSerializerContext
{
}
