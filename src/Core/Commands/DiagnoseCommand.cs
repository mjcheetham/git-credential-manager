using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GitCredentialManager.Diagnostics;

namespace GitCredentialManager.Commands
{
    public class DiagnoseCommand : Command
    {
        private const string TestOutputIndent = "    ";

        private readonly ICommandContext _context;
        private readonly List<IDiagnostic> _diagnostics = new();

        public DiagnoseCommand(ICommandContext context)
            : base("diagnose", "Run diagnostics and gather logs to diagnose problems with Git Credential Manager")
        {
            EnsureArgument.NotNull(context, nameof(context));

            _context = context;

            var output = new Option<string>(new[] { "--output", "-o" }, "Output directory for diagnostic logs.");
            AddOption(output);

            this.SetHandler(ExecuteAsync, output);
        }

        public void AddStandardDiagnostics()
        {
            _diagnostics.Add(new EnvironmentDiagnostic(_context));
            _diagnostics.Add(new FileSystemDiagnostic(_context));
            _diagnostics.Add(new NetworkingDiagnostic(_context));
            _diagnostics.Add(new GitDiagnostic(_context));
            _diagnostics.Add(new CredentialStoreDiagnostic(_context));
            _diagnostics.Add(new EntraAuthenticationDiagnostic(_context));
        }

        public void AddDiagnostic(IDiagnostic diagnostic)
        {
            _diagnostics.Add(diagnostic);
        }

        public void AddDiagnostics(IEnumerable<IDiagnostic> diagnostics)
        {
            _diagnostics.AddRange(diagnostics);
        }

        private async Task<int> ExecuteAsync(string output)
        {
            // Don't use IStandardStreams for writing output in this command as we
            // cannot trust any component on the ICommandContext is working correctly.
            Console.WriteLine($"Running diagnostics...{Environment.NewLine}");

            if (_diagnostics.Count == 0)
            {
                Console.WriteLine("No diagnostics to run.");
                return 0;
            }

            string currentDir = Directory.GetCurrentDirectory();
            string outputDir;
            if (string.IsNullOrWhiteSpace(output))
            {
                outputDir = currentDir;
            }
            else
            {
                if (!Directory.Exists(output))
                {
                    Directory.CreateDirectory(output);
                }

                outputDir = Path.GetFullPath(Path.Combine(currentDir, output));
            }

            string logFilePath = Path.Combine(outputDir, "gcm-diagnose.log");
            var results = new List<DiagnosticResult>();

            using var fullLog = new StreamWriter(logFilePath, append: false, Encoding.UTF8);
            WriteLogHeader(fullLog);

            foreach (IDiagnostic diagnostic in _diagnostics)
            {
                DiagnosticResult result = await RunDiagnosticAsync(diagnostic, fullLog);
                results.Add(result);
            }

            IReadOnlyList<string> additionalFiles = CopyFiles(outputDir, results.SelectMany(x => x.AdditionalFiles));

            Console.WriteLine();
            PrintSummary(logFilePath, results, additionalFiles);

            fullLog.Close();
            return results.Any(x => x.Outcome == DiagnosticOutcome.Error) ? 1 : 0;
        }

        private void PrintSummary(string logFilePath, IReadOnlyList<DiagnosticResult> results, IReadOnlyList<string> additionalFiles)
        {
            int numPassed = results.Count(x => x.Outcome == DiagnosticOutcome.Success);
            int numFailed = results.Count(x => x.Outcome == DiagnosticOutcome.Error);
            int numWarned = results.Count(x => x.Outcome == DiagnosticOutcome.Warning);
            int numSkipped = results.Count(x => x.Outcome == DiagnosticOutcome.Skipped);

            string summary = $"Diagnostic summary: {numPassed} passed, {numSkipped} skipped, {numWarned} warned, {numFailed} failed.";
            Console.WriteLine(summary);
            Console.WriteLine("Log files:");
            Console.WriteLine($"  {logFilePath}");
            foreach (string filePath in additionalFiles)
            {
                Console.WriteLine($"  {filePath}");
            }
            Console.WriteLine();
            Console.WriteLine("Caution: Log files may include sensitive information - redact before sharing.");
            Console.WriteLine();

            if (numFailed + numWarned > 0)
            {
                Console.WriteLine("Diagnostics indicate a possible problem with your installation.");
                Console.WriteLine($"Please open an issue at {Constants.HelpUrls.GcmNewIssue} and include log files.");
                Console.WriteLine();
            }
        }

        private void WriteLogHeader(StreamWriter fullLog)
        {
            fullLog.WriteLine("Diagnose log at {0:s}Z", DateTime.UtcNow);
            fullLog.WriteLine();
            fullLog.WriteLine($"AppPath: {_context.ApplicationPath}");
            fullLog.WriteLine($"InstallDir: {_context.InstallationDirectory}");
            fullLog.WriteLine(
                AssemblyUtils.TryGetAssemblyVersion(out string version)
                    ? $"Version: {version}"
                    : "Version: [!] Failed to get version information [!]"
            );
            fullLog.WriteLine();
        }

        private async Task<DiagnosticResult> RunDiagnosticAsync(IDiagnostic diagnostic, StreamWriter fullLog)
        {
            fullLog.WriteLine("------------");
            fullLog.WriteLine($"Diagnostic: {diagnostic.Name}");

            if (!diagnostic.CanRun(out string skipReason))
            {
                fullLog.Write("Outcome: Skipped");
                if (!string.IsNullOrWhiteSpace(skipReason))
                {
                    fullLog.Write($" ({skipReason})");
                }
                fullLog.WriteLine();

                Console.Write(" ");
                ConsoleEx.WriteColor("[SKIP]", ConsoleColor.DarkGray);
                Console.WriteLine(" {0}", diagnostic.Name);

                return DiagnosticResult.Skipped(skipReason);
            }

            string inProgressMsg = $"  >>>>  {diagnostic.Name}";
            Console.Write(inProgressMsg);

            DiagnosticResult result = await diagnostic.RunAsync();
            fullLog.WriteLine("Outcome: {0}", result.Outcome);

            WriteException(fullLog, result.Exception);

            fullLog.WriteLine("Log:");
            foreach (var report in result.Reports)
            {
                fullLog.WriteLine(report.Message);
            }

            Console.Write(new string('\b', inProgressMsg.Length - 1));
            switch (result.Outcome)
            {
                case DiagnosticOutcome.Success:
                    ConsoleEx.WriteColor("[ OK ]", ConsoleColor.DarkGreen);
                    break;
                case DiagnosticOutcome.Warning:
                    ConsoleEx.WriteColor("[WARN]", ConsoleColor.DarkYellow);
                    break;
                case DiagnosticOutcome.Error:
                    ConsoleEx.WriteColor("[FAIL]", ConsoleColor.Red );
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            Console.WriteLine(" {0}", diagnostic.Name);

            if (result.Outcome != DiagnosticOutcome.Success)
            {
                if (result.Exception is not null)
                {
                    Console.WriteLine();
                    ConsoleEx.WriteLineIndent("[!] Encountered an exception [!]");
                    ConsoleEx.WriteLineIndent(result.Exception.ToString());
                }

                Console.WriteLine();
                ConsoleEx.WriteLineIndent("[*] Diagnostic test log [*]");
                ConsoleEx.WriteLineIndent(result.Reports.Select(x => x.ToLogString()));

                Console.WriteLine();
            }

            fullLog.Flush();
            return result;
        }

        private static IReadOnlyList<string> CopyFiles(string outputDir, IEnumerable<string> additionalFiles)
        {
            var extraLogs = new List<string>();
            foreach (string filePath in additionalFiles)
            {
                string fileName = Path.GetFileName(filePath);
                string destPath = Path.Combine(outputDir, fileName);
                try
                {
                    File.Copy(filePath, destPath, overwrite: true);
                }
                catch
                {
                    ConsoleEx.WriteLineIndent($"Failed to copy additional file '{filePath}'");
                }

                extraLogs.Add(destPath);
            }

            return extraLogs;
        }

        private void WriteException(StreamWriter log, Exception exception)
        {
            if (exception is null)
            {
                return;
            }

            if (exception is AggregateException aex)
            {
                log.WriteLine("Exception: AggregateException");
                log.WriteLine("InnerExceptions (flattened):");
                foreach (var inner in aex.Flatten().InnerExceptions)
                {
                    log.WriteLine(inner.ToString());
                }
            }
            else
            {
                log.WriteLine("Exception: {0}", exception);
            }
        }

        private static class ConsoleEx
        {
            public static void WriteLineIndent(string str)
            {
                string[] lines = str?.Split('\n', '\r');
                WriteLineIndent(lines);
            }

            public static void WriteLineIndent(IEnumerable<string> lines)
            {
                if (lines is null) return;

                foreach (string line in lines)
                {
                    Console.Write(TestOutputIndent);
                    Console.WriteLine(line);
                }
            }

            public static  void WriteColor(string str, ConsoleColor fgColor)
            {
                var initFgColor = Console.ForegroundColor;
                Console.ForegroundColor = fgColor;
                Console.Write(str);
                Console.ForegroundColor = initFgColor;
            }
        }
    }
}
