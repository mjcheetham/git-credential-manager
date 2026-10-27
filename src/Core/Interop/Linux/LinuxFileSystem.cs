using System;
using System.IO;
using System.Runtime.Versioning;
using GitCredentialManager.Interop.Posix;

namespace GitCredentialManager.Interop.Linux
{
    [SupportedOSPlatform("linux")]
    public class LinuxFileSystem : PosixFileSystem
    {
        public override bool IsSamePath(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            {
                return false;
            }

            // Normalize paths
            a = Path.GetFullPath(a);
            b = Path.GetFullPath(b);

            // Resolve symbolic links
            a = ResolveSymbolicLinks(a);
            b = ResolveSymbolicLinks(b);

            // By default, assume paths ARE case-sensitive...
            var comparer = StringComparer.Ordinal;
            if (!IsCaseSensitivePath(a) && !IsCaseSensitivePath(b))
            {
                // ...but if both paths are on a case-sensitive file system,
                // then we need to use a case-insensitive comparison.
                comparer = StringComparer.OrdinalIgnoreCase;
            }

            return comparer.Equals(a, b);
        }
    }
}
