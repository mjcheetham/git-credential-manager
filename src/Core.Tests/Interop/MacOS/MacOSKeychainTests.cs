using GitCredentialManager.Interop;
using GitCredentialManager.Interop.MacOS;
using GitCredentialManager.Interop.MacOS.Native;

namespace GitCredentialManager.Tests.Interop.MacOS
{
    public class MacOSKeychainTests : CredentialStoreTests
    {
        private const string TestNamespace = "git-test";

        [MacOSFact]
        public void MacOSKeychain_MetadataLifecycle()
        {
            try
            {
                MetadataLifecycle(new MacOSKeychain(TestNamespace));
            }
            catch (InteropException iex) when (iex.ErrorCode == SecurityFramework.ErrorSecAuthFailed)
            {
                AssertEx.Skip("macOS Keychain is in an invalid state (errSecAuthFailed)");
            }
        }

        [MacOSFact]
        public void MacOSKeychain_ReadWriteDelete()
        {
            try
            {
                ReadWriteDelete(new MacOSKeychain(TestNamespace));
            }
            // There is an unknown issue that the keychain can sometimes get itself in where all API calls
            // result in an errSecAuthFailed error. The only solution seems to be a machine restart, which
            // isn't really possible in CI!
            // The problem has plagued others who are calling the same Keychain APIs from C# such as the
            // MSAL.NET team - they don't know either. It might have something to do with the code signing
            // signature of the binary (our collective best theory).
            // It's probably only diagnosable at this point by Apple, but we don't have a reliable way to
            // reproduce the problem.
            // For now, we will just mark the test as "skipped" when we hit this problem.
            catch (InteropException iex) when (iex.ErrorCode == SecurityFramework.ErrorSecAuthFailed)
            {
                AssertEx.Skip("macOS Keychain is in an invalid state (errSecAuthFailed)");
            }
        }

        [MacOSFact]
        public void MacOSKeychain_Get_NotFound_ReturnsNull()
        {
            Get_NotFound_ReturnsNull(new MacOSKeychain(TestNamespace));
        }

        [MacOSFact]
        public void MacOSKeychain_Remove_NotFound_ReturnsFalse()
        {
            Remove_NotFound_ReturnsFalse(new MacOSKeychain(TestNamespace));
        }

        [MacOSFact]
        public void MacOSKeychain_MaxCredentialSize_ReturnsZero()
        {
            MaxCredentialSize(new MacOSKeychain(TestNamespace), 0);
        }
    }
}
