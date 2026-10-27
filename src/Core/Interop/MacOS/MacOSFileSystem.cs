using System;
using System.IO;
using System.Runtime.Versioning;
using GitCredentialManager.Interop.Posix;

namespace GitCredentialManager.Interop.MacOS
{
    [SupportedOSPlatform("macos")]
    public class MacOSFileSystem : PosixFileSystem
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

            // By default, HFS+/APFS is NOT case-sensitive...
            var comparer = StringComparer.OrdinalIgnoreCase;
            if (IsCaseSensitivePath(a) || IsCaseSensitivePath(b))
            {
                // ...but if either path is on a case-sensitive file system,
                // then we need to use a case-sensitive comparison.
                comparer = StringComparer.Ordinal;
            }

            return comparer.Equals(a, b);
        }
    }
}
