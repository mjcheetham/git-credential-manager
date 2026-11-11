using System;
using System.Collections.Generic;
using System.IO;

namespace GitCredentialManager.Interop.Posix
{
    public class GpgPassCredentialStore : PlaintextCredentialStore
    {
        public const string PasswordStoreDirEnvar = "PASSWORD_STORE_DIR";

        private readonly IGpg _gpg;

        public GpgPassCredentialStore(IFileSystem fileSystem, IGpg gpg, string storeRoot, string @namespace = null)
            : base(fileSystem, storeRoot, @namespace)
        {
            PlatformUtils.EnsurePosix();
            EnsureArgument.NotNull(gpg, nameof(gpg));
            _gpg = gpg;
        }

        public override string Name => "GPG/Pass";

        protected override string CredentialFileExtension => ".gpg";

        private string GetGpgId(string credentialFullPath)
        {
            // Walk up from the credential's directory to the store root, looking for a .gpg-id file.
            // This mimics the behaviour of GNU Pass, which uses the nearest .gpg-id in the directory hierarchy.
            string dir = Path.GetDirectoryName(credentialFullPath);
            while (dir != null)
            {
                string gpgIdPath = Path.Combine(dir, ".gpg-id");
                if (FileSystem.FileExists(gpgIdPath))
                {
                    using (var stream = FileSystem.OpenFileStream(gpgIdPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var reader = new StreamReader(stream))
                    {
                        return reader.ReadLine();
                    }
                }

                // Stop after checking the store root
                if (FileSystem.IsSamePath(dir, StoreRoot))
                {
                    break;
                }

                dir = Path.GetDirectoryName(dir);
            }

            throw new Exception($"Cannot find GPG ID in password store at '{StoreRoot}'; run `pass init <gpg-id>` to initialize the store.");
        }

        protected override bool TryDeserializeCredential(string path, out FileCredential credential)
        {
            string text = _gpg.DecryptFile(path);

            int line1Idx = text.IndexOf(Environment.NewLine, StringComparison.OrdinalIgnoreCase);
            if (line1Idx > 0)
            {
                // Password is the first line
                string password = text.Substring(0, line1Idx);

                // All subsequent lines are metadata/attributes
                string attrText = text.Substring(line1Idx + Environment.NewLine.Length);
                using var attrReader = new StringReader(attrText);
                IDictionary<string, string> attrs = attrReader.ReadDictionary(StringComparer.OrdinalIgnoreCase);

                // Account is optional
                attrs.TryGetValue(AccountAttribute, out string account);

                // Metadata is optional
                CredentialMetadata metadata = ReadMetadata(attrs);

                // Service is required
                if (attrs.TryGetValue(ServiceAttribute, out string service))
                {
                    credential = new FileCredential(path, service, account, password, metadata);
                    return true;
                }
            }

            credential = null;
            return false;
        }

        protected override void SerializeCredential(FileCredential credential)
        {
            string gpgId = GetGpgId(credential.FullPath);

            using var writer = new StringWriter();
            writer.WriteLine(credential.Password);
            WriteAttributes(writer, credential);
            string fileContents = writer.ToString();

            // Ensure the parent directory exists
            string parentDir = Path.GetDirectoryName(credential.FullPath);
            if (!FileSystem.DirectoryExists(parentDir))
            {
                FileSystem.CreateDirectory(parentDir);
            }

            // Delete any existing file
            if (FileSystem.FileExists(credential.FullPath))
            {
                FileSystem.DeleteFile(credential.FullPath);
            }

            // Encrypt!
            _gpg.EncryptFile(credential.FullPath, gpgId, fileContents);
        }
    }
}
