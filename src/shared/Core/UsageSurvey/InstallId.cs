using System;
using System.IO;
using System.Text;

namespace GitCredentialManager.UsageSurvey;

/// <summary>
/// Reads, creates, and rotates the persistent random install identifier used to link
/// usage survey events from the same install. The id is a random GUID with no machine
/// fingerprinting; its only purpose is to deduplicate event streams from a single user
/// for aggregate counting.
/// </summary>
public class InstallId
{
    private readonly IFileSystem _fileSystem;
    private readonly UsageSurveyPaths _paths;
    private readonly ITrace _trace;

    public InstallId(IFileSystem fileSystem, UsageSurveyPaths paths, ITrace trace)
    {
        EnsureArgument.NotNull(fileSystem, nameof(fileSystem));
        EnsureArgument.NotNull(paths, nameof(paths));
        EnsureArgument.NotNull(trace, nameof(trace));

        _fileSystem = fileSystem;
        _paths = paths;
        _trace = trace;
    }

    /// <summary>
    /// Try to read the existing install id from disk. Returns null if no id has yet been created.
    /// </summary>
    public Guid? TryGet()
    {
        if (!_fileSystem.FileExists(_paths.InstallIdFile))
        {
            return null;
        }

        try
        {
            string raw = _fileSystem.ReadAllText(_paths.InstallIdFile).Trim();
            if (Guid.TryParse(raw, out Guid id))
            {
                return id;
            }

            _trace.WriteLine($"Usage survey install id file at '{_paths.InstallIdFile}' is malformed; will regenerate.");
            return null;
        }
        catch (IOException ex)
        {
            _trace.WriteLine($"Failed to read usage survey install id: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Get the existing install id from disk, or generate and persist a new one if none exists.
    /// </summary>
    public Guid GetOrCreate()
    {
        Guid? existing = TryGet();
        if (existing.HasValue)
        {
            return existing.Value;
        }

        return Reset();
    }

    /// <summary>
    /// Generate a new random install id and persist it, overwriting any existing id.
    /// </summary>
    public Guid Reset()
    {
        var id = Guid.NewGuid();
        Write(id);
        return id;
    }

    private void Write(Guid id)
    {
        _fileSystem.CreateDirectory(_paths.UsageSurveyDirectory);

        // Write atomically: write to a temp file then replace the target.
        string tmp = _paths.InstallIdFile + ".tmp";
        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(id.ToString("D") + "\n");
            using (Stream s = _fileSystem.OpenFileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                s.Write(bytes, 0, bytes.Length);
            }

            // Atomic-ish replace via MoveFile(overwrite). On POSIX this is a rename(2)
            // which is atomic when src and dst are on the same filesystem.
            _fileSystem.MoveFile(tmp, _paths.InstallIdFile, overwrite: true);

            TrySetOwnerOnlyPermissions(_paths.InstallIdFile);
        }
        finally
        {
            if (_fileSystem.FileExists(tmp))
            {
                try { _fileSystem.DeleteFile(tmp); } catch { /* best-effort */ }
            }
        }
    }

    private void TrySetOwnerOnlyPermissions(string path)
    {
#if !NETFRAMEWORK
        if (!PlatformUtils.IsPosix())
        {
            return;
        }

        try
        {
#pragma warning disable CA1416
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
#pragma warning restore CA1416
        }
        catch (Exception ex)
        {
            _trace.WriteLine($"Failed to tighten permissions on '{path}': {ex.Message}");
        }
#endif
    }
}
