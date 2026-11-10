
namespace GitCredentialManager
{
    /// <summary>
    /// Represents a credential.
    /// </summary>
    public interface ICredential
    {
        /// <summary>
        /// Account associated with this credential.
        /// </summary>
        string Account { get; }

        /// <summary>
        /// Password.
        /// </summary>
        string Password { get; }

        /// <summary>
        /// Optional metadata. Its presence does not change credential retrieval.
        /// </summary>
        CredentialMetadata Metadata => CredentialMetadata.Empty;
    }

    /// <summary>
    /// Represents a credential (username/password pair) that Git can use to authenticate to a remote repository.
    /// </summary>
    public class GitCredential : ICredential
    {
        public GitCredential(string userName, string password, CredentialMetadata metadata = null)
        {
            Account = userName;
            Password = password;
            Metadata = metadata ?? CredentialMetadata.Empty;
        }

        public string Account { get; }

        public string Password { get; }

        public CredentialMetadata Metadata { get; }
    }
}
