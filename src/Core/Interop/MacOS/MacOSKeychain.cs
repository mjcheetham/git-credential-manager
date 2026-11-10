using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using GitCredentialManager.Interop.MacOS.Native;
using static GitCredentialManager.Interop.MacOS.Native.CoreFoundation;
using static GitCredentialManager.Interop.MacOS.Native.SecurityFramework;

namespace GitCredentialManager.Interop.MacOS
{
    public class MacOSKeychain : ICredentialStore
    {
        private const string ExpiresAtAttribute = "expires_at";

        private readonly string _namespace;

        #region Constructors

        /// <summary>
        /// Open the default keychain (current user's login keychain).
        /// </summary>
        /// <param name="namespace">Optional namespace to scope credential operations.</param>
        /// <returns>Default keychain.</returns>
        public MacOSKeychain(string @namespace = null)
        {
            PlatformUtils.EnsureMacOS();
            _namespace = @namespace;
        }

        #endregion

        #region ICredentialStore

        public string Name => "macOS Keychain";

        public IList<string> GetAccounts(string service)
        {
            IntPtr query = IntPtr.Zero;
            IntPtr resultPtr = IntPtr.Zero;
            IntPtr servicePtr = IntPtr.Zero;
            IntPtr accountPtr = IntPtr.Zero;

            try
            {
                query = CFDictionaryCreateMutable(
                    IntPtr.Zero,
                    0,
                    IntPtr.Zero, IntPtr.Zero);

                CFDictionaryAddValue(query, kSecClass, kSecClassGenericPassword);
                CFDictionaryAddValue(query, kSecMatchLimit, kSecMatchLimitAll);
                CFDictionaryAddValue(query, kSecReturnAttributes, kCFBooleanTrue);

                if (!string.IsNullOrWhiteSpace(service))
                {
                    string fullService = CreateServiceName(service);
                    servicePtr = CreateCFStringUtf8(fullService);
                    CFDictionaryAddValue(query, kSecAttrService, servicePtr);
                }

                int searchResult = SecItemCopyMatching(query, out resultPtr);

                switch (searchResult)
                {
                    case OK:
                        int typeId = CFGetTypeID(resultPtr);
                        Debug.Assert(typeId == CFArrayGetTypeID(), "Returned unknown item from account query");
                        if (typeId == CFArrayGetTypeID())
                        {
                            int len = (int)CFArrayGetCount(resultPtr);
                            var accounts = new HashSet<string>(len);
                            for (int i = 0; i < len; i++)
                            {
                                IntPtr dict = CFArrayGetValueAtIndex(resultPtr, i);
                                string account = GetStringAttribute(dict, kSecAttrAccount);
                                accounts.Add(account);
                            }

                            return accounts.ToList();
                        }

                        throw new InteropException($"Unknown keychain search result type CFTypeID: {typeId}.", -1);

                    case ErrorSecItemNotFound:
                        return Array.Empty<string>();

                    default:
                        ThrowIfError(searchResult);
                        return null;
                }
            }
            finally
            {
                if (query != IntPtr.Zero) CFRelease(query);
                if (servicePtr != IntPtr.Zero) CFRelease(servicePtr);
                if (accountPtr != IntPtr.Zero) CFRelease(accountPtr);
                if (resultPtr != IntPtr.Zero) CFRelease(resultPtr);
            }
        }

        public ICredential Get(string service, string account)
        {
            IntPtr query = IntPtr.Zero;
            IntPtr resultPtr = IntPtr.Zero;
            IntPtr servicePtr = IntPtr.Zero;
            IntPtr accountPtr = IntPtr.Zero;

            try
            {
                query = CFDictionaryCreateMutable(
                    IntPtr.Zero,
                    0,
                    IntPtr.Zero, IntPtr.Zero);

                CFDictionaryAddValue(query, kSecClass, kSecClassGenericPassword);
                CFDictionaryAddValue(query, kSecMatchLimit, kSecMatchLimitOne);
                CFDictionaryAddValue(query, kSecReturnData, kCFBooleanTrue);
                CFDictionaryAddValue(query, kSecReturnAttributes, kCFBooleanTrue);

                if (!string.IsNullOrWhiteSpace(service))
                {
                    string fullService = CreateServiceName(service);
                    servicePtr = CreateCFStringUtf8(fullService);
                    CFDictionaryAddValue(query, kSecAttrService, servicePtr);
                }

                if (!string.IsNullOrWhiteSpace(account))
                {
                    accountPtr = CreateCFStringUtf8(account);
                    CFDictionaryAddValue(query, kSecAttrAccount, accountPtr);
                }

                int searchResult = SecItemCopyMatching(query, out resultPtr);

                switch (searchResult)
                {
                    case OK:
                        int typeId = CFGetTypeID(resultPtr);
                        Debug.Assert(typeId != CFArrayGetTypeID(), "Returned more than one keychain item in search");
                        if (typeId == CFDictionaryGetTypeID())
                        {
                            return CreateCredentialFromAttributes(resultPtr);
                        }

                        throw new InteropException($"Unknown keychain search result type CFTypeID: {typeId}.", -1);

                    case ErrorSecItemNotFound:
                        return null;

                    default:
                        ThrowIfError(searchResult);
                        return null;
                }
            }
            finally
            {
                if (query != IntPtr.Zero) CFRelease(query);
                if (servicePtr != IntPtr.Zero) CFRelease(servicePtr);
                if (accountPtr != IntPtr.Zero) CFRelease(accountPtr);
                if (resultPtr != IntPtr.Zero) CFRelease(resultPtr);
            }
        }

        public void AddOrUpdate(string service, string account, string secret, CredentialMetadata metadata = null)
        {
            EnsureArgument.NotNullOrWhiteSpace(service, nameof(service));

            ICredential existing = Get(service, account);
            bool sameCredential = existing != null &&
                                  StringComparer.Ordinal.Equals(existing.Account, account) &&
                                  StringComparer.Ordinal.Equals(existing.Password, secret);
            metadata ??= sameCredential ? existing.Metadata : CredentialMetadata.Empty;
            if (sameCredential && metadata == existing.Metadata)
            {
                return;
            }

            byte[] secretBytes = Encoding.UTF8.GetBytes(secret);

            IntPtr passwordData = IntPtr.Zero;
            IntPtr itemRef = IntPtr.Zero;

            string serviceName = CreateServiceName(service);

            uint serviceNameLength = (uint) serviceName.Length;
            uint accountLength = (uint) (account?.Length ?? 0);

            try
            {
                // Check if an entry already exists in the keychain
                int findResult = SecKeychainFindGenericPassword(
                    IntPtr.Zero, serviceNameLength, serviceName, accountLength, account,
                    out uint passwordDataLength, out passwordData, out itemRef);

                switch (findResult)
                {
                    case OK:
                        UpdateCredential(itemRef, secretBytes, metadata);
                        break;

                    // Create new entry
                    case ErrorSecItemNotFound:
                        ThrowIfError(
                            SecKeychainAddGenericPassword(IntPtr.Zero, serviceNameLength, serviceName, accountLength,
                                account, (uint) secretBytes.Length, secretBytes, out itemRef),
                            "Could not create new item"
                        );

                        // If we have metadata, set the metadata on the newly created item.
                        if (metadata != CredentialMetadata.Empty)
                        {
                            UpdateCredential(itemRef, secretBytes, metadata);
                        }
                        break;

                    default:
                        ThrowIfError(findResult);
                        break;
                }
            }
            finally
            {
                if (passwordData != IntPtr.Zero)
                {
                    SecKeychainItemFreeContent(IntPtr.Zero, passwordData);
                }

                if (itemRef != IntPtr.Zero)
                {
                    CFRelease(itemRef);
                }
            }
        }

        public bool Remove(string service, string account)
        {
            IntPtr query = IntPtr.Zero;
            IntPtr itemRefPtr = IntPtr.Zero;
            IntPtr servicePtr = IntPtr.Zero;
            IntPtr accountPtr = IntPtr.Zero;

            try
            {
                query = CFDictionaryCreateMutable(
                    IntPtr.Zero,
                    0,
                    IntPtr.Zero, IntPtr.Zero);

                CFDictionaryAddValue(query, kSecClass, kSecClassGenericPassword);
                CFDictionaryAddValue(query, kSecMatchLimit, kSecMatchLimitOne);
                CFDictionaryAddValue(query, kSecReturnRef, kCFBooleanTrue);

                if (!string.IsNullOrWhiteSpace(service))
                {
                    string fullService = CreateServiceName(service);
                    servicePtr = CreateCFStringUtf8(fullService);
                    CFDictionaryAddValue(query, kSecAttrService, servicePtr);
                }

                if (!string.IsNullOrWhiteSpace(account))
                {
                    accountPtr = CreateCFStringUtf8(account);
                    CFDictionaryAddValue(query, kSecAttrAccount, accountPtr);
                }

                // Search for the credential to delete and get the SecKeychainItem ref.
                int searchResult = SecItemCopyMatching(query, out itemRefPtr);
                switch (searchResult)
                {
                    case OK:
                        // Delete the item
                        ThrowIfError(
                            SecKeychainItemDelete(itemRefPtr)
                        );
                        return true;

                    case ErrorSecItemNotFound:
                        return false;

                    default:
                        ThrowIfError(searchResult);
                        return false;
                }
            }
            finally
            {
                if (query != IntPtr.Zero) CFRelease(query);
                if (itemRefPtr != IntPtr.Zero) CFRelease(itemRefPtr);
                if (servicePtr != IntPtr.Zero) CFRelease(servicePtr);
                if (accountPtr != IntPtr.Zero) CFRelease(accountPtr);
            }
        }

        #endregion

        private static unsafe void UpdateCredential(IntPtr itemRef, byte[] secret, CredentialMetadata metadata)
        {
            var attrs = new Dictionary<string, string>();

            if (metadata.ExpiresAt is { } expiry)
            {
                attrs[ExpiresAtAttribute] = expiry.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            }

            // Convert the metadata dictionary into a (key=value\n)* string to store in the generic attribute.
            // Always write the attribute, even when empty, so that any existing metadata is cleared.
            string value = string.Empty;
            if (attrs.Count > 0)
            {
                using var writer = new StringWriter();
                writer.WriteDictionary(attrs);
                writer.Flush();
                value = writer.ToString();
            }

            byte[] bytes = Encoding.UTF8.GetBytes(value);
            fixed (byte* data = bytes)
            {
                var attribute = new SecKeychainAttribute
                {
                    Tag = SecKeychainAttrType.GenericItem,
                    Length = (uint)bytes.Length,
                    Data = (IntPtr)data
                };
                var attributes = new SecKeychainAttributeList
                {
                    Count = 1,
                    Attributes = (IntPtr)(&attribute)
                };

                // Update the item with the new password and metadata.
                ThrowIfError(
                    SecKeychainItemModifyAttributesAndData(itemRef, (IntPtr)(&attributes), (uint)secret.Length, secret),
                    "Could not update existing item");
            }
        }

        private static IntPtr CreateCFStringUtf8(string str)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(str);
            return CFStringCreateWithBytes(IntPtr.Zero,
                bytes, bytes.Length, CFStringEncoding.kCFStringEncodingUTF8, false);
        }

        private static ICredential CreateCredentialFromAttributes(IntPtr attributes)
        {
            string service = GetStringAttribute(attributes, kSecAttrService);
            string account = GetStringAttribute(attributes, kSecAttrAccount);
            string password = GetStringAttribute(attributes, kSecValueData);
            string label = GetStringAttribute(attributes, kSecAttrLabel);

            var metadata = CredentialMetadata.Empty;
            string generic = GetStringAttribute(attributes, kSecAttrGeneric);

            // Generic attribute is used to store the metadata in (key=value\n)* format.
            if (!string.IsNullOrEmpty(generic))
            {
                using var reader = new StringReader(generic);
                IDictionary<string, string> attrs = reader.ReadDictionary();

                // Currently, we only store the expiry time as a unix timestamp.
                if (attrs.TryGetValue(ExpiresAtAttribute, out string value) &&
                    DateTimeOffsetEx.TryParseUnixTimeSeconds(value, out DateTimeOffset expiry))
                {
                    metadata = metadata with { ExpiresAt = expiry };
                }
            }

            return new MacOSKeychainCredential(service, account, password, label, metadata);
        }

        private static string GetStringAttribute(IntPtr dict, IntPtr key)
        {
            if (dict == IntPtr.Zero)
            {
                return null;
            }

            if (CFDictionaryGetValueIfPresent(dict, key, out IntPtr value) && value != IntPtr.Zero)
            {
                if (CFGetTypeID(value) == CFStringGetTypeID())
                {
                    return CFStringToString(value);
                }

                if (CFGetTypeID(value) == CFDataGetTypeID())
                {
                    int length = CFDataGetLength(value);
                    IntPtr ptr = CFDataGetBytePtr(value);
                    return Marshal.PtrToStringAuto(ptr, length);
                }
            }

            return null;
        }

        private string CreateServiceName(string service)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(_namespace))
            {
                sb.AppendFormat("{0}:", _namespace);
            }

            sb.Append(service);
            return sb.ToString();
        }
    }
}
