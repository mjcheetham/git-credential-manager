using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GitCredentialManager.UsageSurvey;

namespace GitCredentialManager.Commands;

/// <summary>
/// Top-level <c>usage survey</c> command tree. Provides user-facing controls for
/// toggling usage survey, inspecting the local queue, viewing the persistent install id,
/// and running the dispatcher (both as a hidden background process and as a foreground
/// inspector via <c>usage-survey show</c>).
/// </summary>
public class UsageSurveyCommand : Command
{
    private readonly ICommandContext _context;

    public UsageSurveyCommand(ICommandContext context)
        : base("usage-survey", "Manage Git Credential Manager's optional, privacy-preserving usage survey.")
    {
        EnsureArgument.NotNull(context, nameof(context));
        _context = context;

        AddCommand(BuildOnCommand());
        AddCommand(BuildOffCommand());
        AddCommand(BuildStatusCommand());
        AddCommand(BuildShowCommand());
        AddCommand(BuildIdCommand());
        AddCommand(BuildPurgeCommand());
        AddCommand(BuildDispatchCommand());
    }

    private Command BuildOnCommand()
    {
        var cmd = new Command("on", "Opt in to anonymous, aggregate usage survey.");
        cmd.SetHandler(EnableAsync);
        return cmd;
    }

    private Command BuildOffCommand()
    {
        var cmd = new Command("off", "Opt out of usage survey. Queued events on disk are left intact; use 'usage-survey purge' to remove them.");
        cmd.SetHandler(DisableAsync);
        return cmd;
    }

    private Command BuildStatusCommand()
    {
        var cmd = new Command("status", "Print current usage-survey status and queue state.");
        cmd.SetHandler(ShowStatusAsync);
        return cmd;
    }

    private Command BuildShowCommand()
    {
        var cmd = new Command("show",
            "Run the dispatcher in the foreground and print each event being shipped to stdout. " +
            "Holds the dispatcher pidfile while running. Exit with Ctrl-C to release; " +
            "a background dispatcher will resume draining on the next GCM invocation.");
        cmd.SetHandler(ShowAsync);
        return cmd;
    }

    private Command BuildIdCommand()
    {
        var cmd = new Command("id", "Print the persistent usage survey install id, or reset it with --reset.");
        var resetOpt = new Option<bool>("--reset", "Generate a new install id.");
        cmd.AddOption(resetOpt);
        cmd.SetHandler(ShowOrResetIdAsync, resetOpt);
        return cmd;
    }

    private Command BuildPurgeCommand()
    {
        var cmd = new Command("purge", "Delete any queued usage survey events on disk. Does not change opt-in state and does not reset the install id.");
        cmd.SetHandler(PurgeAsync);
        return cmd;
    }

    private Command BuildDispatchCommand()
    {
        // Hidden internal command spawned by the producer to ship events.
        var cmd = new Command("dispatch", "(internal) Run the background usage-survey dispatcher.")
        {
            IsHidden = true,
        };
        cmd.SetHandler(DispatchAsync);
        return cmd;
    }

    private Task EnableAsync()
    {
        IGitConfiguration config = _context.Git.GetConfiguration();
        string key = $"{Constants.GitConfiguration.Credential.SectionName}.{Constants.GitConfiguration.Credential.UsageSurvey}";
        config.Set(GitConfigurationLevel.Global, key, "true");

        // Materialise an install id now so the first usage-survey recording invocation
        // doesn't pay that cost. Also so the user can see it.
        var paths = new UsageSurveyPaths(_context.FileSystem);
        var installId = new InstallId(_context.FileSystem, paths, _context.Trace);
        Guid id = installId.GetOrCreate();

        _context.Streams.Out.WriteLine("Usage survey is now ENABLED.");
        _context.Streams.Out.WriteLine($"Install ID: {id:D}");
        _context.Streams.Out.WriteLine($"See {Constants.HelpUrls.GcmUsageSurvey} for what is collected and how to inspect it.");
        return Task.CompletedTask;
    }

    private Task DisableAsync()
    {
        IGitConfiguration config = _context.Git.GetConfiguration();
        string key = $"{Constants.GitConfiguration.Credential.SectionName}.{Constants.GitConfiguration.Credential.UsageSurvey}";
        config.Set(GitConfigurationLevel.Global, key, "false");

        _context.Streams.Out.WriteLine("Usage survey is now DISABLED.");
        _context.Streams.Out.WriteLine("Queued events on disk are NOT deleted automatically; run 'git credential-manager usage-survey purge' to remove them.");
        return Task.CompletedTask;
    }

    private Task ShowStatusAsync()
    {
        var paths = new UsageSurveyPaths(_context.FileSystem);
        var installId = new InstallId(_context.FileSystem, paths, _context.Trace);

        bool enabled = _context.UsageSurvey?.IsEnabled ?? false;
        Guid? id = installId.TryGet();

        int queueDepth = CountFiles(paths.EventsDirectory, includeAllAges: true, out _);
        int sentCount24h = CountFiles(paths.SentDirectory, includeAllAges: false, out _);

        int? dispatcherPid = DispatcherPidFile.TryReadActivePid(_context.FileSystem, paths, _context.Trace);

        _context.Streams.Out.WriteLine($"Usage survey:        {(enabled ? "enabled" : "disabled")}");
        _context.Streams.Out.WriteLine($"Install ID:       {(id.HasValue ? id.Value.ToString("D") : "(not generated)")}");
        _context.Streams.Out.WriteLine($"Queue depth:      {queueDepth} file(s) in {paths.EventsDirectory}");
        _context.Streams.Out.WriteLine(dispatcherPid.HasValue
            ? $"Dispatcher:       running (pid {dispatcherPid.Value})"
            : "Dispatcher:       not running");
        _context.Streams.Out.WriteLine($"Sent (last 24h):  {sentCount24h} event(s) in {paths.SentDirectory}");
        _context.Streams.Out.WriteLine($"Dispatcher log:   {paths.DispatcherLogFile}");
        _context.Streams.Out.WriteLine($"Event versions:   get v{Constants.UsageSurvey.GetEventVersion}");
        _context.Streams.Out.WriteLine($"Privacy details:  {Constants.HelpUrls.GcmUsageSurvey}");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Count *.jsonl files under <paramref name="directory"/>. When
    /// <paramref name="includeAllAges"/> is false, only files whose embedded timestamp
    /// is within the last 24 hours are counted.
    /// </summary>
    private int CountFiles(string directory, bool includeAllAges, out int _unused)
    {
        _unused = 0;
        if (!_context.FileSystem.DirectoryExists(directory))
        {
            return 0;
        }

        DateTimeOffset cutoff = DateTimeOffset.UtcNow - Constants.UsageSurvey.SentRetention;
        int n = 0;
        try
        {
            foreach (string f in _context.FileSystem.EnumerateFiles(directory, "*.jsonl"))
            {
                if (!includeAllAges)
                {
                    if (!UsageSurveyDispatcher.TryParseEventTimestamp(Path.GetFileName(f), out DateTimeOffset ts) ||
                        ts < cutoff)
                    {
                        continue;
                    }
                }
                n++;
            }
        }
        catch (Exception ex)
        {
            _context.Trace.WriteLine($"Usage survey: failed to enumerate '{directory}': {ex.Message}");
        }
        return n;
    }

    private Task ShowOrResetIdAsync(bool reset)
    {
        var paths = new UsageSurveyPaths(_context.FileSystem);
        var installId = new InstallId(_context.FileSystem, paths, _context.Trace);

        if (reset)
        {
            Guid newId = installId.Reset();
            _context.Streams.Out.WriteLine(newId.ToString("D"));
            _context.Streams.Out.WriteLine("(new Install ID generated)");
        }
        else
        {
            Guid? existing = installId.TryGet();
            if (existing.HasValue)
            {
                _context.Streams.Out.WriteLine(existing.Value.ToString("D"));
            }
            else
            {
                _context.Streams.Out.WriteLine("(not generated)");
                _context.Streams.Out.WriteLine(
                    "Run 'git credential-manager usage-survey on' to enable usage survey and create an Install ID,");
                _context.Streams.Out.WriteLine(
                    "or 'git credential-manager usage-survey id --reset' to create one without enabling usage survey.");
            }
        }
        return Task.CompletedTask;
    }

    private Task PurgeAsync()
    {
        var paths = new UsageSurveyPaths(_context.FileSystem);
        int removed = 0;

        removed += PurgeDir(paths.EventsDirectory);
        removed += PurgeDir(paths.SentDirectory);

        _context.Streams.Out.WriteLine($"Removed {removed} queued/archived event file(s).");
        return Task.CompletedTask;

        int PurgeDir(string dir)
        {
            int n = 0;
            if (!_context.FileSystem.DirectoryExists(dir)) return 0;
            try
            {
                foreach (string f in System.Linq.Enumerable.ToArray(
                             _context.FileSystem.EnumerateFiles(dir, "*.jsonl*")))
                {
                    try
                    {
                        _context.FileSystem.DeleteFile(f);
                        n++;
                    }
                    catch (Exception ex)
                    {
                        _context.Trace.WriteLine($"Usage survey: failed to delete '{f}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _context.Trace.WriteLine($"Usage survey: failed to enumerate '{dir}' for purge: {ex.Message}");
            }
            return n;
        }
    }

    private async Task ShowAsync(InvocationContext invocationContext)
    {
        var paths = new UsageSurveyPaths(_context.FileSystem);
        IUsageSurveyUploader uploader = AppInsightsUploader.TryCreate(_context, out AppInsightsUploader ai)
            ? ai
            : new StubFileUploader(_context.FileSystem, paths, _context.Trace);
        var dispatcher = new UsageSurveyDispatcher(
            _context.FileSystem,
            paths,
            _context.Trace,
            uploader,
            foregroundOutput: Console.Out);

        _context.Streams.Out.WriteLine("Usage survey show: foreground dispatcher running. Press Ctrl-C to exit.");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        await dispatcher.RunAsync(cts.Token);
        _context.Streams.Out.WriteLine("Usage survey show: exited.");
    }

    private async Task DispatchAsync()
    {
        var paths = new UsageSurveyPaths(_context.FileSystem);
        IUsageSurveyUploader uploader = AppInsightsUploader.TryCreate(_context, out AppInsightsUploader ai)
            ? ai
            : new StubFileUploader(_context.FileSystem, paths, _context.Trace);
        var dispatcher = new UsageSurveyDispatcher(
            _context.FileSystem,
            paths,
            _context.Trace,
            uploader);

        await dispatcher.RunAsync(CancellationToken.None);
    }
}
