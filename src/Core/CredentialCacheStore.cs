using System;
using System.Collections.Generic;
using System.Globalization;

namespace GitCredentialManager
{
    public class CredentialCacheStore : ICredentialStore
    {
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

        public IList<string> GetAccounts(string service)
        {
            // Listing accounts is not supported by the credential-cache store so we just attempt to retrieve
            // the username from first credential for the given service and return an empty list if it fails.
            var input = MakeGitCredentialsEntry(service, null);

            var result = _git.InvokeHelperAsync(
                $"credential-cache get {_options}",
                input
            ).GetAwaiter().GetResult();

            if (result.TryGetValue("username", out string value))
            {
                return new List<string> { value };
            }

            return Array.Empty<string>();
        }

        public ICredential Get(string service, string account)
        {
            var input = MakeGitCredentialsEntry(service, account);

            var result = _git.InvokeHelperAsync(
                $"credential-cache get {_options}",
                input
            ).GetAwaiter().GetResult();

            if (result.TryGetValue("username", out string outUser) && result.TryGetValue("password", out string outPwd))
            {
                var metadata = CredentialMetadata.Empty;
                if (result.TryGetValue("password_expiry_utc", out string value) &&
                    DateTimeOffsetEx.TryParseUnixTimeSeconds(value, out DateTimeOffset expiry))
                {
                    metadata = metadata with { ExpiresAt = expiry };
                }

                return new GitCredential(outUser, outPwd, metadata);
            }

            return null;
        }

        public void AddOrUpdate(string service, string account, string secret, CredentialMetadata metadata = null)
        {
            if (metadata is null)
            {
                ICredential existing = Get(service, account);
                bool sameCredential = existing != null &&
                                      StringComparer.Ordinal.Equals(existing.Account, account) &&
                                      StringComparer.Ordinal.Equals(existing.Password, secret);
                metadata = sameCredential ? existing.Metadata : CredentialMetadata.Empty;
            }

            var input = MakeGitCredentialsEntry(service, account);
            input["password"] = secret;
            if (metadata.ExpiresAt is { } expiry)
            {
                input["password_expiry_utc"] = expiry.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            }

            // per https://git-scm.com/docs/gitcredentials :
            // For a store or erase operation, the helper’s output is ignored.
            _git.InvokeHelperAsync(
                $"credential-cache store {_options}",
                input
            ).GetAwaiter().GetResult();
        }

        public bool Remove(string service, string account)
        {
            var input = MakeGitCredentialsEntry(service, account);

            // per https://git-scm.com/docs/gitcredentials :
            // For a store or erase operation, the helper’s output is ignored.
            _git.InvokeHelperAsync(
                $"credential-cache erase {_options}",
                input
            ).GetAwaiter().GetResult();

            // the credential cache doesn't tell us whether anything was erased
            // but we're optimistic sorts
            return true;
        }

        #endregion

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
