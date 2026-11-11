using System;
using Xunit;
using GitCredentialManager.Interop;
using GitCredentialManager.Interop.MacOS;
using GitCredentialManager.Interop.MacOS.Native;

namespace GitCredentialManager.Tests.Interop.MacOS
{
    public class MacOSKeychainTests : CredentialStoreTests
    {
        private const string TestNamespace = "git-test";

        [MacOSFact]
        public void MacOSKeychain_ReadWriteDelete()
        {
            RunWithKeychain(ReadWriteDelete);
        }

        [MacOSFact]
        public void MacOSKeychain_AccountMatching()
        {
            RunWithKeychain(AccountMatching);
        }

        [MacOSFact]
        public void MacOSKeychain_MetadataLifecycle()
        {
            RunWithKeychain(MetadataLifecycle);
        }

        [MacOSFact]
        public void MacOSKeychain_MaxCredentialSize_ReturnsZero()
        {
            ICredentialStore credentialStore = new MacOSKeychain(TestNamespace);
            Assert.Equal(0, credentialStore.MaxCredentialSize);
        }

        private static void RunWithKeychain(Action<MacOSKeychain> test)
        {
            try
            {
                test(new MacOSKeychain(TestNamespace));
            }
            // There is an unknown issue that the keychain can sometimes get itself in where all API calls
            // result in an errSecAuthFailed error. The only solution seems to be a machine restart, which
            // isn't really possible in CI!
            // The problem has plagued others who are calling the same Keychain APIs from C# such as the
            // MSAL.NET team - they don't know either. It might have something to do with the code signing
            // signature of the binary (our collective best theory).
            // It's probably only diagnosable at this point by Apple, but we don't have a reliable way to
            // reproduce the problem.
            // For now we will just mark the test as "skipped" when we hit this problem.
            catch (InteropException iex) when (iex.ErrorCode == SecurityFramework.ErrorSecAuthFailed)
            {
                AssertEx.Skip("macOS Keychain is in an invalid state (errSecAuthFailed)");
            }
        }
    }
}
