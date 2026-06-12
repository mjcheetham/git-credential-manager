using System.IO;

namespace GitCredentialManager.UsageSurvey;

/// <summary>
/// Central path helpers for the usage survey pipeline.
/// All paths are rooted under the user's GCM data directory
/// (<c>~/.gcm/</c> on POSIX, <c>%USERPROFILE%\.gcm\</c> on Windows).
/// </summary>
public class UsageSurveyPaths
{
    private readonly IFileSystem _fileSystem;

    public UsageSurveyPaths(IFileSystem fileSystem)
    {
        EnsureArgument.NotNull(fileSystem, nameof(fileSystem));
        _fileSystem = fileSystem;
    }

    /// <summary>
    /// <c>~/.gcm/usage-survey/</c>
    /// </summary>
    public string UsageSurveyDirectory =>
        Path.Combine(_fileSystem.UserDataDirectoryPath, Constants.UsageSurvey.DirectoryName);

    /// <summary>
    /// <c>~/.gcm/usage-survey/events/</c>
    /// </summary>
    public string EventsDirectory =>
        Path.Combine(UsageSurveyDirectory, Constants.UsageSurvey.EventsDirectoryName);

    /// <summary>
    /// <c>~/.gcm/usage-survey/sent/</c> — archive of events the dispatcher successfully
    /// shipped. Retained for <see cref="Constants.UsageSurvey.SentRetention"/> so users
    /// can inspect what was sent. Auto-purged by the dispatcher on each pass.
    /// </summary>
    public string SentDirectory =>
        Path.Combine(UsageSurveyDirectory, Constants.UsageSurvey.SentDirectoryName);

    /// <summary>
    /// <c>~/.gcm/usage-survey/install-id</c>
    /// </summary>
    public string InstallIdFile =>
        Path.Combine(UsageSurveyDirectory, Constants.UsageSurvey.InstallIdFileName);

    /// <summary>
    /// <c>~/.gcm/usage-survey/dispatcher.pid</c>
    /// </summary>
    public string DispatcherPidFile =>
        Path.Combine(UsageSurveyDirectory, Constants.UsageSurvey.DispatcherPidFileName);

    /// <summary>
    /// <c>~/.gcm/usage-survey/dispatcher.log</c>
    /// </summary>
    public string DispatcherLogFile =>
        Path.Combine(UsageSurveyDirectory, Constants.UsageSurvey.DispatcherLogFileName);
}
