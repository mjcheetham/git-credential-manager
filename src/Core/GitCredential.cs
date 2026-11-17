using System.Text;

namespace GitCredentialManager
{
    /// <summary>
    /// A credential (username/password pair) that Git can use to authenticate to a remote repository.
    /// </summary>
    /// <param name="Account">Account associated with this credential.</param>
    /// <param name="Password">Password or token.</param>
    public sealed record GitCredential(string Account, string Password)
    {
        // Never include the password in the string representation
        private bool PrintMembers(StringBuilder builder)
        {
            builder.Append($"{nameof(Account)} = {Account}");
            return true;
        }
    }
}
