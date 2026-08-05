using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace GitCredentialManager;

/// <summary>
/// The different event types tracked in the TRACE2 tracing
/// system.
/// </summary>
public enum Trace2Event
{
    [JsonStringEnumMemberName("version")]
    Version = 0,
    [JsonStringEnumMemberName("start")]
    Start = 1,
    [JsonStringEnumMemberName("exit")]
    Exit = 2,
    [JsonStringEnumMemberName("child_start")]
    ChildStart = 3,
    [JsonStringEnumMemberName("child_exit")]
    ChildExit = 4,
    [JsonStringEnumMemberName("error")]
    Error = 5,
    [JsonStringEnumMemberName("region_enter")]
    RegionEnter = 6,
    [JsonStringEnumMemberName("region_leave")]
    RegionLeave = 7,
    [JsonStringEnumMemberName("thread_start")]
    ThreadStart = 8,
    [JsonStringEnumMemberName("thread_exit")]
    ThreadExit = 9,
    [JsonStringEnumMemberName("data")]
    Data = 10,
    [JsonStringEnumMemberName("cmd_name")]
    CommandName = 11,
    [JsonStringEnumMemberName("cmd_mode")]
    CommandMode = 12,
    [JsonStringEnumMemberName("data_json")]
    DataJson = 13,
}

/// <summary>
/// Classifications of processes invoked by GCM.
/// </summary>
public enum Trace2ProcessClass
{
    [JsonStringEnumMemberName("none")]
    None = 0,
    [JsonStringEnumMemberName("ui_helper")]
    UIHelper = 1,
    [JsonStringEnumMemberName("git")]
    Git = 2,
    [JsonStringEnumMemberName("other")]
    Other = 3
}

/// <summary>
/// Stores various TRACE2 format targets user has enabled.
/// Check <see cref="Trace2FormatTarget"/> for supported formats.
/// </summary>
internal class Trace2Settings
{
    public IDictionary<Trace2FormatTarget, string> FormatTargetsAndValues { get; set; } =
        new Dictionary<Trace2FormatTarget, string>();
}

/// <summary>
/// Specifies a "text span" (i.e. space between two pipes) for the performance format target.
/// </summary>
internal class PerformanceFormatSpan
{
    public int Size { get; set; }

    public int BeginPadding { get; set; }

    public int EndPadding { get; set; }
}

internal class Trace2ExecutionContext(
    string threadName,
    DateTimeOffset? startTime = null)
{
    public AsyncLocal<int> RegionNesting { get; } = new();
    public AsyncLocal<DateTimeOffset?> RegionStartTime { get; } = new();
    public DateTimeOffset StartTime { get; } = startTime ?? DateTimeOffset.UtcNow;
    public string ThreadName { get; } = threadName;
}

internal class ThreadScope : DisposableObject
{
    private readonly string _filePath;
    private readonly int _lineNumber;
    private readonly Trace2ExecutionContext _context;
    private readonly Trace2ExecutionContext _prevContext;
    private readonly DateTimeOffset _startTime;

    public ThreadScope(string threadName, string filePath, int lineNumber)
    {
        _prevContext = Trace2.GetCurrentContext();
        _filePath = filePath;
        _lineNumber = lineNumber;

        _context = new Trace2ExecutionContext(threadName);
        Trace2.SetContext(_context);

        _startTime = Trace2.WriteThreadStart(_context.ThreadName, _filePath, _lineNumber);
    }

    protected override void ReleaseManagedResources()
    {
        try
        {
            Trace2.WriteThreadExit(_context.ThreadName, _startTime, _filePath, _lineNumber);
        }
        finally
        {
            Debug.Assert(
                ReferenceEquals(Trace2.GetCurrentContext(), _context),
                "Trace2 threads must be disposed in LIFO order.");

            Trace2.SetContext(_prevContext);
        }
    }
}

internal class ContextScope : DisposableObject
{
    private readonly Trace2ExecutionContext _context;
    private readonly Trace2ExecutionContext _previousContext;

    public ContextScope(Trace2ExecutionContext context)
    {
        _context = context;
        _previousContext = Trace2.GetCurrentContext();
        Trace2.SetContext(_context);
    }

    protected override void ReleaseManagedResources()
    {
        Debug.Assert(
            ReferenceEquals(Trace2.GetCurrentContext(), _context),
            "Trace2 contexts must be disposed in LIFO order.");

        Trace2.SetContext(_previousContext);
    }
}

internal class RegionScope : DisposableObject
{
    private readonly Trace2ExecutionContext _context;
    private readonly string _category;
    private readonly string _label;
    private readonly string _filePath;
    private readonly int _lineNumber;
    private readonly string _message;
    private readonly int _nesting;
    private readonly DateTimeOffset? _previousRegionStartTime;
    private readonly DateTimeOffset _startTime;

    internal RegionScope(
        Trace2ExecutionContext context,
        string category,
        string label,
        string filePath,
        int lineNumber,
        string message)
    {
        _context = context;
        _category = category;
        _label = label;
        _filePath = filePath;
        _lineNumber = lineNumber;
        _message = message;

        // Increment nesting level as we enter the region
        _nesting = _context.RegionNesting.Value + 1;
        _context.RegionNesting.Value = _nesting;
        _previousRegionStartTime = _context.RegionStartTime.Value;

        _startTime = Trace2.WriteRegionEnter(
            _category,
            _label,
            _message,
            _context.ThreadName,
            _nesting,
            _filePath,
            _lineNumber);
        _context.RegionStartTime.Value = _startTime;
    }

    protected override void ReleaseManagedResources()
    {
        try
        {
            Trace2.WriteRegionLeave(
                _startTime,
                _category,
                _label,
                _message,
                _context.ThreadName,
                _nesting,
                _filePath,
                _lineNumber);
        }
        finally
        {
            Debug.Assert(
                _context.RegionNesting.Value == _nesting,
                "Trace2 regions must be disposed in LIFO order.");

            // Decrement the nesting level
            _context.RegionNesting.Value = Math.Max(0, _nesting - 1);
            _context.RegionStartTime.Value = _previousRegionStartTime;
        }
    }
}

/// <summary>
/// The application's process-wide TRACE2 tracing system.
/// </summary>
public static class Trace2
{
    internal const string SidEnvar = "GIT_TRACE2_PARENT_SID";
    internal const string ParentNameEnvar = "GIT_TRACE2_PARENT_NAME";
    private const string MainThreadName = "main";

    private static readonly Lock WritersLock = new();
    private static readonly List<ITrace2Writer> Writers = new();
    private static readonly AsyncLocal<Trace2ExecutionContext> ThreadContext = new();

    private static bool _initialized;
    private static DateTimeOffset _applicationStartTime;
    private static Trace2Settings _settings;
    private static Trace2ExecutionContext _mainContext;
    private static string _sid;
    private static int _depth;

    // Increment for each new logical thread created
    private static int _nextThreadId;

    public static void Initialize(
        string[] args,
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        if (_initialized)
        {
            // Already initialised!
            return;
        }

        _applicationStartTime = DateTimeOffset.UtcNow;
        _sid = CreateSid();
        Environment.SetEnvironmentVariable(SidEnvar, _sid);

        _depth = GetProcessDepth(_sid);
        _settings = ReadSettings();

        InitializeWriters();

        // The main thread context is ambiently created with the process and Trace2 init
        _mainContext = new Trace2ExecutionContext(MainThreadName, _applicationStartTime);
        ThreadContext.Value = _mainContext;

        _initialized = true;

        string appPath = Environment.ProcessPath ?? Environment.GetCommandLineArgs()[0];
        Start(appPath, args, filePath, lineNumber);
    }

    /// <summary>
    /// Create the TRACE2 "session id" (sid) for this process.
    /// </summary>
    internal static string CreateSid()
    {
        // Use trim to ensure no accidental leading or trailing slashes
        var sid = Environment.GetEnvironmentVariable(SidEnvar)?.Trim('/');

        // If we are the root process we must create our own 'root' SID,
        // otherwise append a new UUID to the existing root.
        sid = string.IsNullOrEmpty(sid)
            ? Guid.NewGuid().ToString("D")
            : $"{sid}/{Guid.NewGuid():D}";

        return sid;
    }

    /// <summary>
    /// Get "depth" of current process relative to top-level GCM process.
    /// </summary>
    /// <returns>Depth of current process.</returns>
    internal static int GetProcessDepth(string sid)
    {
        const char processSeparator = '/';

        int count = 0;
        for (var i = 0; i < sid.Length; i++) // use for-loop to avoid IEnumerable overhead from a foreach-loop
        {
            if (sid[i] == processSeparator)
                count++;
        }

        return count;
    }

    internal static void SetContext(Trace2ExecutionContext context)
    {
        ThreadContext.Value = context;
    }

    internal static Trace2ExecutionContext GetCurrentContext()
    {
        Trace2ExecutionContext context = ThreadContext.Value;
        Debug.Assert(context is not null, "Trace2 event emitted without an execution context.");
        // Fall back to the main thread context if we are missing one.
        // This can happen when ExecutionContext flow is suppressed, an unsafe
        // ThreadPool API is used, or work runs on a manually created thread without
        // creating a new Trace2 thread scope.
        return context ?? _mainContext;
    }

    internal static IDisposable UseMainContext()
    {
        if (!_initialized)
            return NoOpDisposable.Instance;

        return new ContextScope(_mainContext);
    }

    private static void Start(string appPath,
        string[] args,
        string filePath,
        int lineNumber)
    {
        if (!AssemblyUtils.TryGetAssemblyVersion(out string version))
        {
            // A version is required for TRACE2, so if this call fails
            // manually set the version.
            version = "0.0.0";
        }
        WriteVersion(version, filePath, lineNumber);
        WriteStart(appPath, args, filePath, lineNumber);
    }

    public static void Stop(
        int exitCode,
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        if (!_initialized) return;

        WriteExit(exitCode, filePath, lineNumber);
        DisposeWriters();
    }

    /// <summary>
    /// Writes the canonical name of the command being run.
    /// </summary>
    /// <param name="name">The canonical command name.</param>
    /// <param name="filePath">The source file writing the event.</param>
    /// <param name="lineNumber">The source line writing the event.</param>
    /// <remarks>
    /// The command hierarchy is inherited from the parent process and extended
    /// for child processes through <c>GIT_TRACE2_PARENT_NAME</c>.
    /// </remarks>
    public static void WriteCommandName(
        string name,
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        if (!_initialized) return;

        EnsureArgument.NotNullOrWhiteSpace(name, nameof(name));

        string parentName = Environment.GetEnvironmentVariable(ParentNameEnvar);
        string hierarchy = string.IsNullOrEmpty(parentName)
            ? name
            : $"{parentName}/{name}";

        Environment.SetEnvironmentVariable(ParentNameEnvar, hierarchy);

        WriteMessage(new CommandNameMessage
        {
            Event = Trace2Event.CommandName,
            Sid = _sid,
            Time = DateTimeOffset.UtcNow,
            Thread = GetCurrentContext().ThreadName,
            File = Path.GetFileName(filePath),
            Line = lineNumber,
            Name = name,
            Hierarchy = hierarchy,
            Depth = _depth
        });
    }

    /// <summary>
    /// Writes the variant or mode of the command being run.
    /// </summary>
    /// <param name="mode">The command mode.</param>
    /// <param name="filePath">The source file writing the event.</param>
    /// <param name="lineNumber">The source line writing the event.</param>
    public static void WriteCommandMode(
        string mode,
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        if (!_initialized) return;

        EnsureArgument.NotNullOrWhiteSpace(mode, nameof(mode));

        WriteMessage(new CommandModeMessage
        {
            Event = Trace2Event.CommandMode,
            Sid = _sid,
            Time = DateTimeOffset.UtcNow,
            Thread = GetCurrentContext().ThreadName,
            File = Path.GetFileName(filePath),
            Line = lineNumber,
            Name = mode,
            Depth = _depth
        });
    }

    public static DateTimeOffset WriteChildStart(
        int childId,
        Trace2ProcessClass processClass,
        bool useShell,
        string appName,
        string argv,
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        var startTime = DateTimeOffset.UtcNow;

        if (!_initialized)
            return startTime;

        // Always add name of the application the process is executing
        var procArgs = new List<string>
        {
            Path.GetFileName(appName)
        };

        // If the process has arguments, append them.
        if (!string.IsNullOrEmpty(argv))
        {
            procArgs.AddRange(argv.Split(' '));
        }

        WriteMessage(new ChildStartMessage
        {
            Event = Trace2Event.ChildStart,
            Sid = _sid,
            Time = startTime,
            Thread = GetCurrentContext().ThreadName,
            File = Path.GetFileName(filePath),
            Line = lineNumber,
            Id = childId,
            Classification = processClass,
            UseShell = useShell,
            Argv = procArgs,
            ElapsedTime = (DateTimeOffset.UtcNow - _applicationStartTime).TotalSeconds,
            Depth = _depth,
        });
        return startTime;
    }

    public static void WriteChildExit(
        int childId,
        DateTimeOffset startTime,
        int pid,
        int code,
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0) =>
        WriteChildExit(childId, DateTimeOffset.UtcNow - startTime, pid, code, filePath, lineNumber);

    public static void WriteChildExit(
        int childId,
        TimeSpan relativeTime,
        int pid,
        int code,
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0) =>
        WriteChildExit(childId, relativeTime.TotalSeconds, pid, code, filePath, lineNumber);

    public static void WriteChildExit(
        int childId,
        double relativeTime,
        int pid,
        int code,
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        if (!_initialized) return;

        WriteMessage(new ChildExitMessage
        {
            Event = Trace2Event.ChildExit,
            Sid = _sid,
            Time = DateTimeOffset.UtcNow,
            Thread = GetCurrentContext().ThreadName,
            File = Path.GetFileName(filePath),
            Line = lineNumber,
            Id = childId,
            Pid = pid,
            Code = code,
            ElapsedTime = (DateTimeOffset.UtcNow - _applicationStartTime).TotalSeconds,
            RelativeTime = relativeTime,
            Depth = _depth
        });
    }

    public static void WriteError(
        string errorMessage,
        string parameterizedMessage = null,
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        if (!_initialized) return;

        WriteMessage(new ErrorMessage
        {
            Event = Trace2Event.Error,
            Sid = _sid,
            Time = DateTimeOffset.UtcNow,
            Thread = GetCurrentContext().ThreadName,
            File = Path.GetFileName(filePath),
            Line = lineNumber,
            Message = errorMessage,
            ParameterizedMessage = parameterizedMessage ?? errorMessage,
            Depth = _depth
        });
    }

    /// <summary>
    /// Creates a logical Trace2 thread scope.
    /// </summary>
    /// <param name="name">The descriptive name of the logical thread.</param>
    /// <param name="filePath">The source file creating the scope.</param>
    /// <param name="lineNumber">The source line creating the scope.</param>
    /// <returns>
    /// A scope that emits <c>thread_exit</c> and restores the previous logical
    /// thread context when disposed.
    /// </returns>
    /// <remarks>
    /// The scope emits <c>thread_start</c> when created. Its context flows
    /// through normal execution-context transitions, including
    /// <see langword="await"/> and <see cref="System.Threading.Tasks.Task.Run(Action)"/>.
    /// Nested scopes must be disposed in LIFO order. Independently concurrent
    /// operations should create their scopes within their respective execution
    /// contexts.
    /// </remarks>
    public static IDisposable CreateThread(
        string name,
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        if (!_initialized)
            return NoOpDisposable.Instance;

        // Create new logical thread execution context
        int id = Interlocked.Increment(ref _nextThreadId);
        string fullName = CreateThreadName(id, name);

        return new ThreadScope(fullName, filePath, lineNumber);
    }

    internal static DateTimeOffset WriteThreadStart(
        string name,
        string filePath = "",
        int lineNumber = 0)
    {
        var startTime = DateTimeOffset.UtcNow;

        if (_initialized)
        {
            WriteMessage(new ThreadStartMessage
            {
                Event = Trace2Event.ThreadStart,
                Sid = _sid,
                Time = startTime,
                Thread = name,
                File = Path.GetFileName(filePath),
                Line = lineNumber,
                Depth = _depth
            });
        }

        return startTime;
    }

    internal static void WriteThreadExit(
        string name,
        DateTimeOffset startTime,
        string filePath,
        int lineNumber) =>
        WriteThreadExit(name, DateTimeOffset.UtcNow - startTime, filePath, lineNumber);

    internal static void WriteThreadExit(
        string name,
        TimeSpan relativeTime,
        string filePath,
        int lineNumber) =>
        WriteThreadExit(name, relativeTime.TotalSeconds, filePath, lineNumber);

    internal static void WriteThreadExit(
        string name,
        double relativeTime,
        string filePath,
        int lineNumber)
    {
        if (!_initialized) return;

        WriteMessage(new ThreadExitMessage
        {
            Event = Trace2Event.ThreadExit,
            Sid = _sid,
            Time = DateTimeOffset.UtcNow,
            Thread = name,
            File = Path.GetFileName(filePath),
            Line = lineNumber,
            RelativeTime = relativeTime,
            Depth = _depth
        });
    }

    /// <summary>
    /// Creates a nested Trace2 region on the current logical thread.
    /// </summary>
    /// <param name="category">The broad category of work represented by the region.</param>
    /// <param name="label">The name of the operation represented by the region.</param>
    /// <param name="message">
    /// An optional event message. When omitted, <paramref name="label"/> is used.
    /// </param>
    /// <param name="filePath">The source file creating the region.</param>
    /// <param name="lineNumber">The source line creating the region.</param>
    /// <returns>
    /// A scope that emits <c>region_leave</c> and restores the previous nesting
    /// level when disposed.
    /// </returns>
    /// <remarks>
    /// The scope emits <c>region_enter</c> when created. Region nesting is
    /// maintained per logical thread and flows across normal asynchronous
    /// continuations. Regions must be disposed in LIFO order.
    /// </remarks>
    public static IDisposable CreateRegion(
        string category,
        string label,
        string message = "",
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        if (!_initialized)
            return NoOpDisposable.Instance;

        Trace2ExecutionContext context = GetCurrentContext();
        return new RegionScope(context, category, label, filePath, lineNumber, message);
    }

    /// <summary>
    /// Writes a thread- and region-local TRACE2 data event.
    /// </summary>
    /// <param name="category">The broad category of the data.</param>
    /// <param name="key">The name of the data value.</param>
    /// <param name="value">The data value.</param>
    /// <param name="filePath">The source file writing the event.</param>
    /// <param name="lineNumber">The source line writing the event.</param>
    public static void WriteData(
        string category,
        string key,
        string value,
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        if (!_initialized) return;

        EnsureArgument.NotNullOrWhiteSpace(category, nameof(category));
        EnsureArgument.NotNullOrWhiteSpace(key, nameof(key));

        value ??= string.Empty;

        DateTimeOffset now = DateTimeOffset.UtcNow;
        Trace2ExecutionContext context = GetCurrentContext();
        DateTimeOffset relativeStart = context.RegionStartTime.Value ?? context.StartTime;

        WriteMessage(new DataMessage
        {
            Event = Trace2Event.Data,
            Sid = _sid,
            Time = now,
            Thread = context.ThreadName,
            File = Path.GetFileName(filePath),
            Line = lineNumber,
            ElapsedTime = (now - _applicationStartTime).TotalSeconds,
            RelativeTime = (now - relativeStart).TotalSeconds,
            Nesting = context.RegionNesting.Value + 1,
            Category = category,
            Key = key,
            Value = value,
            Depth = _depth
        });
    }

    /// <summary>
    /// Writes a thread- and region-local integer TRACE2 data event.
    /// </summary>
    /// <param name="category">The broad category of the data.</param>
    /// <param name="key">The name of the data value.</param>
    /// <param name="value">The data value.</param>
    /// <param name="filePath">The source file writing the event.</param>
    /// <param name="lineNumber">The source line writing the event.</param>
    public static void WriteData(
        string category,
        string key,
        long value,
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        WriteData(
            category,
            key,
            value.ToString(CultureInfo.InvariantCulture),
            filePath,
            lineNumber);
    }

    /// <summary>
    /// Writes a thread- and region-local TRACE2 structured data event.
    /// </summary>
    /// <param name="category">The broad category of the data.</param>
    /// <param name="key">The name of the data value.</param>
    /// <param name="value">The structured JSON value.</param>
    /// <param name="filePath">The source file writing the event.</param>
    /// <param name="lineNumber">The source line writing the event.</param>
    public static void WriteDataJson(
        string category,
        string key,
        JsonElement value,
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        if (!_initialized) return;

        EnsureArgument.NotNullOrWhiteSpace(category, nameof(category));
        EnsureArgument.NotNullOrWhiteSpace(key, nameof(key));

        if (value.ValueKind == JsonValueKind.Undefined)
        {
            throw new ArgumentException("JSON value must be defined.", nameof(value));
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        Trace2ExecutionContext context = GetCurrentContext();
        DateTimeOffset relativeStart = context.RegionStartTime.Value ?? context.StartTime;

        WriteMessage(new DataJsonMessage
        {
            Event = Trace2Event.DataJson,
            Sid = _sid,
            Time = now,
            Thread = context.ThreadName,
            File = Path.GetFileName(filePath),
            Line = lineNumber,
            ElapsedTime = (now - _applicationStartTime).TotalSeconds,
            RelativeTime = (now - relativeStart).TotalSeconds,
            Nesting = context.RegionNesting.Value + 1,
            Category = category,
            Key = key,
            Value = value,
            Depth = _depth
        });
    }

    internal static DateTimeOffset WriteRegionEnter(
        string category,
        string label,
        string message,
        string thread,
        int nesting,
        string filePath,
        int lineNumber)
    {
        var start = DateTimeOffset.UtcNow;

        if (_initialized)
        {
            WriteMessage(new RegionEnterMessage
            {
                Event = Trace2Event.RegionEnter,
                Sid = _sid,
                Time = start,
                Category = category,
                Label = label,
                Message = message == "" ? label : message,
                Thread = thread,
                File = Path.GetFileName(filePath),
                Line = lineNumber,
                ElapsedTime = (start - _applicationStartTime).TotalSeconds,
                Nesting = nesting,
                Depth = _depth
            });
        }

        return start;
    }

    internal static void WriteRegionLeave(
        DateTimeOffset startTime,
        string category,
        string label,
        string message,
        string thread,
        int nesting,
        string filePath,
        int lineNumber) =>
        WriteRegionLeave(DateTimeOffset.UtcNow - startTime, category, label, message, thread, nesting, filePath, lineNumber);

    internal static void WriteRegionLeave(
        TimeSpan relativeTime,
        string category,
        string label,
        string message,
        string thread,
        int nesting,
        string filePath,
        int lineNumber) =>
        WriteRegionLeave(relativeTime.TotalSeconds, category, label, message, thread, nesting, filePath, lineNumber);

    internal static void WriteRegionLeave(
        double relativeTime,
        string category,
        string label,
        string message,
        string thread,
        int nesting,
        string filePath,
        int lineNumber)
    {
        if (!_initialized) return;

        WriteMessage(new RegionLeaveMessage
        {
            Event = Trace2Event.RegionLeave,
            Sid = _sid,
            Time = DateTimeOffset.UtcNow,
            Category = category,
            Label = label,
            Message = message == "" ? label : message,
            Thread = thread,
            File = Path.GetFileName(filePath),
            Line = lineNumber,
            ElapsedTime = (DateTimeOffset.UtcNow - _applicationStartTime).TotalSeconds,
            RelativeTime = relativeTime,
            Nesting = nesting,
            Depth = _depth
        });
    }

    private static void DisposeWriters()
    {
        lock (WritersLock)
        {
            try
            {
                for (int i = Writers.Count - 1; i >= 0; i--)
                {
                    using (Writers[i])
                    {
                        Writers.RemoveAt(i);
                    }
                }
            }
            catch
            {
                /* squelch */
            }
        }
    }

    internal static bool TryGetPipeName(string eventTarget, out string name)
    {
        // Use prefixes to determine whether target is a named pipe/socket
        if (eventTarget.StartsWith("af_unix:", StringComparison.OrdinalIgnoreCase) ||
            eventTarget.StartsWith(@"\\.\pipe\", StringComparison.OrdinalIgnoreCase) ||
            eventTarget.StartsWith("//./pipe/", StringComparison.OrdinalIgnoreCase))
        {
            name = PlatformUtils.IsWindows()
                ? eventTarget.Replace('/', '\\')
                    .TrimUntilIndexOf(@"\\.\pipe\")
                : eventTarget.Replace("af_unix:dgram:", "")
                    .Replace("af_unix:stream:", "")
                    .Replace("af_unix:", "");
            return true;
        }

        name = "";
        return false;
    }

    private static Trace2Settings ReadSettings()
    {
        var settings = new Trace2Settings();

        AddTarget(settings, Trace2FormatTarget.Event,
            Constants.EnvironmentVariables.GitTrace2Event,
            Constants.GitConfiguration.Trace2.EventTarget);
        AddTarget(settings, Trace2FormatTarget.Normal,
            Constants.EnvironmentVariables.GitTrace2Normal,
            Constants.GitConfiguration.Trace2.NormalTarget);
        AddTarget(settings, Trace2FormatTarget.Performance,
            Constants.EnvironmentVariables.GitTrace2Performance,
            Constants.GitConfiguration.Trace2.PerformanceTarget);

        return settings;
    }

    private static void AddTarget(
        Trace2Settings settings,
        Trace2FormatTarget format,
        string environmentVariable,
        string configurationProperty)
    {
        string value = Environment.GetEnvironmentVariable(environmentVariable);
        if (value is null)
        {
            string key = $"{Constants.GitConfiguration.Trace2.SectionName}.{configurationProperty}";
            value = ReadGitConfig(key);
        }

        if (value is not null)
        {
            settings.FormatTargetsAndValues.Add(format, value);
        }
    }

    private static string ReadGitConfig(string key)
    {
        string programName = OperatingSystem.IsWindows() ? "git.exe" : "git";
        string gitExecPath = Environment.GetEnvironmentVariable(Constants.EnvironmentVariables.GitExecutablePath);
        string candidatePath = string.IsNullOrEmpty(gitExecPath)
            ? null
            : Path.Combine(gitExecPath, programName);
        string gitPath = candidatePath is not null && File.Exists(candidatePath)
            ? candidatePath
            : programName;

        try
        {
            var startInfo = new ProcessStartInfo(gitPath, $"config --get {key}")
            {
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            using Process process = Process.Start(startInfo);
            string value = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 ? value.TrimEnd('\r', '\n') : null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static void InitializeWriters()
    {
        // Set up the correct writer for every enabled format target.
        foreach (var formatTarget in _settings.FormatTargetsAndValues)
        {
            if (TryGetPipeName(formatTarget.Value, out string name)) // Write to named pipe/socket
            {
                AddWriter(new Trace2CollectorWriter(formatTarget.Key, (
                        () => new NamedPipeClientStream(".", name,
                            PipeDirection.Out,
                            PipeOptions.Asynchronous)
                    )
                ));
            }
            else if (formatTarget.Value.IsTruthy()) // Write to stderr
            {
                AddWriter(new Trace2StreamWriter(formatTarget.Key, Console.Error));
            }
            else if (Path.IsPathRooted(formatTarget.Value)) // Write to file
            {
                try
                {
                    AddWriter(new Trace2FileWriter(formatTarget.Key, formatTarget.Value));
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"warning: unable to trace to file '{formatTarget.Value}': {ex.Message}");
                }
            }
        }
    }

    private static void WriteVersion(
        string gcmVersion,
        string filePath,
        int lineNumber,
        string eventFormatVersion = "3")
    {
        EnsureArgument.NotNull(gcmVersion, nameof(gcmVersion));

        WriteMessage(new VersionMessage
        {
            Event = Trace2Event.Version,
            Sid = _sid,
            Time = DateTimeOffset.UtcNow,
            Thread = GetCurrentContext().ThreadName,
            File = Path.GetFileName(filePath),
            Line = lineNumber,
            Evt = eventFormatVersion,
            Exe = gcmVersion
        });
    }

    private static void WriteStart(
        string appPath,
        string[] args,
        string filePath,
        int lineNumber)
    {
        // Prepend GCM exe to arguments
        var argv = new List<string>
        {
            Path.GetFileName(appPath),
        };

        if (args.Length > 0)
        {
            argv.AddRange(args);
        }

        WriteMessage(new StartMessage
        {
            Event = Trace2Event.Start,
            Sid = _sid,
            Time = DateTimeOffset.UtcNow,
            Thread = GetCurrentContext().ThreadName,
            File = Path.GetFileName(filePath),
            Line = lineNumber,
            Argv = argv,
            ElapsedTime = (DateTimeOffset.UtcNow - _applicationStartTime).TotalSeconds
        });
    }

    private static void WriteExit(int code, string filePath = "", int lineNumber = 0)
    {
        EnsureArgument.NotNull(code, nameof(code));

        WriteMessage(new ExitMessage
        {
            Event = Trace2Event.Exit,
            Sid = _sid,
            Time = DateTimeOffset.UtcNow,
            Thread = GetCurrentContext().ThreadName,
            File = Path.GetFileName(filePath),
            Line = lineNumber,
            Code = code,
            ElapsedTime = (DateTimeOffset.UtcNow - _applicationStartTime).TotalSeconds
        });
    }

    private static void AddWriter(ITrace2Writer writer)
    {
        lock (WritersLock)
        {
            // Try not to add the same writer more than once
            if (Writers.Contains(writer))
                return;

            Writers.Add(writer);
        }
    }

    private static void WriteMessage(Trace2Message message)
    {
        if (!_initialized)
        {
            return;
        }

        lock (WritersLock)
        {
            if (Writers.Count == 0)
            {
                return;
            }

            foreach (var writer in Writers)
            {
                if (!writer.Failed)
                {
                    writer.Write(message);
                }
            }
        }
    }

    private static string CreateThreadName(int id, string name)
    {
        // If we don't have a name for this thread then give it a generic name
        if (string.IsNullOrEmpty(name))
        {
            name = "unknown";
        }

        // Threads should be named "th%d:%s" per Trace2 convention,
        // where %d is the ID and %s is the thread name.
        return $"th{id}:{name}";
    }

    private class NoOpDisposable : IDisposable
    {
        public static readonly IDisposable Instance = new NoOpDisposable();
        public void Dispose(){}
    }
}
