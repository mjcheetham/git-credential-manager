using System;
using System.Globalization;
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

        [Fact]
        public void PlaintextCredentialStore_MetadataLifecycle()
        {
            MetadataLifecycle(new PlaintextCredentialStore(new TestFileSystem(), StoreRoot, TestNamespace));
        }

        [Theory]
        [InlineData("")]
        [InlineData("not-a-timestamp")]
        [InlineData("999999999999999999999999")]
        [InlineData("-62135596801")]
        [InlineData("253402300800")]
        public void PlaintextCredentialStore_Get_InvalidExpiryPreservesCredential(string expiry)
        {
            var fs = new TestFileSystem();
            var store = new PlaintextCredentialStore(fs, StoreRoot, TestNamespace);
            const string service = "https://example.com";
            store.AddOrUpdate(service, "alice", "password");
            string path = Path.Combine(StoreRoot, TestNamespace, "https", "example.com", "alice.credential");
            string contents = $"password{Environment.NewLine}service={service}{Environment.NewLine}" +
                              $"account=alice{Environment.NewLine}expiresAt={expiry}{Environment.NewLine}";
            fs.Files[path] = Encoding.UTF8.GetBytes(contents);

            ICredential credential = store.Get(service, "alice");

            Assert.Equal("password", credential.Password);
            Assert.Equal(CredentialMetadata.Empty, credential.Metadata);
        }

        [Fact]
        public void PlaintextCredentialStore_AddOrUpdate_WritesUnixSeconds()
        {
            var fs = new TestFileSystem();
            var store = new PlaintextCredentialStore(fs, StoreRoot, TestNamespace);
            DateTimeOffset expiry = DateTimeOffset.FromUnixTimeSeconds(1784986200); // 2026-07-25T13:30:00.000Z
            var metadata = new CredentialMetadata
            {
                ExpiresAt = expiry
            };

            store.AddOrUpdate("https://example.com", "alice", "password", metadata);

            string path = Path.Combine(StoreRoot, TestNamespace, "https", "example.com", "alice.credential");
            string contents = Encoding.UTF8.GetString(fs.Files[path]);
            Assert.Contains($"expires_at=1784986200{Environment.NewLine}", contents);
        }

        [Fact]
        public void PlaintextCredentialStore_ReadWriteDelete()
        {
            var fs = new TestFileSystem();
            var store = new PlaintextCredentialStore(fs, StoreRoot, TestNamespace);

            // Create a service that is guaranteed to be unique
            string uniqueGuid = Guid.NewGuid().ToString("N");
            string service = $"https://example.com/{uniqueGuid}";
            const string userName = "john.doe";
            const string password = "letmein123"; // [SuppressMessage("Microsoft.Security", "CS001:SecretInline", Justification="Fake credential")]
            const string expiryUnix = "1784986200"; // 2026-07-25T13:30:00.000Z

            // Create metadata
            DateTimeOffset expiry = DateTimeOffset.FromUnixTimeSeconds(long.Parse(expiryUnix));
            var metadata = new CredentialMetadata
            {
                ExpiresAt = expiry
            };

            string expectedSlug = Path.Combine(
                TestNamespace,
                "https",
                "example.com",
                uniqueGuid,
                $"{userName}.credential");
            string expectedFilePath = Path.Combine(StoreRoot, expectedSlug);
            string expectedFileContents = password + Environment.NewLine +
                                          $"service={service}" + Environment.NewLine +
                                          $"account={userName}" + Environment.NewLine +
                                          $"expires_at={expiry.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}" + Environment.NewLine;
            byte[] expectedFileBytes = Encoding.UTF8.GetBytes(expectedFileContents);

            try
            {
                // Write
                store.AddOrUpdate(service, userName, password, metadata);

                // Read
                ICredential outCredential = store.Get(service, userName);

                Assert.NotNull(outCredential);
                Assert.Equal(userName, userName);
                Assert.Equal(password, outCredential.Password);
                Assert.True(fs.Files.ContainsKey(expectedFilePath));
                Assert.Equal(expectedFileBytes, fs.Files[expectedFilePath]);
            }
            finally
            {
                // Ensure we clean up after ourselves even in case of 'get' failures
                store.Remove(service, userName);
            }
        }

        [Fact]
        public void PlaintextCredentialStore_Get_NotFound_ReturnsNull()
        {
            var fs = new TestFileSystem();
            var store = new PlaintextCredentialStore(fs, StoreRoot, TestNamespace);
            Get_NotFound_ReturnsNull(store);
        }

        [Fact]
        public void PlaintextCredentialStore_Remove_NotFound_ReturnsFalse()
        {
            var fs = new TestFileSystem();
            var store = new PlaintextCredentialStore(fs, StoreRoot, TestNamespace);
            Remove_NotFound_ReturnsFalse(store);
        }
        
        [Fact]
        public void PlaintextCredentialStore_MaxCredentialSize_ReturnsZero()
        {
            var fs = new TestFileSystem();
            ICredentialStore store = new PlaintextCredentialStore(fs, StoreRoot, TestNamespace);
            MaxCredentialSize(store, 0);
        }
    }
}
