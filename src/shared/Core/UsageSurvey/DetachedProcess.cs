using System;
using System.Diagnostics;

namespace GitCredentialManager.UsageSurvey;

/// <summary>
/// Spawns a child process fully detached from the parent's stdio so it can outlive
/// the parent without holding any pipes open. Used by the usage survey producer to start
/// the background dispatcher.
/// </summary>
/// <remarks>
/// v1 implementation uses <see cref="Process.Start"/> with all stdio redirected and
/// no shell. This is sufficient on all three platforms for short-lived parents like
/// <c>git credential fill</c>. A future improvement could use <c>setsid()</c> on
/// POSIX and <c>DETACHED_PROCESS|CREATE_NEW_PROCESS_GROUP</c> on Windows via P/Invoke
/// for full session detachment, but is not required for the dispatcher's purposes.
/// </remarks>
public static class DetachedProcess
{
    /// <summary>
    /// Start the given executable with the given arguments as a fully orphaned child.
    /// Stdio is closed; the child does not inherit the parent's handles. The returned
    /// process object is not waited on by the caller.
    /// </summary>
    public static int? Start(string path, string arguments)
    {
        EnsureArgument.NotNullOrWhiteSpace(path, nameof(path));

        var psi = new ProcessStartInfo(path, arguments ?? string.Empty)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        try
        {
            Process p = Process.Start(psi);
            if (p is null)
            {
                return null;
            }

            // Close inherited stdio handles so the child has no pipe back to us.
            try { p.StandardInput.Close(); } catch { /* ignore */ }
            try { p.StandardOutput.Close(); } catch { /* ignore */ }
            try { p.StandardError.Close(); } catch { /* ignore */ }

            return p.Id;
        }
        catch (Exception)
        {
            // The producer must never throw because of usage survey; caller handles failure.
            return null;
        }
    }
}
