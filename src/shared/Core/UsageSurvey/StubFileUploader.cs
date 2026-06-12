using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GitCredentialManager.UsageSurvey;

/// <summary>
/// Default v1 uploader: writes shipped events to a local log file instead of sending
/// them anywhere. Used as a stub until a real HTTPS endpoint is in place.
/// Also used as the local sink when usage survey is being inspected without a configured
/// network endpoint.
/// </summary>
public class StubFileUploader : IUsageSurveyUploader
{
    private readonly IFileSystem _fileSystem;
    private readonly UsageSurveyPaths _paths;
    private readonly ITrace _trace;
    private readonly object _writeLock = new();

    public StubFileUploader(IFileSystem fileSystem, UsageSurveyPaths paths, ITrace trace)
    {
        EnsureArgument.NotNull(fileSystem, nameof(fileSystem));
        EnsureArgument.NotNull(paths, nameof(paths));
        EnsureArgument.NotNull(trace, nameof(trace));

        _fileSystem = fileSystem;
        _paths = paths;
        _trace = trace;
    }

    public Task<bool> UploadAsync(string jsonLine, CancellationToken ct)
    {
        try
        {
            _fileSystem.CreateDirectory(_paths.UsageSurveyDirectory);

            lock (_writeLock)
            {
                using Stream s = _fileSystem.OpenFileStream(
                    _paths.DispatcherLogFile,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.Read);

                byte[] bytes = Encoding.UTF8.GetBytes(jsonLine.TrimEnd('\r', '\n') + "\n");
                s.Write(bytes, 0, bytes.Length);
            }

            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _trace.WriteLine($"StubFileUploader failed to append event: {ex.Message}");
            return Task.FromResult(false);
        }
    }
}
