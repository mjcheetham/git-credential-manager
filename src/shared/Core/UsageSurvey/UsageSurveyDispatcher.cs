using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GitCredentialManager.UsageSurvey;

/// <summary>
/// The background dispatcher. Runs as a separate, detached GCM process that periodically
/// scans the events queue directory and ships each completed JSONL file via an
/// <see cref="IUsageSurveyUploader"/>. Successfully shipped files are moved to a
/// <c>sent/</c> archive that is auto-purged after
/// <see cref="Constants.UsageSurvey.SentRetention"/> so users can audit recent traffic.
/// Exits cleanly after a configurable idle period to avoid sticking around forever
/// on machines that don't use git.
/// </summary>
public sealed class UsageSurveyDispatcher
{
    private readonly IFileSystem _fileSystem;
    private readonly UsageSurveyPaths _paths;
    private readonly ITrace _trace;
    private readonly IUsageSurveyUploader _uploader;
    private readonly TextWriter _foregroundOutput;

    /// <summary>
    /// Interval between queue scans.
    /// </summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Maximum time the dispatcher will run with no events to ship before releasing the
    /// pidfile and exiting.
    /// </summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// How long shipped events are kept in <see cref="UsageSurveyPaths.SentDirectory"/>
    /// before being purged.
    /// </summary>
    public TimeSpan SentRetention { get; init; } = Constants.UsageSurvey.SentRetention;

    public UsageSurveyDispatcher(
        IFileSystem fileSystem,
        UsageSurveyPaths paths,
        ITrace trace,
        IUsageSurveyUploader uploader,
        TextWriter foregroundOutput = null)
    {
        EnsureArgument.NotNull(fileSystem, nameof(fileSystem));
        EnsureArgument.NotNull(paths, nameof(paths));
        EnsureArgument.NotNull(trace, nameof(trace));
        EnsureArgument.NotNull(uploader, nameof(uploader));

        _fileSystem = fileSystem;
        _paths = paths;
        _trace = trace;
        _uploader = uploader;
        _foregroundOutput = foregroundOutput;
    }

    /// <summary>
    /// Run the dispatcher loop until either the idle timeout elapses or the
    /// cancellation token is signalled.
    /// </summary>
    public async Task RunAsync(CancellationToken ct)
    {
        // Single-owner: bail out immediately if another dispatcher is already running.
        if (DispatcherPidFile.IsActive(_fileSystem, _paths, _trace))
        {
            _trace.WriteLine("Usage survey: another dispatcher is already running; exiting.");
            return;
        }

        if (!DispatcherPidFile.TryAcquire(_fileSystem, _paths, _trace))
        {
            _trace.WriteLine("Usage survey: failed to acquire dispatcher pidfile; exiting.");
            return;
        }

        try
        {
            DateTimeOffset lastActivity = DateTimeOffset.UtcNow;

            while (!ct.IsCancellationRequested)
            {
                int shipped = await DrainOnceAsync(ct);
                PurgeExpiredSent();

                if (shipped > 0)
                {
                    lastActivity = DateTimeOffset.UtcNow;
                }
                else if (DateTimeOffset.UtcNow - lastActivity > IdleTimeout)
                {
                    _trace.WriteLine($"Usage survey: idle for >{IdleTimeout.TotalMinutes:F0}m, exiting.");
                    return;
                }

                try
                {
                    await Task.Delay(PollInterval, ct);
                }
                catch (TaskCanceledException)
                {
                    return;
                }
            }
        }
        finally
        {
            DispatcherPidFile.Release(_fileSystem, _paths, _trace);
        }
    }

    /// <summary>
    /// Single pass over the queue directory. Returns the number of events shipped this pass.
    /// </summary>
    public async Task<int> DrainOnceAsync(CancellationToken ct)
    {
        int shipped = 0;

        if (!_fileSystem.DirectoryExists(_paths.EventsDirectory))
        {
            return 0;
        }

        IEnumerable<string> files;
        try
        {
            // Only pick up finalised files (producer has renamed away from .partial).
            // Materialise immediately because the loop mutates the directory (move to
            // sent/) which would invalidate a lazy enumeration.
            files = System.Linq.Enumerable.ToArray(
                _fileSystem.EnumerateFiles(_paths.EventsDirectory, "*.jsonl"));
        }
        catch (Exception ex)
        {
            _trace.WriteLine($"Usage survey: failed to enumerate events: {ex.Message}");
            return 0;
        }

        foreach (string file in files)
        {
            if (ct.IsCancellationRequested) break;

            // Defensive: only ship files literally ending in .jsonl (not .partial).
            if (!file.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                bool ok = await ShipFileAsync(file, ct);
                if (ok)
                {
                    ArchiveShipped(file);
                }
            }
            catch (Exception ex)
            {
                _trace.WriteLine($"Usage survey: failed to ship '{file}': {ex.Message}");
            }
        }

        return shipped;

        async Task<bool> ShipFileAsync(string filePath, CancellationToken ctok)
        {
            string[] lines;
            try
            {
                lines = _fileSystem.ReadAllLines(filePath);
            }
            catch (Exception ex)
            {
                _trace.WriteLine($"Usage survey: failed to read '{filePath}': {ex.Message}");
                return false;
            }

            foreach (string line in lines)
            {
                if (ctok.IsCancellationRequested) return false;
                if (string.IsNullOrWhiteSpace(line)) continue;

                bool ok = await _uploader.UploadAsync(line, ctok);
                if (!ok)
                {
                    return false;
                }

                shipped++;
                _foregroundOutput?.WriteLine(line);
                _foregroundOutput?.Flush();
            }

            return true;
        }
    }

    /// <summary>
    /// Move a successfully-shipped file into the <c>sent/</c> archive so users can
    /// inspect recent traffic. The filename is preserved (it already encodes the
    /// timestamp) so retention can be enforced without filesystem mtimes.
    /// </summary>
    private void ArchiveShipped(string filePath)
    {
        try
        {
            _fileSystem.CreateDirectory(_paths.SentDirectory);
            string name = Path.GetFileName(filePath);
            string dest = Path.Combine(_paths.SentDirectory, name);
            _fileSystem.MoveFile(filePath, dest, overwrite: true);
        }
        catch (Exception ex)
        {
            _trace.WriteLine($"Usage survey: failed to archive '{filePath}', deleting instead: {ex.Message}");
            try { _fileSystem.DeleteFile(filePath); }
            catch (Exception ex2)
            {
                _trace.WriteLine($"Usage survey: failed to delete '{filePath}': {ex2.Message}");
            }
        }
    }

    /// <summary>
    /// Delete any files in <c>sent/</c> whose filename timestamp is older than
    /// <see cref="SentRetention"/>. Filenames are
    /// <c>yyyyMMddTHHmmssfff-pid-seq.jsonl</c> per <c>UsageSurveyService</c>; the
    /// timestamp prefix is the source of truth for age so we don't depend on
    /// filesystem mtimes.
    /// </summary>
    private void PurgeExpiredSent()
    {
        if (!_fileSystem.DirectoryExists(_paths.SentDirectory))
        {
            return;
        }

        DateTimeOffset cutoff = DateTimeOffset.UtcNow - SentRetention;

        try
        {
            foreach (string file in System.Linq.Enumerable.ToArray(
                         _fileSystem.EnumerateFiles(_paths.SentDirectory, "*.jsonl")))
            {
                if (TryParseEventTimestamp(Path.GetFileName(file), out DateTimeOffset ts) && ts < cutoff)
                {
                    try { _fileSystem.DeleteFile(file); }
                    catch (Exception ex)
                    {
                        _trace.WriteLine($"Usage survey: failed to purge '{file}': {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _trace.WriteLine($"Usage survey: failed to enumerate sent archive: {ex.Message}");
        }
    }

    /// <summary>
    /// Parse the timestamp prefix from a queue/sent filename of the form
    /// <c>yyyyMMddTHHmmssfff-pid-seq.jsonl</c>. Returns false on any parse failure.
    /// Exposed internal for testing.
    /// </summary>
    internal static bool TryParseEventTimestamp(string fileName, out DateTimeOffset ts)
    {
        ts = default;
        if (string.IsNullOrEmpty(fileName)) return false;

        int dash = fileName.IndexOf('-');
        if (dash <= 0) return false;

        string prefix = fileName.Substring(0, dash);
        return DateTimeOffset.TryParseExact(
            prefix,
            "yyyyMMddTHHmmssfff",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out ts);
    }
}
