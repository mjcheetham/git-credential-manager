using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using Xunit;

namespace GitCredentialManager.Tests
{
    public class CredentialCacheStoreTests : CredentialStoreTests
    {
        [Theory]
        [InlineData("981163506", 981163506L)]
        [InlineData(null, null)]
        [InlineData("invalid", null)]
        [InlineData("9223372036854775807", null)]
        [InlineData("-9223372036854775808", null)]
        public void CredentialCacheStore_Get_ReadsExpiryWithoutRejectingExpiredCredential(string value, long? expected)
        {
            var response = new Dictionary<string, string>
            {
                ["username"] = "alice",
                ["password"] = "password"
            };
            if (value != null)
            {
                response["password_expiry_utc"] = value;
            }

            var git = new Mock<IGit>(MockBehavior.Strict);
            git.Setup(x => x.InvokeHelperAsync("credential-cache get ", It.IsAny<IDictionary<string, string>>()))
                .ReturnsAsync(response);
            var store = new CredentialCacheStore(git.Object, null);

            ICredential credential = store.Get("https://example.com", "alice");

            Assert.Equal("password", credential.Password);
            Assert.Equal(expected, credential.Metadata.ExpiresAt?.ToUnixTimeSeconds());
        }

        [Fact]
        public void CredentialCacheStore_MetadataLifecycle_UsesPasswordExpiryUtc()
        {
            var entries = new Dictionary<string, IDictionary<string, string>>();
            var git = new Mock<IGit>(MockBehavior.Strict);
            git.Setup(x => x.InvokeHelperAsync(It.IsAny<string>(), It.IsAny<IDictionary<string, string>>()))
                .Returns((string command, IDictionary<string, string> input) =>
                {
                    string account = input["username"];
                    IDictionary<string, string> output = new Dictionary<string, string>();
                    switch (command)
                    {
                        case "credential-cache get --timeout=3600":
                            entries.TryGetValue(account, out var entry);
                            output = entry ?? output;
                            break;
                        case "credential-cache store --timeout=3600":
                            Assert.DoesNotContain("expiresAt", input.Keys);
                            entries[account] = new Dictionary<string, string>(input);
                            break;
                        case "credential-cache erase --timeout=3600":
                            entries.Remove(account);
                            break;
                        default:
                            throw new InvalidOperationException(command);
                    }

                    return Task.FromResult(output);
                });

            var store = new CredentialCacheStore(git.Object, "--timeout=3600");
            MetadataLifecycle(store);

            git.Verify(x => x.InvokeHelperAsync("credential-cache store --timeout=3600",
                It.Is<IDictionary<string, string>>(input => input.ContainsKey("password_expiry_utc"))),
                Times.AtLeastOnce);
        }
    }
}
