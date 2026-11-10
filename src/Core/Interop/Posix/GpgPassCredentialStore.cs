using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

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
                var metadata = CredentialMetadata.Empty;
                if (attrs.TryGetValue(ExpiresAtAttribute, out string value) &&
                    DateTimeOffsetEx.TryParseUnixTimeSeconds(value, out DateTimeOffset expiry))
                {
                    metadata = metadata with { ExpiresAt = expiry };
                }

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

            var sb = new StringBuilder(credential.Password);
            sb.AppendFormat("{2}{0}={1}{2}", ServiceAttribute, credential.Service, Environment.NewLine);
            sb.AppendFormat("{0}={1}{2}", AccountAttribute, credential.Account, Environment.NewLine);
            if (credential.Metadata.ExpiresAt is {} expiry)
            {
                string expiryStr = expiry.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
                sb.AppendFormat("{0}={1}{2}", ExpiresAtAttribute, expiryStr, Environment.NewLine);
            }

            string fileContents = sb.ToString();

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
