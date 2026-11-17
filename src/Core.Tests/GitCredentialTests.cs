using Xunit;

namespace GitCredentialManager.Tests;

public class GitCredentialTests
{
    [Fact]
    public void GitCredential_ToString_DoesNotIncludePassword()
    {
        const string password = "letmein123"; // [SuppressMessage("Microsoft.Security", "CS001:SecretInline", Justification="Fake credential")]
        var credential = new GitCredential("john.doe", password);

        string str = credential.ToString();

        Assert.Contains("john.doe", str);
        Assert.DoesNotContain(password, str);
    }
}
