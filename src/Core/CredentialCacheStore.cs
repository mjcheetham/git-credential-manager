using System;
using System.Collections.Generic;
using System.Globalization;

namespace GitCredentialManager
{
    public class CredentialCacheStore : ICredentialStore2
    {
        private const string PasswordExpiryKey = "password_expiry_utc";

        readonly IGit _git;
        readonly string _options;

        public CredentialCacheStore(IGit git, string options)
        {
            _git = git;
            if (string.IsNullOrEmpty(options))
            {
                _options = string.Empty;
            }
            else
            {
                _options = options;
            }
        }

        #region ICredentialStore

        public string Name => "Git Credential Cache";

        public StoredCredential Get(CredentialKey key)
        {
            IDictionary<string, string> result = Query(key.Service, key.Account);
            if (!result.TryGetValue("password", out string password))
            {
                return null;
            }

            // A query without a username matches an entry for any account,
            // so check the entry is for exactly the account we asked for.
            result.TryGetValue("username", out string username);
            if (new CredentialKey(key.Service, username) != key)
            {
                return null;
            }

            var metadata = CredentialMetadata.Empty;
            if (result.TryGetValue(PasswordExpiryKey, out string value) &&
                DateTimeOffsetEx.TryParseUnixTimeSeconds(value, out DateTimeOffset expiry))
            {
                metadata = metadata with { ExpiresAt = expiry };
            }

            return new StoredCredential(key, password, metadata);
        }

        public IReadOnlyList<CredentialKey> Find(string service, string account = null)
        {
            EnsureArgument.NotNullOrWhiteSpace(service, nameof(service));

            // Listing entries is not supported by the credential-cache, so the best we can do
            // is to return the account of the first entry that matches.
            IDictionary<string, string> result = Query(service, account);
            if (result.ContainsKey("password"))
            {
                result.TryGetValue("username", out string username);
                return new[] { new CredentialKey(service, username) };
            }

            return Array.Empty<CredentialKey>();
        }

        public void Set(StoredCredential credential)
        {
            EnsureArgument.NotNull(credential, nameof(credential));

            var input = MakeGitCredentialsEntry(credential.Key.Service, credential.Key.Account);
            input["password"] = credential.Secret;

            // The cache replaces the whole entry, so omitting the expiry clears any existing one
            if (credential.Metadata.ExpiresAt is { } expiry)
            {
                input[PasswordExpiryKey] = expiry.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            }

            // per https://git-scm.com/docs/gitcredentials :
            // For a store or erase operation, the helper’s output is ignored.
            _git.InvokeHelperAsync(
                $"credential-cache store {_options}",
                input
            ).GetAwaiter().GetResult();
        }

        public bool Remove(CredentialKey key)
        {
            // The credential-cache doesn't tell us whether anything was erased, so check first
            if (Get(key) is null)
            {
                return false;
            }

            // Note that erasing without a username also erases entries for other accounts
            var input = MakeGitCredentialsEntry(key.Service, key.Account);

            // per https://git-scm.com/docs/gitcredentials :
            // For a store or erase operation, the helper’s output is ignored.
            _git.InvokeHelperAsync(
                $"credential-cache erase {_options}",
                input
            ).GetAwaiter().GetResult();

            return true;
        }

        #endregion

        private IDictionary<string, string> Query(string service, string account)
        {
            var input = MakeGitCredentialsEntry(service, account);

            return _git.InvokeHelperAsync(
                $"credential-cache get {_options}",
                input
            ).GetAwaiter().GetResult();
        }

        private Dictionary<string, string> MakeGitCredentialsEntry(string service, string account)
        {
            var result = new Dictionary<string, string>();

            result["url"] = service;
            if (!string.IsNullOrEmpty(account))
            {
                result["username"] = account;
            }

            return result;
        }
    }
}
