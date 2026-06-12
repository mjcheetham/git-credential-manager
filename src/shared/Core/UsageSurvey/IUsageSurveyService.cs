namespace GitCredentialManager.UsageSurvey;

/// <summary>
/// Records usage survey events from inside GCM's normal credential flow.
/// All operations are best-effort and must never throw: usage survey failures
/// must never break a git operation.
/// </summary>
public interface IUsageSurveyService
{
    /// <summary>
    /// True if the user has opted in to usage survey (via git config or env var).
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Record a single <c>get</c> credential event. Best-effort, never throws,
    /// no-op when usage survey is disabled. Performs only constant-time, non-blocking
    /// work on the hot path: writes one self-contained JSONL file to the queue
    /// directory. A background dispatcher process is spawned if one is not
    /// already running.
    /// </summary>
    /// <param name="providerId">Host provider id (e.g. "github").</param>
    /// <param name="fromCache">True if the credential came from the OS store; false if freshly generated.</param>
    /// <param name="authMethod">Host-provider-specific authentication mechanism (e.g. "oauth", "pat", "managed-identity"). Null when the provider did not report a mechanism.</param>
    void RecordGet(string providerId, bool fromCache, string authMethod);
}
