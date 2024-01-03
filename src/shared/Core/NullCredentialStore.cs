using System;
using System.Collections.Generic;

namespace GitCredentialManager;

public class NullCredentialStore : ICredentialStore
{
    public IList<string> GetAccounts(string service) => Array.Empty<string>();

    public ICredential Get(string service, string account) => null;

    public void AddOrUpdate(string service, string account, string secret) { }

    public bool Remove(string service, string account) => false;
}
