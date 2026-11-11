using System;
using System.Collections.Generic;
using Xunit;

namespace GitCredentialManager.Tests;

/// <summary>
/// Behaviour shared by all <see cref="ICredentialStore"/> implementations.
/// </summary>
public abstract class CredentialStoreTests
{
    private static readonly CredentialMetadata Metadata = new()
    {
        ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(1784986200)
    };

    private static readonly CredentialMetadata OtherMetadata = new()
    {
        ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(1784989800)
    };

    // Unique values avoid collisions with other tests and leftovers in real OS stores
    private static string CreateService() => $"https://example.com/{Guid.NewGuid():N}";
    private static string CreateSecret() => Guid.NewGuid().ToString("N");

    protected static void ReadWriteDelete(ICredentialStore store)
    {
        var key = new CredentialKey(CreateService(), "john.doe");
        string secret = CreateSecret();

        try
        {
            Assert.Null(store.Get(key));
            Assert.False(store.Remove(key));

            store.Set(new StoredCredential(key, secret));

            StoredCredential credential = store.Get(key);
            Assert.NotNull(credential);
            Assert.Equal(key, credential.Key);
            Assert.Equal(secret, credential.Secret);
            Assert.Equal(CredentialMetadata.Empty, credential.Metadata);

            Assert.True(store.Remove(key));
            Assert.Null(store.Get(key));
            Assert.False(store.Remove(key));
        }
        finally
        {
            // Ensure we clean up after ourselves even in case of failures
            store.Remove(key);
        }
    }

    protected static void AccountMatching(ICredentialStore store)
    {
        string service = CreateService();
        var alice = new CredentialKey(service, "alice");
        var bob = new CredentialKey(service, "bob");
        var noAccount = new CredentialKey(service, null);

        string otherService = CreateService();
        var otherNoAccount = new CredentialKey(otherService, null);
        var otherAlice = new CredentialKey(otherService, "alice");

        string aliceSecret = CreateSecret();
        string noAccountSecret = CreateSecret();

        try
        {
            store.Set(new StoredCredential(alice, aliceSecret));
            store.Set(new StoredCredential(bob, CreateSecret()));
            store.Set(new StoredCredential(otherNoAccount, noAccountSecret));

            // Find without an account matches every account for the service
            IReadOnlyList<CredentialKey> keys = store.Find(service);
            Assert.Equal(2, keys.Count);
            Assert.Contains(alice, keys);
            Assert.Contains(bob, keys);
            Assert.Equal(new[] { alice }, store.Find(service, "alice"));
            Assert.Equal(new[] { otherNoAccount }, store.Find(otherService));
            Assert.Empty(store.Find(otherService, "alice"));

            // Keys without an account only match credentials stored without an account
            Assert.Null(store.Get(noAccount));
            Assert.False(store.Remove(noAccount));
            Assert.Equal(noAccountSecret, store.Get(otherNoAccount).Secret);
            Assert.Null(store.Get(otherAlice));
            Assert.False(store.Remove(otherAlice));

            // Removing a credential leaves other accounts alone
            Assert.True(store.Remove(alice));
            Assert.Null(store.Get(alice));
            Assert.Equal(new[] { bob }, store.Find(service));
        }
        finally
        {
            store.Remove(alice);
            store.Remove(bob);
            store.Remove(otherNoAccount);
        }
    }

    protected static void MetadataLifecycle(ICredentialStore store)
    {
        string service = CreateService();
        var alice = new CredentialKey(service, "alice");
        var bob = new CredentialKey(service, "bob");
        string secret1 = CreateSecret();
        string secret2 = CreateSecret();
        string secret3 = CreateSecret();

        try
        {
            // Credentials stored without metadata have empty metadata
            store.Set(new StoredCredential(alice, secret1));
            Assert.Equal(CredentialMetadata.Empty, store.Get(alice).Metadata);

            // Metadata can be added to an existing credential
            store.Set(new StoredCredential(alice, secret1, Metadata));
            StoredCredential credential = store.Get(alice);
            Assert.Equal(secret1, credential.Secret);
            Assert.Equal(Metadata, credential.Metadata);

            // Metadata is replaced even if the secret is unchanged
            store.Set(new StoredCredential(alice, secret1, OtherMetadata));
            Assert.Equal(OtherMetadata, store.Get(alice).Metadata);

            // Storing without metadata clears existing metadata
            store.Set(new StoredCredential(alice, secret1));
            Assert.Equal(CredentialMetadata.Empty, store.Get(alice).Metadata);

            // Metadata is stored per account
            store.Set(new StoredCredential(alice, secret2, Metadata));
            store.Set(new StoredCredential(bob, secret3, OtherMetadata));
            Assert.Equal(Metadata, store.Get(alice).Metadata);
            Assert.Equal(OtherMetadata, store.Get(bob).Metadata);

            // Removing a credential does not affect metadata of other credentials
            Assert.True(store.Remove(alice));
            Assert.Equal(OtherMetadata, store.Get(bob).Metadata);
        }
        finally
        {
            store.Remove(alice);
            store.Remove(bob);
        }
    }
}
