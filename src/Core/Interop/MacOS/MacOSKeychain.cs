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
    public class MacOSKeychain : ICredentialStore2
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

        public StoredCredential Get(CredentialKey key)
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

                servicePtr = CreateCFStringUtf8(CreateServiceName(key.Service));
                CFDictionaryAddValue(query, kSecAttrService, servicePtr);

                // An empty account only matches items stored without an account
                accountPtr = CreateCFStringUtf8(key.Account ?? string.Empty);
                CFDictionaryAddValue(query, kSecAttrAccount, accountPtr);

                int searchResult = SecItemCopyMatching(query, out resultPtr);

                switch (searchResult)
                {
                    case OK:
                        int typeId = CFGetTypeID(resultPtr);
                        Debug.Assert(typeId != CFArrayGetTypeID(), "Returned more than one keychain item in search");
                        if (typeId == CFDictionaryGetTypeID())
                        {
                            return CreateCredentialFromAttributes(resultPtr).ToStoredCredential();
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

        public IReadOnlyList<CredentialKey> Find(string service, string account = null)
        {
            EnsureArgument.NotNullOrWhiteSpace(service, nameof(service));

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

                // Only ask for attributes; reading item data can prompt the user for access
                CFDictionaryAddValue(query, kSecClass, kSecClassGenericPassword);
                CFDictionaryAddValue(query, kSecMatchLimit, kSecMatchLimitAll);
                CFDictionaryAddValue(query, kSecReturnAttributes, kCFBooleanTrue);

                servicePtr = CreateCFStringUtf8(CreateServiceName(service));
                CFDictionaryAddValue(query, kSecAttrService, servicePtr);

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
                        Debug.Assert(typeId == CFArrayGetTypeID(), "Returned unknown item from account query");
                        if (typeId == CFArrayGetTypeID())
                        {
                            int len = (int)CFArrayGetCount(resultPtr);
                            var keys = new HashSet<CredentialKey>(len);
                            for (int i = 0; i < len; i++)
                            {
                                IntPtr dict = CFArrayGetValueAtIndex(resultPtr, i);
                                keys.Add(new CredentialKey(service, GetStringAttribute(dict, kSecAttrAccount)));
                            }

                            return keys.ToList();
                        }

                        throw new InteropException($"Unknown keychain search result type CFTypeID: {typeId}.", -1);

                    case ErrorSecItemNotFound:
                        return Array.Empty<CredentialKey>();

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

        public void Set(StoredCredential credential)
        {
            EnsureArgument.NotNull(credential, nameof(credential));

            CredentialKey key = credential.Key;

            // No need to update existing credential if nothing has changed
            StoredCredential existing = Get(key);
            if (existing != null &&
                StringComparer.Ordinal.Equals(existing.Secret, credential.Secret) &&
                existing.Metadata == credential.Metadata)
            {
                return;
            }

            byte[] secretBytes = Encoding.UTF8.GetBytes(credential.Secret);
            IntPtr itemRef = FindItem(key);

            try
            {
                if (itemRef != IntPtr.Zero)
                {
                    UpdateItem(itemRef, secretBytes, credential.Metadata);
                }
                else
                {
                    string serviceName = CreateServiceName(key.Service);
                    ThrowIfError(
                        SecKeychainAddGenericPassword(IntPtr.Zero, (uint) serviceName.Length, serviceName,
                            (uint) (key.Account?.Length ?? 0), key.Account, (uint) secretBytes.Length, secretBytes,
                            out itemRef),
                        "Could not create new item"
                    );

                    // Metadata can only be set on an item after it has been created
                    if (credential.Metadata != CredentialMetadata.Empty)
                    {
                        UpdateItem(itemRef, secretBytes, credential.Metadata);
                    }
                }
            }
            finally
            {
                if (itemRef != IntPtr.Zero)
                {
                    CFRelease(itemRef);
                }
            }
        }

        public bool Remove(CredentialKey key)
        {
            IntPtr itemRef = FindItem(key);
            if (itemRef == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                ThrowIfError(SecKeychainItemDelete(itemRef));
                return true;
            }
            finally
            {
                CFRelease(itemRef);
            }
        }

        #endregion

        /// <summary>
        /// Find the keychain item stored with exactly the given key.
        /// </summary>
        /// <returns>Item reference that must be released by the caller, or <see cref="IntPtr.Zero"/> if not found.</returns>
        private IntPtr FindItem(CredentialKey key)
        {
            IntPtr query = IntPtr.Zero;
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

                servicePtr = CreateCFStringUtf8(CreateServiceName(key.Service));
                CFDictionaryAddValue(query, kSecAttrService, servicePtr);

                // An empty account only matches items stored without an account
                accountPtr = CreateCFStringUtf8(key.Account ?? string.Empty);
                CFDictionaryAddValue(query, kSecAttrAccount, accountPtr);

                int searchResult = SecItemCopyMatching(query, out IntPtr itemRef);
                switch (searchResult)
                {
                    case OK:
                        return itemRef;

                    case ErrorSecItemNotFound:
                        return IntPtr.Zero;

                    default:
                        ThrowIfError(searchResult);
                        return IntPtr.Zero;
                }
            }
            finally
            {
                if (query != IntPtr.Zero) CFRelease(query);
                if (servicePtr != IntPtr.Zero) CFRelease(servicePtr);
                if (accountPtr != IntPtr.Zero) CFRelease(accountPtr);
            }
        }

        private static unsafe void UpdateItem(IntPtr itemRef, byte[] secret, CredentialMetadata metadata)
        {
            var attrs = new Dictionary<string, string>();
            if (metadata.ExpiresAt is { } expiry)
            {
                attrs[ExpiresAtAttribute] = expiry.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            }

            // Store metadata in the generic attribute in (key=value\n)* format.
            // Always write the attribute, even when empty, so that any existing metadata is cleared.
            string value = string.Empty;
            if (attrs.Count > 0)
            {
                using var writer = new StringWriter();
                writer.WriteDictionary(attrs);
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

                ThrowIfError(
                    SecKeychainItemModifyAttributesAndData(itemRef, (IntPtr)(&attributes), (uint)secret.Length, secret),
                    "Could not update existing item"
                );
            }
        }

        private static IntPtr CreateCFStringUtf8(string str)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(str);
            return CFStringCreateWithBytes(IntPtr.Zero,
                bytes, bytes.Length, CFStringEncoding.kCFStringEncodingUTF8, false);
        }

        private MacOSKeychainCredential CreateCredentialFromAttributes(IntPtr attributes)
        {
            string serviceName = GetStringAttribute(attributes, kSecAttrService);
            string account = GetStringAttribute(attributes, kSecAttrAccount);
            string password = GetStringAttribute(attributes, kSecValueData);
            string label = GetStringAttribute(attributes, kSecAttrLabel);
            string generic = GetStringAttribute(attributes, kSecAttrGeneric);

            string service = GetServiceFromServiceName(serviceName);
            CredentialMetadata metadata = ReadMetadata(generic);

            return new MacOSKeychainCredential(service, account, password, label, metadata);
        }

        private static CredentialMetadata ReadMetadata(string generic)
        {
            var metadata = CredentialMetadata.Empty;
            if (string.IsNullOrEmpty(generic))
            {
                return metadata;
            }

            using var reader = new StringReader(generic);
            IDictionary<string, string> attrs = reader.ReadDictionary();
            if (attrs.TryGetValue(ExpiresAtAttribute, out string value) &&
                DateTimeOffsetEx.TryParseUnixTimeSeconds(value, out DateTimeOffset expiry))
            {
                metadata = metadata with { ExpiresAt = expiry };
            }

            return metadata;
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

        private string GetServiceFromServiceName(string serviceName)
        {
            if (!string.IsNullOrWhiteSpace(_namespace) && serviceName is not null)
            {
                string prefix = $"{_namespace}:";
                if (serviceName.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return serviceName.Substring(prefix.Length);
                }
            }

            return serviceName;
        }
    }
}
