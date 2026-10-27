using System;
using System.IO;
using System.Runtime.Versioning;
using GitCredentialManager.Interop.Windows.Native;

namespace GitCredentialManager.Interop.Windows
{
    [SupportedOSPlatform("windows")]
    public class WindowsFileSystem : FileSystem
    {
        public override bool IsSamePath(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            {
                return false;
            }

            a = Path.GetFullPath(a);
            b = Path.GetFullPath(b);

            // Note: we do not resolve or handle symlinks on Windows
            // because they require administrator permissions to even create!

            // Just always assume that Windows paths are case-insensitive,
            // even though they can be configured to be case-sensitive (on a per-directory basis!).
            return StringComparer.OrdinalIgnoreCase.Equals(a, b);
        }

        public override bool IsCaseSensitivePath(string path)
        {
            if (!Directory.Exists(path))
            {
                // Get the file directory if the path is a file, otherwise use the directory itself
                path = Path.GetDirectoryName(path);
            }

            return Kernel32.IsDirectoryCaseSensitive(path);
        }
    }
}
