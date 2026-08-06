using System;
using System.IO;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace GitCredentialManager.MSBuild
{
    public class GenerateLibGit2Info : Task
    {
        [Required]
        public string OutputFile { get; set; }

        [Required]
        public string BinaryName { get; set; }

        [Required]
        public string CommitHash { get; set; }

        public override bool Execute()
        {
            string contents =
                "namespace GitCredentialManager;" + Environment.NewLine +
                "public static class LibGit2Info" + Environment.NewLine +
                "{" + Environment.NewLine +
                $"    public const string FileName = \"{BinaryName}\";" + Environment.NewLine +
                $"    public const string CommitHash = \"{CommitHash}\";" + Environment.NewLine +
                "}" + Environment.NewLine;

            if (!File.Exists(OutputFile) || File.ReadAllText(OutputFile) != contents)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(OutputFile));
                File.WriteAllText(OutputFile, contents);
            }

            return true;
        }
    }
}
