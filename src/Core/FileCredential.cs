namespace GitCredentialManager
{
    /// <summary>
    /// A credential stored in a file by a <see cref="PlaintextCredentialStore"/> or a store derived from it.
    /// </summary>
    public class FileCredential
    {
        public FileCredential(string fullPath, string service, string account, string password,
            CredentialMetadata metadata = null)
        {
            FullPath = fullPath;
            Service = service;
            Account = account;
            Password = password;
            Metadata = metadata ?? CredentialMetadata.Empty;
        }

        public string FullPath { get; }

        public string Service { get; }

        public string Account { get; }

        public string Password { get; }

        public CredentialMetadata Metadata { get; }

        public CredentialKey Key => new(Service, Account);

        public StoredCredential ToStoredCredential() => new(Key, Password, Metadata);
    }
}
