
namespace GitCredentialManager.Interop.Windows
{
    public class WindowsCredential : ICredential
    {
        public WindowsCredential(string service, string userName, string password, string targetName, CredentialMetadata metadata = null)
        {
            Service = service;
            UserName = userName;
            Password = password;
            TargetName = targetName;
            Metadata = metadata ?? CredentialMetadata.Empty;
        }

        public string Service { get; }

        public string UserName { get; }

        public string Password { get; }

        public string TargetName { get; }

        public CredentialMetadata Metadata { get; }

        string ICredential.Account => UserName;
    }
}
