using System.Text;

namespace GitCredentialManager;

/// <summary>
/// A credential held in an <see cref="ICredentialStore"/>.
/// </summary>
/// <param name="Key">Service and account that identify the credential.</param>
/// <param name="Secret">Secret value.</param>
/// <param name="Metadata">Metadata stored with the credential, or null for none.</param>
public sealed record StoredCredential(CredentialKey Key, string Secret, CredentialMetadata Metadata = null)
{
    private readonly CredentialMetadata _metadata = Metadata ?? CredentialMetadata.Empty;

    /// <summary>
    /// Metadata stored with the credential. Credentials without metadata have <see cref="CredentialMetadata.Empty"/>.
    /// </summary>
    public CredentialMetadata Metadata
    {
        get => _metadata;
        init => _metadata = value ?? CredentialMetadata.Empty;
    }

    /// <summary>
    /// Create a credential to return to Git from the stored account, secret, and expiry.
    /// </summary>
    public GitCredential ToGitCredential() => new(Key.Account, Secret) { ExpiresAt = Metadata.ExpiresAt };

    // Never include the secret in the string representation
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append($"{nameof(Key)} = {Key}, {nameof(Metadata)} = {Metadata}");
        return true;
    }
}
