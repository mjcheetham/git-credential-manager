using System.IO;
using System;

namespace GitCredentialManager;

public class Trace2FileWriter : Trace2Writer
{
    private readonly string _path;
    private readonly IFileSystem _fs;

    private Stream _stream;

    public Trace2FileWriter(IFileSystem fs, Trace2FormatTarget formatTarget, string path) : base(formatTarget)
    {
        _fs = fs;
        _path = path;
    }

    public override void Write(Trace2Message message)
    {
        try
        {
            _stream ??= _fs.OpenFileStream(_path, FileMode.Append, FileAccess.ReadWrite, FileShare.ReadWrite);
            byte[] data = EncodingEx.UTF8NoBom.GetBytes(Format(message));
            _stream.Write(data, 0, data.Length);
        }
        catch (DirectoryNotFoundException)
        {
            // Do nothing, as this either means we don't have the
            // parent directories above the file, or this trace2
            // target points to a directory.
        }
        catch (UnauthorizedAccessException)
        {
            // Do nothing, as this either means the file is not
            // accessible with current permissions, or we are on
            // Windows and the file is currently open for writing
            // by another process (likely Git itself.)
        }
        catch (IOException)
        {
            // Do nothing, as this likely means that the file is currently
            // open by another process (on Windows).
        }
    }
}
