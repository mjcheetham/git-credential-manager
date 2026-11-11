using System.Collections.Generic;
using System.Linq;

namespace GitCredentialManager.Tests.Objects
{
    public class TestCredentialStore : ICredentialStore2
    {
        private readonly IDictionary<(string service, string account), TestCredential> _store;

        public TestCredentialStore()
        {
            _store = new Dictionary<(string,string), TestCredential>();
        }

        #region ICredentialStore

        public string Name => "Test Credential Store";
        
        public int MaxCredentialSize { get; set; }

        StoredCredential ICredentialStore2.Get(CredentialKey key)
        {
            return _store.TryGetValue((key.Service, key.Account), out TestCredential credential)
                ? new StoredCredential(key, credential.Password, credential.Metadata)
                : null;
        }

        IReadOnlyList<CredentialKey> ICredentialStore2.Find(string service, string account)
        {
            return Query(service, account).Select(x => new CredentialKey(x.Key.service, x.Key.account)).Distinct().ToList();
        }

        void ICredentialStore2.Set(StoredCredential credential)
        {
            Add(credential.Key.Service, credential.Key.Account, credential.Secret, credential.Metadata);
        }

        bool ICredentialStore2.Remove(CredentialKey key)
        {
            return _store.Remove((key.Service, key.Account));
        }

        #endregion

        public int Count => _store.Count;

        /// <summary>
        /// Get the first credential stored for a service and, if not null or empty, an account.
        /// </summary>
        public bool TryGet(string service, string account, out TestCredential credential)
        {
            credential = Query(service, account).Select(x => x.Value).FirstOrDefault();
            return credential != null;
        }

        public void Add(string service, TestCredential credential)
        {
            _store[(service, NormalizeAccount(credential.Account))] = credential;
        }

        public TestCredential Add(string service, string account, string secret, CredentialMetadata metadata = null)
        {
            var credential = new TestCredential(service, account, secret, metadata);
            _store[(service, NormalizeAccount(account))] = credential;
            return credential;
        }

        public bool Contains(string service, string account)
        {
            return TryGet(service, account, out _);
        }

        // Match CredentialKey, which treats an empty account as no account
        private static string NormalizeAccount(string account) => string.IsNullOrWhiteSpace(account) ? null : account;

        private IEnumerable<KeyValuePair<(string service, string account), TestCredential>> Query(string service, string account)
        {
            if (string.IsNullOrWhiteSpace(account))
            {
                // Find the all credentials matching service
                foreach (var kvp in _store)
                {
                    if (kvp.Key.service == service)
                    {
                        yield return kvp;
                    }
                }
            }
            else if (_store.TryGetValue((service, account), out var credential))
            {
                // Find the specific credential matching both service and credential
                yield return new KeyValuePair<(string, string), TestCredential>((service, account), credential);
            }
        }
    }

    public class TestCredential : ICredential
    {
        public TestCredential(string service, string account, string password, CredentialMetadata metadata = null)
        {
            Service = service;
            Account = account;
            Password = password;
            Metadata = metadata ?? CredentialMetadata.Empty;
        }

        public string Service { get; }

        public string Account { get; }

        public string Password { get; }

        public CredentialMetadata Metadata { get; }
    }
}
