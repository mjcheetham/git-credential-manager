using System.Collections.Generic;

namespace GitCredentialManager;

/// <summary>
/// Represents a secure storage location for credentials and their metadata.
/// </summary>
/// <remarks>
/// Each credential is identified by a <see cref="CredentialKey"/>. Only <see cref="Find"/> matches credentials
/// for any account; every other operation addresses exactly one credential.
/// </remarks>
public interface ICredentialStore2
{
    /// <summary>
    /// Get the name of the credential store.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the maximum width of a secret that can be stored by this CredentialStore. When set allows credentials
    /// to be stored in chunks of at most the size configured.
    ///
    /// A value of LTE 0 (default of 0) indicates there is no limits set by the CredentialStore
    /// </summary>
    /// <remarks>
    /// The size represented here is the number of characters (<see langword="char"/>) of the string.
    /// </remarks>
    int MaxCredentialSize => 0;

    /// <summary>
    /// Get the credential stored with the given key.
    /// </summary>
    /// <param name="key">
    /// Key of the credential. A key without an account only matches a credential stored without an account.
    /// </param>
    /// <returns>The stored credential, or null if there is none.</returns>
    StoredCredential Get(CredentialKey key);

    /// <summary>
    /// Find the keys of credentials stored for a service.
    /// </summary>
    /// <remarks>
    /// Secrets are not returned, and stores avoid reading them where they can.
    /// </remarks>
    /// <param name="service">Name of the service to match.</param>
    /// <param name="account">Account to match, or null to match credentials for any account.</param>
    /// <returns>Keys of all matching credentials, each returned once.</returns>
    IReadOnlyList<CredentialKey> Find(string service, string account = null);

    /// <summary>
    /// Store a credential, replacing the secret and metadata of any credential stored with the same key.
    /// </summary>
    /// <param name="credential">Credential to store.</param>
    void Set(StoredCredential credential);

    /// <summary>
    /// Remove the credential stored with the given key.
    /// </summary>
    /// <param name="key">
    /// Key of the credential. A key without an account only matches a credential stored without an account.
    /// </param>
    /// <returns>True if a credential was removed, false otherwise.</returns>
    bool Remove(CredentialKey key);
}
