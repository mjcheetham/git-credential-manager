namespace GitCredentialManager;

/// <summary>
/// Identifies a credential in an <see cref="ICredentialStore"/>.
/// </summary>
public readonly record struct CredentialKey
{
    /// <param name="service">Name of the service the credential is for.</param>
    /// <param name="account">Account the credential is for, or null for a credential without an account.</param>
    public CredentialKey(string service, string account)
    {
        EnsureArgument.NotNullOrWhiteSpace(service, nameof(service));

        Service = service;
        // Stores do not distinguish between a missing and an empty account
        Account = string.IsNullOrWhiteSpace(account) ? null : account;
    }

    /// <summary>
    /// Name of the service the credential is for.
    /// </summary>
    public string Service { get; }

    /// <summary>
    /// Account the credential is for, or null if the credential has no account.
    /// </summary>
    public string Account { get; }
}
