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
    public static StoredCredential FindFirst(this ICredentialStore store, string service, string account = null)
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
}
