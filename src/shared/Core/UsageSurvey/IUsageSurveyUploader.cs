using System.Threading;
using System.Threading.Tasks;

namespace GitCredentialManager.UsageSurvey;

/// <summary>
/// Sink for shipped usage survey events. The dispatcher hands one event at a time to the
/// uploader; the uploader returns success/failure. On failure the dispatcher retries
/// later.
/// </summary>
public interface IUsageSurveyUploader
{
    /// <summary>
    /// Attempt to ship the given raw JSON line. Return true on success, false on a
    /// retriable failure (network down, server 5xx, etc.). Should not throw.
    /// </summary>
    Task<bool> UploadAsync(string jsonLine, CancellationToken ct);
}
