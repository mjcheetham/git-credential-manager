using System;
using GitCredentialManager.Tests.Objects;
using Xunit;

namespace GitCredentialManager.Tests;

public class CredentialStoreExtensionsTests
{
    private const string Service = "https://example.com";
    private const string TestPassword = "letmein123"; // [SuppressMessage("Microsoft.Security", "CS001:SecretInline", Justification="Fake credential")]

    private static readonly CredentialKey Key = new(Service, "john.doe");

    private static readonly CredentialMetadata Metadata = new()
    {
        ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(1784986200)
    };

    [Fact]
    public void CredentialStoreExtensions_FindFirst_NoAccount_ReturnsCredentialForAnyAccount()
    {
        ICredentialStore store = new TestCredentialStore();
        store.Set(new StoredCredential(Key, TestPassword, Metadata));

        StoredCredential credential = store.FindFirst(Service);

        Assert.NotNull(credential);
        Assert.Equal(Key, credential.Key);
        Assert.Equal(TestPassword, credential.Secret);
        Assert.Equal(Metadata, credential.Metadata);
    }

    [Fact]
    public void CredentialStoreExtensions_FindFirst_OtherAccount_ReturnsNull()
    {
        ICredentialStore store = new TestCredentialStore();
        store.Set(new StoredCredential(Key, TestPassword));

        Assert.Null(store.FindFirst(Service, "jane.doe"));
    }
}
