using System;

namespace GitCredentialManager;

public static class CredentialStoreExtensions
{
    /// <summary>
    /// Get the first credential stored for a service and, optionally, an account.
    /// </summary>
    /// <param name="store">Credential store.</param>
    /// <param name="service">Name of the service to match.</param>
    /// <param name="account">Account to match, or null to match a credential for any account.</param>
    /// <returns>First matching credential, or null if there is none.</returns>
    public static StoredCredential FindFirst(this ICredentialStore2 store, string service, string account = null)
    {
        foreach (CredentialKey key in store.Find(service, account))
        {
            StoredCredential credential = store.Get(key);
            if (credential is not null)
            {
                return credential;
            }
        }

        return null;
    }

    /// <summary>
    /// Store a credential, keeping any metadata already stored with it if the secret is unchanged.
    /// </summary>
    /// <remarks>
    /// Git asks for a credential to be stored after each successful use, passing back only what it was given.
    /// GCM does not return metadata to Git, so storing on Git's behalf with <see cref="ICredentialStore2.Set"/>
    /// alone would discard metadata, such as an expiry, every time a stored credential is used.
    /// </remarks>
    /// <param name="store">Credential store.</param>
    /// <param name="key">Key of the credential.</param>
    /// <param name="secret">Secret value to store.</param>
    public static void SetPreservingMetadata(this ICredentialStore2 store, CredentialKey key, string secret)
    {
        StoredCredential existing = store.Get(key);
        CredentialMetadata metadata = StringComparer.Ordinal.Equals(existing?.Secret, secret)
            ? existing.Metadata
            : null;

        store.Set(new StoredCredential(key, secret, metadata));
    }
}
