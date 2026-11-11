using System;
using System.Collections.Generic;

namespace GitCredentialManager;

/// <summary>
/// Credential store that does nothing. This is useful when you want to disable internal credential storage
/// and only use another helper configured in Git to store credentials.
/// </summary>
public class NullCredentialStore : ICredentialStore
{
    public string Name => "No-op";

    public StoredCredential Get(CredentialKey key) => null;

    public IReadOnlyList<CredentialKey> Find(string service, string account = null) => Array.Empty<CredentialKey>();

    public void Set(StoredCredential credential) { }

    public bool Remove(CredentialKey key) => false;
}
