using System;
using GitCredentialManager.Tests.Objects;
using Xunit;

namespace GitCredentialManager.Tests;

public class CredentialStoreExtensionsTests
{
    private const string Service = "https://example.com";
    private const string OldPassword = "letmein123-old"; // [SuppressMessage("Microsoft.Security", "CS001:SecretInline", Justification="Fake credential")]
    private const string NewPassword = "letmein123-new"; // [SuppressMessage("Microsoft.Security", "CS001:SecretInline", Justification="Fake credential")]

    private static readonly CredentialKey Key = new(Service, "john.doe");

    private static readonly CredentialMetadata Metadata = new()
    {
        ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(1784986200)
    };

    [Fact]
    public void CredentialStoreExtensions_FindFirst_NoAccount_ReturnsCredentialForAnyAccount()
    {
        ICredentialStore store = new TestCredentialStore();
        store.Set(new StoredCredential(Key, OldPassword, Metadata));

        StoredCredential credential = store.FindFirst(Service);

        Assert.NotNull(credential);
        Assert.Equal(Key, credential.Key);
        Assert.Equal(OldPassword, credential.Secret);
        Assert.Equal(Metadata, credential.Metadata);
    }

    [Fact]
    public void CredentialStoreExtensions_FindFirst_OtherAccount_ReturnsNull()
    {
        ICredentialStore store = new TestCredentialStore();
        store.Set(new StoredCredential(Key, OldPassword));

        Assert.Null(store.FindFirst(Service, "jane.doe"));
    }

    [Fact]
    public void CredentialStoreExtensions_SetPreservingMetadata_SameSecret_KeepsMetadata()
    {
        ICredentialStore store = new TestCredentialStore();
        store.Set(new StoredCredential(Key, OldPassword, Metadata));

        store.SetPreservingMetadata(Key, OldPassword);

        Assert.Equal(new StoredCredential(Key, OldPassword, Metadata), store.Get(Key));
    }

    [Fact]
    public void CredentialStoreExtensions_SetPreservingMetadata_DifferentSecret_ClearsMetadata()
    {
        ICredentialStore store = new TestCredentialStore();
        store.Set(new StoredCredential(Key, OldPassword, Metadata));

        store.SetPreservingMetadata(Key, NewPassword);

        Assert.Equal(new StoredCredential(Key, NewPassword), store.Get(Key));
    }
}
