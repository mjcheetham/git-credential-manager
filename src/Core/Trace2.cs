using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
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
public class PerformanceFormatSpan
{
    public int Size { get; set; }

    public int BeginPadding { get; set; }

    public int EndPadding { get; set; }
}

internal class RegionScope : DisposableObject
{
    private readonly string _category;
    private readonly string _label;
    private readonly string _filePath;
    private readonly int _lineNumber;
    private readonly string _message;
    private readonly string _thread;
    private readonly int _nesting;
    private readonly DateTimeOffset _startTime;

    internal RegionScope(
        string category,
        string label,
        string filePath,
        int lineNumber,
        string message,
        string thread,
        int nesting)
    {
        _category = category;
        _label = label;
        _filePath = filePath;
        _lineNumber = lineNumber;
        _message = message;
        _thread = thread;
        _nesting = nesting;

        _startTime = DateTimeOffset.UtcNow;

        Trace2.WriteRegionEnter(_category, _label, _message, _thread, _nesting, _filePath, _lineNumber);
    }

    protected override void ReleaseManagedResources()
    {
        double relativeTime = (DateTimeOffset.UtcNow - _startTime).TotalSeconds;
        Trace2.WriteRegionLeave(
            relativeTime, _category, _label, _message, _thread, _nesting, _filePath, _lineNumber);
        Trace2.CompleteRegion(_nesting);
    }
}

/// <summary>
/// The application's process-wide TRACE2 tracing system.
/// </summary>
public static class Trace2
{
    internal const string SidEnvar = "GIT_TRACE2_PARENT_SID";

    private static readonly Lock WritersLock = new();
    private static readonly List<ITrace2Writer> Writers = new();
    private static readonly AsyncLocal<int> RegionNesting = new();

    private static bool _initialized;
    private static DateTimeOffset _applicationStartTime;
    private static Trace2Settings _settings;
    private static string _sid;
    private static int _depth;

    public static void Initialize(
        string[] args,
        [System.Runtime.CompilerServices.CallerFilePath] string filePath = "",
        [System.Runtime.CompilerServices.CallerLineNumber] int lineNumber = 0)
    {
        if (_initialized)
        {
            return;
        }

        _applicationStartTime = DateTimeOffset.UtcNow;
        _sid = CreateSid();
        Environment.SetEnvironmentVariable(SidEnvar, _sid);

        _depth = GetProcessDepth(_sid);
        _settings = ReadSettings();

        InitializeWriters();
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
        [System.Runtime.CompilerServices.CallerFilePath] string filePath = "",
        [System.Runtime.CompilerServices.CallerLineNumber] int lineNumber = 0)
    {
        WriteExit(exitCode, filePath, lineNumber);
        DisposeWriters();
    }

    public static DateTimeOffset WriteChildStart(
        int childId,
        Trace2ProcessClass processClass,
        bool useShell,
        string appName,
        string argv,
        [System.Runtime.CompilerServices.CallerFilePath] string filePath = "",
        [System.Runtime.CompilerServices.CallerLineNumber] int lineNumber = 0)
    {
        var startTime = DateTimeOffset.UtcNow;

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
            Thread = BuildThreadName(),
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
        [System.Runtime.CompilerServices.CallerFilePath] string filePath = "",
        [System.Runtime.CompilerServices.CallerLineNumber] int lineNumber = 0) =>
        WriteChildExit(childId, DateTimeOffset.UtcNow - startTime, pid, code, filePath, lineNumber);

    public static void WriteChildExit(
        int childId,
        TimeSpan relativeTime,
        int pid,
        int code,
        [System.Runtime.CompilerServices.CallerFilePath] string filePath = "",
        [System.Runtime.CompilerServices.CallerLineNumber] int lineNumber = 0) =>
        WriteChildExit(childId, relativeTime.TotalSeconds, pid, code, filePath, lineNumber);

    public static void WriteChildExit(
        int childId,
        double relativeTime,
        int pid,
        int code,
        [System.Runtime.CompilerServices.CallerFilePath] string filePath = "",
        [System.Runtime.CompilerServices.CallerLineNumber] int lineNumber = 0)
    {
        WriteMessage(new ChildExitMessage
        {
            Event = Trace2Event.ChildExit,
            Sid = _sid,
            Time = DateTimeOffset.UtcNow,
            Thread = BuildThreadName(),
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
        [System.Runtime.CompilerServices.CallerFilePath] string filePath = "",
        [System.Runtime.CompilerServices.CallerLineNumber] int lineNumber = 0)
    {
        WriteMessage(new ErrorMessage
        {
            Event = Trace2Event.Error,
            Sid = _sid,
            Time = DateTimeOffset.UtcNow,
            Thread = BuildThreadName(),
            File = Path.GetFileName(filePath),
            Line = lineNumber,
            Message = errorMessage,
            ParameterizedMessage = parameterizedMessage ?? errorMessage,
            Depth = _depth
        });
    }

    public static DateTimeOffset WriteThreadStart(
        [System.Runtime.CompilerServices.CallerFilePath] string filePath = "",
        [System.Runtime.CompilerServices.CallerLineNumber] int lineNumber = 0)
    {
        var startTime = DateTimeOffset.UtcNow;
        WriteMessage(new ThreadStartMessage
        {
            Event = Trace2Event.ThreadStart,
            Sid = _sid,
            Time = startTime,
            Thread = BuildThreadName(),
            File = Path.GetFileName(filePath),
            Line = lineNumber,
            Depth = _depth
        });
        return startTime;
    }

    public static void WriteThreadExit(
        DateTimeOffset startTime,
        [System.Runtime.CompilerServices.CallerFilePath] string filePath = "",
        [System.Runtime.CompilerServices.CallerLineNumber] int lineNumber = 0) =>
        WriteThreadExit(DateTimeOffset.UtcNow - startTime, filePath, lineNumber);

    public static void WriteThreadExit(
        TimeSpan relativeTime,
        [System.Runtime.CompilerServices.CallerFilePath] string filePath = "",
        [System.Runtime.CompilerServices.CallerLineNumber] int lineNumber = 0) =>
        WriteThreadExit(relativeTime.TotalSeconds, filePath, lineNumber);

    public static void WriteThreadExit(
        double relativeTime,
        [System.Runtime.CompilerServices.CallerFilePath] string filePath = "",
        [System.Runtime.CompilerServices.CallerLineNumber] int lineNumber = 0)
    {
        WriteMessage(new ThreadExitMessage
        {
            Event = Trace2Event.ThreadExit,
            Sid = _sid,
            Time = DateTimeOffset.UtcNow,
            Thread = BuildThreadName(),
            File = Path.GetFileName(filePath),
            Line = lineNumber,
            RelativeTime = relativeTime,
            Depth = _depth
        });
    }

    public static IDisposable CreateRegion(
        string category,
        string label,
        string message = "",
        [System.Runtime.CompilerServices.CallerFilePath] string filePath = "",
        [System.Runtime.CompilerServices.CallerLineNumber] int lineNumber = 0)
    {
        int nesting = RegionNesting.Value + 1;
        RegionNesting.Value = nesting;
        return new RegionScope(category, label, filePath, lineNumber, message, BuildThreadName(), nesting);
    }

    internal static void WriteRegionEnter(
        string category,
        string label,
        string message,
        string thread,
        int nesting,
        string filePath,
        int lineNumber)
    {
        WriteMessage(new RegionEnterMessage
        {
            Event = Trace2Event.RegionEnter,
            Sid = _sid,
            Time = DateTimeOffset.UtcNow,
            Category = category,
            Label = label,
            Message = message == "" ? label : message,
            Thread = thread,
            File = Path.GetFileName(filePath),
            Line = lineNumber,
            ElapsedTime = (DateTimeOffset.UtcNow - _applicationStartTime).TotalSeconds,
            Nesting = nesting,
            Depth = _depth
        });
    }

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

    internal static void CompleteRegion(int nesting)
    {
        RegionNesting.Value = Math.Max(0, nesting - 1);
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
            Thread = BuildThreadName(),
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
            Thread = BuildThreadName(),
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
            Thread = BuildThreadName(),
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

    private static string BuildThreadName()
    {
        int id = Environment.CurrentManagedThreadId;
        string name = Thread.CurrentThread.Name;

        // If this is the entry thread, call it "main", per Trace2 convention
        if (id == 1)
        {
            return "main";
        }

        // If this is a thread pool thread then name it as such
        if (Thread.CurrentThread.IsThreadPoolThread)
        {
            name = "thread_pool";
        }

        // If we don't have a name for this thread then give it a generic name
        if (string.IsNullOrEmpty(name))
        {
            name = "unknown";
        }

        // Threads should be named "th%d:%s" per Trace2 convention,
        // where %d is the ID and %s is the thread name.
        return $"th{id}:{name}";
    }
}

