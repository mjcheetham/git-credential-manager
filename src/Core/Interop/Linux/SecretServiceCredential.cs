using System.Diagnostics;

namespace GitCredentialManager.Interop.Linux
{
    [DebuggerDisplay("{DebuggerDisplay}")]
    public class SecretServiceCredential : ICredential
    {
        internal SecretServiceCredential(string service, string account, string password, CredentialMetadata metadata = null)
        {
            Service = service;
            Account = account;
            Password = password;
            Metadata = metadata ?? CredentialMetadata.Empty;
        }

        public string Service { get; }

        public string Account { get; }

        public string Password { get; }

        public CredentialMetadata Metadata { get; }

        private string DebuggerDisplay => $"[Service: {Service}, Account: {Account}]";
    }
}
