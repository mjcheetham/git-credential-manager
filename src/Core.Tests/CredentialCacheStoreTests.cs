using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using Xunit;

namespace GitCredentialManager.Tests
{
    public class CredentialCacheStoreTests : CredentialStoreTests
    {
        private const string TestService = "https://example.com";
        private const string TestPassword = "letmein123"; // [SuppressMessage("Microsoft.Security", "CS001:SecretInline", Justification="Fake credential")]

        [Fact]
        public void CredentialCacheStore_ReadWriteDelete()
        {
            ReadWriteDelete(new CredentialCacheStore(CreateCacheHelper().Object, null));
        }

        [Fact]
        public void CredentialCacheStore_MetadataLifecycle()
        {
            MetadataLifecycle(new CredentialCacheStore(CreateCacheHelper().Object, null));
        }

        [Fact]
        public void CredentialCacheStore_Get_NoAccount_DoesNotMatchOtherAccounts()
        {
            var store = new CredentialCacheStore(CreateCacheHelper().Object, null);
            store.Set(new StoredCredential(new CredentialKey(TestService, "john.doe"), TestPassword));

            Assert.Null(store.Get(new CredentialKey(TestService, null)));
            Assert.False(store.Remove(new CredentialKey(TestService, null)));
            Assert.Equal(new[] { new CredentialKey(TestService, "john.doe") }, store.Find(TestService));
        }

        [Theory]
        [InlineData("1784986200", 1784986200L)]
        [InlineData("invalid", null)]
        [InlineData(null, null)]
        public void CredentialCacheStore_Get_ReadsPasswordExpiry(string value, long? expected)
        {
            var response = new Dictionary<string, string>
            {
                ["username"] = "john.doe",
                ["password"] = TestPassword,
            };
            if (value != null)
            {
                response["password_expiry_utc"] = value;
            }

            var git = new Mock<IGit>(MockBehavior.Strict);
            git.Setup(x => x.InvokeHelperAsync("credential-cache get ", It.IsAny<IDictionary<string, string>>()))
                .ReturnsAsync(response);
            var store = new CredentialCacheStore(git.Object, null);

            StoredCredential credential = store.Get(new CredentialKey(TestService, "john.doe"));

            Assert.Equal(TestPassword, credential.Secret);
            Assert.Equal(expected, credential.Metadata.ExpiresAt?.ToUnixTimeSeconds());
        }

        [Fact]
        public void CredentialCacheStore_Set_WritesPasswordExpiry()
        {
            IDictionary<string, string> input = null;
            var git = new Mock<IGit>(MockBehavior.Strict);
            git.Setup(x => x.InvokeHelperAsync("credential-cache store ", It.IsAny<IDictionary<string, string>>()))
                .Callback((string _, IDictionary<string, string> stdin) => input = stdin)
                .ReturnsAsync(new Dictionary<string, string>());
            var store = new CredentialCacheStore(git.Object, null);
            var metadata = new CredentialMetadata { ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(1784986200) };

            store.Set(new StoredCredential(new CredentialKey(TestService, "john.doe"), TestPassword, metadata));

            Assert.NotNull(input);
            Assert.Equal("1784986200", input["password_expiry_utc"]);
        }

        /// <summary>
        /// Create a fake `git credential-cache` helper which matches entries like the real cache daemon:
        /// a request without a username matches entries for any username.
        /// </summary>
        private static Mock<IGit> CreateCacheHelper()
        {
            var entries = new List<IDictionary<string, string>>();

            string GetUserName(IDictionary<string, string> dict) =>
                dict.TryGetValue("username", out string userName) ? userName : null;

            bool IsMatch(IDictionary<string, string> entry, IDictionary<string, string> input) =>
                entry["url"] == input["url"] &&
                (GetUserName(input) is null || GetUserName(entry) == GetUserName(input));

            var git = new Mock<IGit>(MockBehavior.Strict);
            git.Setup(x => x.InvokeHelperAsync(It.IsAny<string>(), It.IsAny<IDictionary<string, string>>()))
                .Returns((string args, IDictionary<string, string> input) =>
                {
                    IDictionary<string, string> output = new Dictionary<string, string>();
                    switch (args)
                    {
                        case "credential-cache get ":
                            IDictionary<string, string> match = entries.FirstOrDefault(x => IsMatch(x, input));
                            if (match != null)
                            {
                                output = new Dictionary<string, string>(match);
                            }
                            break;

                        case "credential-cache store ":
                            // Storing replaces the entry for the same URL and username
                            entries.RemoveAll(x => x["url"] == input["url"] && GetUserName(x) == GetUserName(input));
                            entries.Add(new Dictionary<string, string>(input));
                            break;

                        case "credential-cache erase ":
                            entries.RemoveAll(x => IsMatch(x, input));
                            break;

                        default:
                            throw new InvalidOperationException($"Unexpected helper arguments: {args}");
                    }

                    return Task.FromResult(output);
                });

            return git;
        }
    }
}
