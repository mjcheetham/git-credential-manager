using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace GitCredentialManager.UsageSurvey;

/// <summary>
/// Producer-side usage survey service. Lives inside the GCM process for the duration of
/// a single invocation. For each event it writes a self-contained
/// <c>&lt;ts&gt;-&lt;pid&gt;-&lt;seq&gt;.jsonl</c> file under
/// <c>~/.gcm/usage-survey/events/</c> (via a brief <c>.partial</c> staging name) and
/// best-effort spawns the background dispatcher to ship them.
/// </summary>
public sealed class UsageSurveyService : DisposableObject, IUsageSurveyService
{
    private readonly ICommandContext _context;
    private readonly UsageSurveyPaths _paths;
    private readonly InstallId _installId;
    private readonly object _writeLock = new();

    private int _eventSequence;
    private bool _dispatcherSpawnAttempted;
    private bool _enabledCache;
    private bool _enabledCacheValid;

    public UsageSurveyService(ICommandContext context)
        : this(context, new UsageSurveyPaths(context.FileSystem))
    {
    }

    internal UsageSurveyService(ICommandContext context, UsageSurveyPaths paths)
    {
        EnsureArgument.NotNull(context, nameof(context));
        EnsureArgument.NotNull(paths, nameof(paths));

        _context = context;
        _paths = paths;
        _installId = new InstallId(context.FileSystem, paths, context.Trace);
    }

    public bool IsEnabled
    {
        get
        {
            if (_enabledCacheValid)
            {
                return _enabledCache;
            }

            _enabledCache = ResolveEnabled();
            _enabledCacheValid = true;
            return _enabledCache;
        }
    }

    private bool ResolveEnabled()
    {
        try
        {
            if (_context.Settings.TryGetSetting(
                    Constants.EnvironmentVariables.GcmUsageSurvey,
                    Constants.GitConfiguration.Credential.SectionName,
                    Constants.GitConfiguration.Credential.UsageSurvey,
                    out string raw))
            {
                return raw.ToBooleanyOrDefault(false);
            }
        }
        catch (Exception ex)
        {
            _context.Trace.WriteLine($"Usage survey: failed to resolve enabled state: {ex.Message}");
        }

        return false;
    }

    public void RecordGet(string providerId, bool fromCache, string authMethod)
    {
        if (string.IsNullOrEmpty(providerId))
        {
            return;
        }

        try
        {
            if (!IsEnabled)
            {
                return;
            }

            UsageSurveyEvent evt = BuildEvent(providerId, fromCache, authMethod);
            string line = JsonSerializer.Serialize(evt, UsageSurveyEventJsonContext.Default.UsageSurveyEvent);

            WriteEvent(line);

            TrySpawnDispatcherOnce();
        }
        catch (Exception ex)
        {
            // Producer must never throw. Trace and swallow.
            _context.Trace.WriteLine($"Usage survey: RecordGet failed: {ex.Message}");
        }
    }

    private UsageSurveyEvent BuildEvent(string providerId, bool fromCache, string authMethod)
    {
        PlatformInformation info = PlatformUtils.GetPlatformInformation(_context.Trace2);

        return new UsageSurveyEvent
        {
            Event = "get",
            EventVersion = Constants.UsageSurvey.GetEventVersion,
            Timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            InstallId = _installId.GetOrCreate().ToString("D"),
            GcmVersion = Constants.GcmVersion?.ToString() ?? "0.0.0",
            Os = info.OperatingSystemType?.ToLowerInvariant(),
            OsVersion = info.OperatingSystemVersion,
            Arch = info.CpuArchitecture?.ToLowerInvariant(),
            Provider = providerId,
            AuthMethod = string.IsNullOrWhiteSpace(authMethod) ? null : authMethod,
            FromCache = fromCache,
        };
    }

    /// <summary>
    /// Write a single event to its own self-contained <c>.jsonl</c> file. The file is
    /// first written under a <c>.partial</c> name (which the dispatcher ignores) and
    /// then atomically renamed to the published <c>.jsonl</c> name as the very last
    /// step. This makes the operation safe against the process exiting before
    /// <see cref="DisposableObject.Dispose"/> runs (which happens when the main thread
    /// reaches <c>Environment.Exit</c> while the AppMain thread is still unwinding
    /// using-blocks).
    /// </summary>
    private void WriteEvent(string jsonLine)
    {
        lock (_writeLock)
        {
            _context.FileSystem.CreateDirectory(_paths.EventsDirectory);

            string ts = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfff");
            int pid;
            try { pid = Process.GetCurrentProcess().Id; }
            catch { pid = 0; }
            int seq = Interlocked.Increment(ref _eventSequence);

            string name = $"{ts}-{pid}-{seq}.jsonl";
            string finalPath = Path.Combine(_paths.EventsDirectory, name);
            string partialPath = finalPath + ".partial";

            byte[] bytes = Encoding.UTF8.GetBytes(jsonLine + "\n");
            using (Stream s = _context.FileSystem.OpenFileStream(
                       partialPath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None))
            {
                s.Write(bytes, 0, bytes.Length);
            }

            // Promote the file to its final name. The dispatcher only consumes
            // *.jsonl, so it never observes the partial.
            _context.FileSystem.MoveFile(partialPath, finalPath, overwrite: true);
        }
    }

    private void TrySpawnDispatcherOnce()
    {
        if (_dispatcherSpawnAttempted)
        {
            return;
        }
        _dispatcherSpawnAttempted = true;

        try
        {
            // If a dispatcher is already running (holds the pidfile), skip spawning.
            if (DispatcherPidFile.IsActive(_context.FileSystem, _paths, _context.Trace))
            {
                return;
            }

            string appPath = _context.ApplicationPath;
            if (string.IsNullOrEmpty(appPath))
            {
                return;
            }

            DetachedProcess.Start(appPath, "usage-survey dispatch");
        }
        catch (Exception ex)
        {
            _context.Trace.WriteLine($"Usage survey: failed to spawn dispatcher: {ex.Message}");
        }
    }
}
