using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace GitCredentialManager.UsageSurvey;

/// <summary>
/// Helpers for the dispatcher pidfile. The dispatcher writes its pid into
/// <c>~/.gcm/usage-survey/dispatcher.pid</c> on startup so that at most one dispatcher
/// runs at a time. The file is the single source of truth: a stale file with a
/// non-existent pid is treated as no-dispatcher.
/// </summary>
public static class DispatcherPidFile
{
    /// <summary>
    /// Try to read the pid currently recorded in the pidfile, returning null when the
    /// file is missing, malformed, or references a process that is not alive.
    /// </summary>
    public static int? TryReadActivePid(IFileSystem fileSystem, UsageSurveyPaths paths, ITrace trace)
    {
        if (!fileSystem.FileExists(paths.DispatcherPidFile))
        {
            return null;
        }

        try
        {
            string raw = fileSystem.ReadAllText(paths.DispatcherPidFile).Trim();
            if (!int.TryParse(raw, out int pid) || pid <= 0)
            {
                return null;
            }

            int self;
            try { self = Process.GetCurrentProcess().Id; } catch { self = -1; }
            if (pid == self)
            {
                return pid;
            }

            try
            {
                using Process p = Process.GetProcessById(pid);
                return p != null && !p.HasExited ? pid : (int?)null;
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
        catch (Exception ex)
        {
            trace.WriteLine($"Usage survey: pidfile check failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Returns true if the pidfile exists and references a process that is currently
    /// alive (including this process). Used by the producer to decide whether to spawn
    /// a dispatcher, and by the dispatcher itself to detect take-over.
    /// </summary>
    public static bool IsActive(IFileSystem fileSystem, UsageSurveyPaths paths, ITrace trace)
        => TryReadActivePid(fileSystem, paths, trace).HasValue;

    /// <summary>
    /// Attempt to write our pid into the pidfile, overwriting any stale entry.
    /// Should only be called after <see cref="IsActive"/> returned false.
    /// </summary>
    public static bool TryAcquire(IFileSystem fileSystem, UsageSurveyPaths paths, ITrace trace)
    {
        try
        {
            fileSystem.CreateDirectory(paths.UsageSurveyDirectory);

            int pid = Process.GetCurrentProcess().Id;
            byte[] bytes = Encoding.UTF8.GetBytes(pid.ToString() + "\n");

            using Stream s = fileSystem.OpenFileStream(
                paths.DispatcherPidFile,
                FileMode.Create,
                FileAccess.Write,
                FileShare.Read);
            s.Write(bytes, 0, bytes.Length);
            return true;
        }
        catch (Exception ex)
        {
            trace.WriteLine($"Usage survey: failed to acquire pidfile: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Remove the pidfile if it still belongs to us.
    /// </summary>
    public static void Release(IFileSystem fileSystem, UsageSurveyPaths paths, ITrace trace)
    {
        try
        {
            if (!fileSystem.FileExists(paths.DispatcherPidFile))
            {
                return;
            }

            string raw = fileSystem.ReadAllText(paths.DispatcherPidFile).Trim();
            if (int.TryParse(raw, out int pid) && pid == Process.GetCurrentProcess().Id)
            {
                fileSystem.DeleteFile(paths.DispatcherPidFile);
            }
        }
        catch (Exception ex)
        {
            trace.WriteLine($"Usage survey: failed to release pidfile: {ex.Message}");
        }
    }
}
