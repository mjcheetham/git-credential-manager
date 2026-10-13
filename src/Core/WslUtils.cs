using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace GitCredentialManager
{
    public enum WindowsShell
    {
        Cmd,
        PowerShell,
    }

    public static class WslUtils
    {
        private const string WslUncPrefix = @"\\wsl$\";
        private const string WslLocalHostUncPrefix = @"\\wsl.localhost\";
        private const string WslCommandName = "wsl.exe";
        private const string WslInteropEnvar = "WSL_INTEROP";
        private const string WslConfFilePath = "/etc/wsl.conf";
        private const string DefaultWslMountPrefix = "/mnt";
        private const string DefaultWslSysDriveMountName = "c";

        internal const string WslViewShellHandlerName = "wslview";

        /// <summary>
        /// Cached Windows host session ID.
        /// </summary>
        /// <remarks>A value less than 0 represents "unknown".</remarks>
        private static int _windowsSessionId = -1;

        /// <summary>
        /// Cached WSL version.
        /// </summary>
        /// <remarks>A value of 0 represents "not WSL", and a value less than 0 represents "unknown".</remarks>
        private static int _wslVersion = -1;

        /// <summary>
        /// Cached Windows system drive mount path.
        /// </summary>
        private static string _sysDriveMountPath = null;

        /// <summary>
        /// Cached WSL drive mount root path.
        /// </summary>
        private static string _driveMountRoot = null;

        public static bool IsWslDistribution(IEnvironment env, IFileSystem fs, out int wslVersion)
        {
            if (_wslVersion < 0)
            {
                _wslVersion = GetWslVersion(env, fs);
            }

            wslVersion = _wslVersion;
            return _wslVersion > 0;
        }

        private static int GetWslVersion(IEnvironment env, IFileSystem fs)
        {
            // All WSL distributions are Linux.. obviously!
            if (!PlatformUtils.IsLinux())
            {
                return 0;
            }

            // The WSL_INTEROP variable is set in WSL2 distributions
            if (env.Variables.TryGetValue(WslInteropEnvar, out _))
            {
                return 2;
            }

            const string procVersionPath = "/proc/version";
            if (fs.FileExists(procVersionPath))
            {
                // Both WSL1 and WSL2 distributions include "[Mm]icrosoft" in the version string
                string procVersion = fs.ReadAllText(procVersionPath);
                if (!Regex.IsMatch(procVersion, "[Mm]icrosoft"))
                {
                    return 0;
                }

                // WSL2 distributions return "WSL2" in the version string
                if (Regex.IsMatch(procVersion, "wsl2", RegexOptions.IgnoreCase))
                {
                    return 2;
                }

                return 1;
            }

            return 0;
        }

        /// <summary>
        /// Test if a file path points to a location in a Windows Subsystem for Linux distribution.
        /// </summary>
        /// <param name="path">Path to test.</param>
        /// <returns>True if <paramref name="path"/> is a WSL path, false otherwise.</returns>
        public static bool IsWslPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            return (path.StartsWith(WslUncPrefix, StringComparison.OrdinalIgnoreCase) &&
                    path.Length > WslUncPrefix.Length) ||
                   (path.StartsWith(WslLocalHostUncPrefix, StringComparison.OrdinalIgnoreCase) &&
                    path.Length > WslLocalHostUncPrefix.Length);
        }

        /// <summary>
        /// Create a command to be executed in a Windows Subsystem for Linux distribution.
        /// </summary>
        /// <param name="distribution">WSL distribution name.</param>
        /// <param name="command">Command to execute.</param>
        /// <param name="workingDirectory">Optional working directory.</param>
        /// <param name="class">Process class for tracing purposes.</param>
        /// <returns><see cref="ChildProcess"/> object ready to start.</returns>
        public static ChildProcess CreateWslProcess(string distribution,
            string command,
            string workingDirectory = null,
            Trace2ProcessClass @class = Trace2ProcessClass.None)
        {
            var args = new StringBuilder();
            args.AppendFormat("--distribution {0} ", distribution);
            args.AppendFormat("--exec {0}", command);

            string wslExePath = GetWslPath();

            var psi = new ProcessStartInfo(wslExePath, args.ToString())
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = false, // Do not redirect stderr as tracing might be enabled
                UseShellExecute = false,
                WorkingDirectory = workingDirectory ?? string.Empty
            };

            return new ChildProcess(psi, @class);
        }

        /// <summary>
        /// Create a command to be executed in a shell in the host Windows operating system.
        /// </summary>
        /// <param name="fs">File system.</param>
        /// <param name="shell">Shell used to execute the command in Windows.</param>
        /// <param name="command">Command to execute.</param>
        /// <param name="workingDirectory">Optional working directory.</param>
        /// <returns><see cref="Process"/> object ready to start.</returns>
        public static Process CreateWindowsShellProcess(IFileSystem fs,
            WindowsShell shell, string command, string workingDirectory = null)
        {
            string sysDrive = GetSystemDriveMountPath(fs);

            string launcher;
            var args = new StringBuilder();

            switch (shell)
            {
                case WindowsShell.Cmd:
                    launcher = Path.Combine(sysDrive, "Windows/cmd.exe");
                    args.AppendFormat("/C {0}", command);
                    break;

                case WindowsShell.PowerShell:
                    const string psStreamSetup =
                        "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; " +
                        "[Console]::InputEncoding = [System.Text.Encoding]::UTF8; ";

                    launcher = Path.Combine(sysDrive, "Windows/System32/WindowsPowerShell/v1.0/powershell.exe");
                    args.Append(" -NoProfile -NonInteractive -ExecutionPolicy Bypass");
                    args.AppendFormat(" -Command \"{0} {1}\"", psStreamSetup, command);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(shell));
            }

            var psi = new ProcessStartInfo(launcher, args.ToString())
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = workingDirectory ?? string.Empty
            };

            return new Process { StartInfo = psi };
        }

        /// <summary>
        /// Get the host Windows session ID.
        /// </summary>
        /// <returns>Windows session ID, or a negative value if it is not known.</returns>
        public static int GetWindowsSessionId(IFileSystem fs)
        {
            if (_windowsSessionId < 0)
            {
                const string script = @"(Get-Process -ID $PID).SessionId";
                using (Process proc = CreateWindowsShellProcess(fs, WindowsShell.PowerShell, script))
                {
                    try
                    {
                        proc.Start();
                        proc.WaitForExit();
                    }
                    catch
                    {
                        // Unable to start the process, return unknown session ID
                        return -1;
                    }

                    if (proc.ExitCode == 0)
                    {
                        string output = proc.StandardOutput.ReadToEnd().Trim();
                        if (int.TryParse(output, out int sessionId))
                        {
                            _windowsSessionId = sessionId;
                        }
                    }
                }
            }

            return _windowsSessionId;
        }

        private static string GetDriveMountRoot(IFileSystem fs)
        {
            if (_driveMountRoot is null)
            {
                _driveMountRoot = DefaultWslMountPrefix;

                // If the wsl.conf file exists in this distribution the user may
                // have changed the Windows volume mount point prefix. Use it!
                if (fs.FileExists(WslConfFilePath))
                {
                    // Read wsl.conf for [automount] root = <path>
                    IniFile wslConf = IniSerializer.Deserialize(fs, WslConfFilePath);
                    if (wslConf.TryGetSection("automount", out IniSection automountSection) &&
                        automountSection.TryGetProperty("root", out string value))
                    {
                        _driveMountRoot = value;
                    }
                }
            }

            return _driveMountRoot;
        }

        private static string GetSystemDriveMountPath(IFileSystem fs)
        {
            if (_sysDriveMountPath is null)
            {
                string mountPrefix = GetDriveMountRoot(fs);

                // Try to locate the system volume by looking for the Windows\System32 directory
                IEnumerable<string> mountPoints = fs.EnumerateDirectories(mountPrefix);
                foreach (string mountPoint in mountPoints)
                {
                    string sys32Path = Path.Combine(mountPoint, "Windows", "System32");

                    if (fs.DirectoryExists(sys32Path))
                    {
                        _sysDriveMountPath = mountPoint;
                        return _sysDriveMountPath;
                    }
                }

                _sysDriveMountPath = Path.Combine(mountPrefix, DefaultWslSysDriveMountName);
            }

            return _sysDriveMountPath;
        }

        public static string ConvertToDistroPath(string path, out string distribution)
        {
            if (!IsWslPath(path)) throw new ArgumentException("Must provide a WSL path", nameof(path));

            int distroStart;
            if (path.StartsWith(WslUncPrefix, StringComparison.OrdinalIgnoreCase))
            {
                distroStart = WslUncPrefix.Length;
            }
            else if (path.StartsWith(WslLocalHostUncPrefix, StringComparison.OrdinalIgnoreCase))
            {
                distroStart = WslLocalHostUncPrefix.Length;
            }
            else
            {
                throw new Exception("Invalid WSL path prefix");
            }

            int distroEnd = path.IndexOf('\\', distroStart);

            if (distroEnd < 0) distroEnd = path.Length;

            distribution = path.Substring(distroStart, distroEnd - distroStart);

            if (path.Length > distroEnd)
            {
                return path.Substring(distroEnd).Replace('\\', '/');
            }

            return "/";
        }

        public static string ConvertToUnixPath(IFileSystem fs, string path)
        {
            EnsureArgument.NotNullOrWhiteSpace(path, nameof(path));

            if (!Path.IsPathRooted(path))
            {
                throw new ArgumentException("Must be an absolute path", nameof(path));
            }

            string root = Path.GetPathRoot(path);
            if (root == "/") // we have been given a unix path
            {
                throw new ArgumentException("Must be a Windows path", nameof(path));
            }

            if (root is not [var driveChar, ':', '\\'])
            {
                throw new ArgumentException($"Invalid Windows path root '{root}'", nameof(path));
            }

            // Drive letter must be lowercase for WSL mount points
            string driveLetter = driveChar.ToString().ToLowerInvariant();

            // Convert the Windows path to a Unix path
            string unixPath = path.Substring(root.Length).Replace('\\', '/');

            string mountPrefix = GetDriveMountRoot(fs);
            return $"{mountPrefix}/{driveLetter}/{unixPath}";
        }

        internal /*for testing purposes*/ static string GetWslPath()
        {
            // WSL is only supported on 64-bit operating systems
            if (!Environment.Is64BitOperatingSystem)
            {
                throw new Exception("WSL is not supported on 32-bit operating systems");
            }

            //
            // When running as a 32-bit application on a 64-bit operating system, we cannot access the real
            // C:\Windows\System32 directory because the OS will redirect us transparently to the
            // C:\Windows\SysWOW64 directory (containing 32-bit executables).
            //
            // In order to access the real 64-bit System32 directory, we must access via the pseudo directory
            // C:\Windows\SysNative that does **not** experience any redirection for 32-bit applications.
            //
            // HOWEVER, if we're running as a 64-bit application on a 64-bit operating system, the SysNative
            // directory does not exist! This means if running as a 64-bit application on a 64-bit OS we must
            // use the System32 directory name directly.
            //
            var sysDir = Environment.ExpandEnvironmentVariables(
                Environment.Is64BitProcess
                    ? @"%WINDIR%\System32"
                    : @"%WINDIR%\SysNative"
            );

            return Path.Combine(sysDir, WslCommandName);
        }

        public static IReadOnlyList<string> GetWslDistributions()
        {
            // Can only run this from Windows
            PlatformUtils.EnsureWindows();

            string wslExePath = GetWslPath();

            if (!File.Exists(wslExePath))
            {
                // WSL is not installed on this system
                return [];
            }

            var psi = new ProcessStartInfo(wslExePath, "--list --quiet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = new ChildProcess(psi);
            proc.Start();
            proc.WaitForExit();

            if (proc.ExitCode != 0)
            {
                throw new Exception($"Failed to list WSL distributions: {proc.StandardError.ReadToEnd()}");
            }

            var distributions = new List<string>();
            while (proc.StandardOutput.ReadLine() is { } line)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    distributions.Add(line.Trim());
                }
            }

            return distributions;
        }

        public static bool DistributionExists(string distribution)
        {
            IReadOnlyList<string> distros = GetWslDistributions();

            // Distro names are not case-sensitive
            return distros.Contains(distribution, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Attempt to locate an executable file in a WSL distribution.
        /// </summary>
        /// <param name="distribution">The name of the WSL distribution.</param>
        /// <param name="executableName">The name of the executable to locate.</param>
        /// <param name="path">The full path to the executable if found.</param>
        /// <returns>True if the executable was found; otherwise, false.</returns>
        public static bool TryLocateExecutable(string distribution, string executableName, out string path)
        {
            EnsureArgument.NotNullOrWhiteSpace(distribution, nameof(distribution));
            EnsureArgument.NotNullOrWhiteSpace(executableName, nameof(executableName));

            // Can only run this on Windows
            PlatformUtils.EnsureWindows();

            path = null;

            using ChildProcess which = CreateWslProcess(distribution, $"which {executableName}");
            which.Start();
            which.WaitForExit();

            if (which.ExitCode == 0)
            {
                path = which.StandardOutput.ReadToEnd().Trim();
                return !string.IsNullOrWhiteSpace(path);
            }

            // Not found
            return false;
        }
    }
}
