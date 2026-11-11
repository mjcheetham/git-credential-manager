using Xunit;

namespace GitCredentialManager.Tests;

public class StoredCredentialTests
{
    [Fact]
    public void StoredCredential_ToString_DoesNotIncludeSecret()
    {
        const string secret = "letmein123"; // [SuppressMessage("Microsoft.Security", "CS001:SecretInline", Justification="Fake credential")]
        var credential = new StoredCredential(new CredentialKey("https://example.com", "john.doe"), secret);

        string str = credential.ToString();

        Assert.Contains("john.doe", str);
        Assert.DoesNotContain(secret, str);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void CredentialKey_EmptyAccount_EqualsNoAccount(string account)
    {
        Assert.Equal(new CredentialKey("https://example.com", null), new CredentialKey("https://example.com", account));
    }
}
