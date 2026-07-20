namespace GitCredentialManager.Output;

/// <summary>
/// Formats supported for structured command output.
/// </summary>
public enum OutputFormat
{
    /// <summary>
    /// Render results as a human-readable table.
    /// </summary>
    Table = 0,

    /// <summary>
    /// Render results as an indented JSON array.
    /// </summary>
    Json,

    /// <summary>
    /// Render fields as Git-style LF-separated key/value pairs terminated by NUL.
    /// </summary>
    Nul,
}
