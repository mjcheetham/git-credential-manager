using System;
using System.IO;
using System.Text;
using GitCredentialManager.Tests.Objects;
using Xunit;

namespace GitCredentialManager.Tests
{
    public class PlaintextCredentialStoreTests : CredentialStoreTests
    {
        private const string TestNamespace = "git-test";
        private const string StoreRoot = "/tmp/test-store";
        private const string TestPassword = "letmein123"; // [SuppressMessage("Microsoft.Security", "CS001:SecretInline", Justification="Fake credential")]

        [Fact]
        public void PlaintextCredentialStore_ReadWriteDelete()
        {
            ReadWriteDelete(new PlaintextCredentialStore(new TestFileSystem(), StoreRoot, TestNamespace));
        }

        [Fact]
        public void PlaintextCredentialStore_AccountMatching()
        {
            AccountMatching(new PlaintextCredentialStore(new TestFileSystem(), StoreRoot, TestNamespace));
        }

        [Fact]
        public void PlaintextCredentialStore_MetadataLifecycle()
        {
            MetadataLifecycle(new PlaintextCredentialStore(new TestFileSystem(), StoreRoot, TestNamespace));
        }

        [Fact]
        public void PlaintextCredentialStore_Set_WritesCredentialFile()
        {
            var fs = new TestFileSystem();
            var store = new PlaintextCredentialStore(fs, StoreRoot, TestNamespace);

            string uniqueGuid = Guid.NewGuid().ToString("N");
            string service = $"https://example.com/{uniqueGuid}";
            const string userName = "john.doe";
            var metadata = new CredentialMetadata { ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(1784986200) };

            string expectedFilePath = Path.Combine(
                StoreRoot,
                TestNamespace,
                "https",
                "example.com",
                uniqueGuid,
                $"{userName}.credential");
            string expectedFileContents = TestPassword + Environment.NewLine +
                                          $"service={service}" + Environment.NewLine +
                                          $"account={userName}" + Environment.NewLine +
                                          "expires_at=1784986200" + Environment.NewLine;

            store.Set(new StoredCredential(new CredentialKey(service, userName), TestPassword, metadata));

            Assert.True(fs.Files.ContainsKey(expectedFilePath));
            Assert.Equal(expectedFileContents, Encoding.UTF8.GetString(fs.Files[expectedFilePath]));
        }

        [Theory]
        [InlineData("not-a-timestamp")]
        [InlineData("253402300800")]
        public void PlaintextCredentialStore_Get_InvalidExpiry_ReturnsCredentialWithoutMetadata(string expiry)
        {
            var fs = new TestFileSystem();
            var store = new PlaintextCredentialStore(fs, StoreRoot, TestNamespace);
            string path = Path.Combine(StoreRoot, TestNamespace, "https", "example.com", "john.doe.credential");
            fs.Directories.Add(Path.GetDirectoryName(path));
            fs.Files[path] = Encoding.UTF8.GetBytes(
                TestPassword + Environment.NewLine +
                "service=https://example.com" + Environment.NewLine +
                "account=john.doe" + Environment.NewLine +
                $"expires_at={expiry}" + Environment.NewLine);

            StoredCredential credential = store.Get(new CredentialKey("https://example.com", "john.doe"));

            Assert.NotNull(credential);
            Assert.Equal(TestPassword, credential.Secret);
            Assert.Equal(CredentialMetadata.Empty, credential.Metadata);
        }

        [Fact]
        public void PlaintextCredentialStore_MaxCredentialSize_ReturnsZero()
        {
            var fs = new TestFileSystem();
            ICredentialStore2 credentialStore = new PlaintextCredentialStore(fs, StoreRoot, TestNamespace);
            Assert.Equal(0, credentialStore.MaxCredentialSize);
        }
    }
}
