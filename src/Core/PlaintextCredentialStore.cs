using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace GitCredentialManager
{
    public class PlaintextCredentialStore : ICredentialStore2
    {
        protected const string ServiceAttribute = "service";
        protected const string AccountAttribute = "account";
        protected const string ExpiresAtAttribute = "expires_at";

        public PlaintextCredentialStore(IFileSystem fileSystem, string storeRoot, string @namespace = null)
        {
            EnsureArgument.NotNull(fileSystem, nameof(fileSystem));
            EnsureArgument.NotNullOrWhiteSpace(storeRoot, nameof(storeRoot));

            FileSystem = fileSystem;
            StoreRoot = storeRoot;
            Namespace = @namespace;
        }

        protected IFileSystem FileSystem { get; }
        protected string StoreRoot { get; }
        protected string Namespace { get; }
        protected virtual string CredentialFileExtension => ".credential";

        public virtual string Name => "Plaintext";

        public StoredCredential Get(CredentialKey key)
        {
            return Enumerate(key.Service, key.Account, anyAccount: false).FirstOrDefault()?.ToStoredCredential();
        }

        public IReadOnlyList<CredentialKey> Find(string service, string account = null)
        {
            EnsureArgument.NotNullOrWhiteSpace(service, nameof(service));

            return Enumerate(service, account, anyAccount: string.IsNullOrWhiteSpace(account))
                .Select(x => x.Key)
                .Distinct()
                .ToList();
        }

        public void Set(StoredCredential credential)
        {
            EnsureArgument.NotNull(credential, nameof(credential));

            // Ensure the store root exists and permissions are set
            EnsureStoreRoot();

            CredentialKey key = credential.Key;
            FileCredential existingCredential = Enumerate(key.Service, key.Account, anyAccount: false).FirstOrDefault();

            // No need to update existing credential if nothing has changed
            if (existingCredential != null &&
                StringComparer.Ordinal.Equals(key.Account, existingCredential.Key.Account) &&
                StringComparer.Ordinal.Equals(credential.Secret, existingCredential.Password) &&
                credential.Metadata == existingCredential.Metadata)
            {
                return;
            }

            string serviceSlug = CreateServiceSlug(key.Service);
            string servicePath = Path.Combine(StoreRoot, serviceSlug);

            if (!FileSystem.DirectoryExists(servicePath))
            {
                FileSystem.CreateDirectory(servicePath);
            }

            string fullPath = Path.Combine(servicePath, $"{key.Account}{CredentialFileExtension}");
            var fileCredential = new FileCredential(fullPath, key.Service, key.Account, credential.Secret, credential.Metadata);
            SerializeCredential(fileCredential);
        }

        public bool Remove(CredentialKey key)
        {
            foreach (FileCredential credential in Enumerate(key.Service, key.Account, anyAccount: false))
            {
                // Only delete the first match
                FileSystem.DeleteFile(credential.FullPath);
                return true;
            }

            return false;
        }

        protected virtual bool TryDeserializeCredential(string path, out FileCredential credential)
        {
            string text;
            using (var stream = FileSystem.OpenFileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new StreamReader(stream))
            {
                text = reader.ReadToEnd();
            }

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

        protected virtual void SerializeCredential(FileCredential credential)
        {
            // Ensure the parent directory exists
            string parentDir = Path.GetDirectoryName(credential.FullPath);
            if (!FileSystem.DirectoryExists(parentDir))
            {
                FileSystem.CreateDirectory(parentDir);
            }

            using (var stream = FileSystem.OpenFileStream(credential.FullPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.WriteLine(credential.Password);
                WriteAttributes(writer, credential);
                writer.Flush();
            }
        }

        /// <summary>
        /// Write the service, account, and metadata attributes of a credential as `key=value` lines.
        /// </summary>
        protected static void WriteAttributes(TextWriter writer, FileCredential credential)
        {
            writer.WriteLine("{0}={1}", ServiceAttribute, credential.Service);
            writer.WriteLine("{0}={1}", AccountAttribute, credential.Account);
            if (credential.Metadata.ExpiresAt is { } expiry)
            {
                string value = expiry.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
                writer.WriteLine("{0}={1}", ExpiresAtAttribute, value);
            }
        }

        /// <summary>
        /// Read credential metadata from a set of attributes. Unrecognised or invalid values are ignored.
        /// </summary>
        protected static CredentialMetadata ReadMetadata(IDictionary<string, string> attrs)
        {
            var metadata = CredentialMetadata.Empty;
            if (attrs.TryGetValue(ExpiresAtAttribute, out string value) &&
                DateTimeOffsetEx.TryParseUnixTimeSeconds(value, out DateTimeOffset expiry))
            {
                metadata = metadata with { ExpiresAt = expiry };
            }

            return metadata;
        }

        private IEnumerable<FileCredential> Enumerate(string service, string account, bool anyAccount)
        {
            string serviceSlug = CreateServiceSlug(service);
            string searchPath = Path.Combine(StoreRoot, serviceSlug);

            // Credentials without an account are stored with an empty account name
            account ??= string.Empty;

            if (!FileSystem.DirectoryExists(searchPath))
            {
                yield break;
            }

            IEnumerable<string> allFiles = FileSystem.EnumerateFiles(searchPath, $"*{CredentialFileExtension}");

            foreach (string fullPath in allFiles)
            {
                string accountFile = Path.GetFileNameWithoutExtension(fullPath);
                if (anyAccount || StringComparer.OrdinalIgnoreCase.Equals(account, accountFile))
                {
                    // Validate the credential metadata also matches our search
                    if (TryDeserializeCredential(fullPath, out FileCredential credential) &&
                        StringComparer.OrdinalIgnoreCase.Equals(service, credential.Service) &&
                        (anyAccount || StringComparer.OrdinalIgnoreCase.Equals(account, credential.Account ?? string.Empty)))
                    {
                        yield return credential;
                    }
                }
            }
        }

        /// <summary>
        /// Ensure the store root directory exists. If it does not, create a new directory with
        /// permissions that only permit the owner to read/write/execute. Permissions on an existing
        /// directory are not modified.
        /// </summary>
        private void EnsureStoreRoot()
        {
            if (FileSystem.DirectoryExists(StoreRoot))
            {
                // Don't touch the permissions on the existing directory
                return;
            }

            FileSystem.CreateDirectory(StoreRoot);

            // We only set file system permissions on POSIX platforms
            if (!PlatformUtils.IsPosix())
            {
                return;
            }

            // Set store root permissions such that only the owner can read/write/execute
            var mode = Interop.Posix.Native.NativeFileMode.S_IRUSR |
                       Interop.Posix.Native.NativeFileMode.S_IWUSR |
                       Interop.Posix.Native.NativeFileMode.S_IXUSR;

            // Ignore the return code.. this is a best effort only
            Interop.Posix.Native.Stat.chmod(StoreRoot, mode);
        }

        private string CreateServiceSlug(string service)
        {
            var sb = new StringBuilder();
            char sep = Path.DirectorySeparatorChar;

            if (!string.IsNullOrWhiteSpace(Namespace))
            {
                sb.AppendFormat("{0}{1}", Namespace, sep);
            }

            if (Uri.TryCreate(service, UriKind.Absolute, out Uri serviceUri))
            {
                sb.AppendFormat("{0}{1}", serviceUri.Scheme, sep);
                sb.AppendFormat("{0}", serviceUri.Host);

                if (!serviceUri.IsDefaultPort)
                {
                    sb.Append(PlatformUtils.IsWindows() ? '-' : ':');
                    sb.Append(serviceUri.Port);
                }

                sb.Append(serviceUri.AbsolutePath.Replace('/', sep));
            }
            else
            {
                sb.Append(service);
            }

            return sb.ToString();
        }
    }
}
