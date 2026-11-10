using System;
using Xunit;

namespace GitCredentialManager.Tests;

public abstract class CredentialStoreTests
{
    protected static void ReadWriteDelete(ICredentialStore store)
    {
        // Create a service that is guaranteed to be unique
        string service = $"https://example.com/{Guid.NewGuid():N}";
        const string account = "john.doe";
        const string password = "letmein123"; // [SuppressMessage("Microsoft.Security", "CS001:SecretInline", Justification="Fake credential")]
        DateTimeOffset expiry = DateTimeOffset.FromUnixTimeSeconds(1784986200); // 2026-07-25T13:30:00.000Z

        // Create metadata
        var metadata = new CredentialMetadata
        {
            ExpiresAt = expiry
        };

        try
        {
            // Write
            store.AddOrUpdate(service, account, password, metadata);

            // Read
            ICredential outCredential = store.Get(service, account);

            Assert.NotNull(outCredential);
            Assert.Equal(account, outCredential.Account);
            Assert.Equal(password, outCredential.Password);
            Assert.NotNull(outCredential.Metadata);
            Assert.Equal(expiry, outCredential.Metadata.ExpiresAt);
        }
        finally
        {
            // Ensure we clean up after ourselves even in case of 'get' failures
            store.Remove(service, account);
        }
    }

    protected static void MetadataLifecycle(ICredentialStore store)
    {
        // Create a service that is guaranteed to be unique
        string service = $"https://example.com/{Guid.NewGuid():N}";
        DateTimeOffset expiry = DateTimeOffset.FromUnixTimeSeconds(1784986200); // 2026-07-25T13:30:00.000Z

        var metadata = new CredentialMetadata
        {
            ExpiresAt = expiry
        };

        var otherMetadata = new CredentialMetadata
        {
            ExpiresAt = expiry.AddHours(1)
        };

        try
        {
            // Create with empty metadata
            store.AddOrUpdate(service, "alice", "original");
            Assert.Equal(CredentialMetadata.Empty, store.Get(service, "alice").Metadata);

            // Add metadata to existing credential
            store.AddOrUpdate(service, "alice", "original", metadata);
            ICredential credential = store.Get(service, "alice");
            Assert.Equal("original", credential.Password);
            Assert.Equal(metadata, credential.Metadata);

            // No-op credential update should not change metadata
            store.AddOrUpdate(service, "alice", "original", otherMetadata);
            store.AddOrUpdate(service, "alice", "original");
            Assert.Equal(otherMetadata, store.Get(service, "alice").Metadata);

            // Storing a new credential should not affect existing credentials
            store.AddOrUpdate(service, "bob", "another-secret", metadata);
            Assert.Equal(otherMetadata, store.Get(service, "alice").Metadata);
            Assert.Equal(metadata, store.Get(service, "bob").Metadata);

            // Updating credential without metadata should clear existing metadata
            store.AddOrUpdate(service, "alice", "replacement");
            Assert.Equal(CredentialMetadata.Empty, store.Get(service, "alice").Metadata);

            // Explicitly clearing metadata should work as expected
            store.AddOrUpdate(service, "alice", "replacement", metadata);
            store.AddOrUpdate(service, "alice", "replacement", CredentialMetadata.Empty);
            Assert.Equal(CredentialMetadata.Empty, store.Get(service, "alice").Metadata);

            // Removing a credential should not affect metadata on other credentials
            Assert.True(store.Remove(service, "alice"));
            Assert.Null(store.Get(service, "alice"));
            Assert.Equal(metadata, store.Get(service, "bob").Metadata);
        }
        finally
        {
            // Always clean up even if the test fails
            store.Remove(service, "alice");
            store.Remove(service, "bob");
        }
    }

    protected static void Get_NotFound_ReturnsNull(ICredentialStore store)
    {
        // Unique service; guaranteed not to exist!
        string service = $"https://example.com/{Guid.NewGuid():N}";

        ICredential credential = store.Get(service, account: null);
        Assert.Null(credential);
    }

    protected static void Remove_NotFound_ReturnsFalse(ICredentialStore store)
    {
        // Unique service; guaranteed not to exist!
        string service = $"https://example.com/{Guid.NewGuid():N}";

        bool result = store.Remove(service, account: null);
        Assert.False(result);
    }

    protected static void MaxCredentialSize(ICredentialStore store, int expected)
    {
        Assert.Equal(expected, store.MaxCredentialSize);
    }
}
