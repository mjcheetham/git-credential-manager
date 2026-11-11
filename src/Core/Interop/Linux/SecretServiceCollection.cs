using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using GitCredentialManager.Interop.Linux.Native;
using static GitCredentialManager.Interop.Linux.Native.Gobject;
using static GitCredentialManager.Interop.Linux.Native.Glib;
using static GitCredentialManager.Interop.Linux.Native.Libsecret;
using static GitCredentialManager.Interop.Linux.Native.Libsecret.SecretSchemaAttributeType;
using static GitCredentialManager.Interop.Linux.Native.Libsecret.SecretSchemaFlags;

namespace GitCredentialManager.Interop.Linux
{
    public class SecretServiceCollection : ICredentialStore2
    {
        private const string SchemaName = "com.microsoft.GitCredentialManager";
        private const string ServiceAttributeName = "service";
        private const string AccountAttributeName = "account";
        private const string ExpiresAtAttributeName = "expires_at";
        private const string PlainTextContentType = "text/plain";

        private readonly string _namespace;

        internal enum Operation
        {
            ConnectBus,
            OpenService,
            Search,
            Unlock,
            Store,
            Erase,
            Load,
        }

        #region Constructors

        /// <summary>
        /// Open the default secret collection for the current user.
        /// </summary>
        /// <param name="namespace">Optional namespace to scope credential operations.</param>
        /// <returns>Default secret collection.</returns>
        public SecretServiceCollection(string @namespace)
        {
            PlatformUtils.EnsureLinux();
            _namespace = @namespace;
        }

        #endregion

        #region ICredentialStore

        public string Name => "freedesktop.org Secret Service";

        public unsafe StoredCredential Get(CredentialKey key)
        {
            GHashTable* queryAttrs = null;
            GList* results = null;

            try
            {
                SecretService* secService = GetSecretService();

                queryAttrs = CreateSearchQuery(key.Service, key.Account);
                results = Search(secService, queryAttrs);

                SecretItem* item = FindItem(results, key.Account);
                if (item == null)
                {
                    return null;
                }

                EnsureUnlocked(secService, item);
                return CreateCredentialFromItem(item);
            }
            finally
            {
                if (queryAttrs != null) g_hash_table_destroy(queryAttrs);
                if (results != null) g_list_free_full(results, g_object_unref);
            }
        }

        public unsafe IReadOnlyList<CredentialKey> Find(string service, string account = null)
        {
            EnsureArgument.NotNullOrWhiteSpace(service, nameof(service));

            GHashTable* queryAttrs = null;
            GList* results = null;

            try
            {
                SecretService* secService = GetSecretService();

                queryAttrs = CreateSearchQuery(service, account);
                results = Search(secService, queryAttrs);

                // Attributes can be read without loading each item's secret
                var keys = new HashSet<CredentialKey>();
                for (GList* itemPtr = results; itemPtr != null && itemPtr->data != IntPtr.Zero; itemPtr = (GList*)itemPtr->next)
                {
                    var item = (SecretItem*)itemPtr->data;
                    keys.Add(new CredentialKey(service, GetItemAttribute(item, AccountAttributeName)));
                }

                return keys.ToList();
            }
            finally
            {
                if (queryAttrs != null) g_hash_table_destroy(queryAttrs);
                if (results != null) g_list_free_full(results, g_object_unref);
            }
        }

        public unsafe void Set(StoredCredential credential)
        {
            EnsureArgument.NotNull(credential, nameof(credential));

            string service = credential.Key.Service;
            string account = credential.Key.Account;
            CredentialMetadata metadata = credential.Metadata;

            GHashTable* queryAttrs = null;
            GList* results = null;
            GHashTable* attributes = null;
            SecretValue* secretValue = null;
            GError *error = null;

            // If there is an existing credential that matches the same account, password, and metadata
            // then don't bother writing out anything because they're the same!
            StoredCredential existingCred = Get(credential.Key);
            if (existingCred != null &&
                StringComparer.Ordinal.Equals(existingCred.Secret, credential.Secret) &&
                existingCred.Metadata == metadata)
            {
                return;
            }

            try
            {
                SecretService* secService = GetSecretService();

                SecretSchema schema = GetSchema();

                // Secret Service only replaces an existing item when all of its attributes match, so storing
                // different metadata would create a second item for the same credential. Look for an item
                // with exactly this service and account to update in place instead.
                queryAttrs = CreateSearchQuery(service, account);
                results = Search(secService, queryAttrs);
                SecretItem* existingItem = FindItem(results, account);

                // Create attributes for the key and user
                attributes = g_hash_table_new_full(g_str_hash, g_str_equal,
                    Marshal.FreeHGlobal, Marshal.FreeHGlobal);

                string fullServiceName = CreateServiceName(service);
                IntPtr serviceKeyPtr = Marshal.StringToHGlobalAnsi(ServiceAttributeName);
                IntPtr serviceValuePtr = Marshal.StringToHGlobalAnsi(fullServiceName);
                g_hash_table_insert(attributes, serviceKeyPtr, serviceValuePtr);

                if (!string.IsNullOrWhiteSpace(account))
                {
                    IntPtr accountKeyPtr = Marshal.StringToHGlobalAnsi(AccountAttributeName);
                    IntPtr accountValuePtr = Marshal.StringToHGlobalAnsi(account);
                    g_hash_table_insert(attributes, accountKeyPtr, accountValuePtr);
                }

                if (metadata.ExpiresAt is { } expiry)
                {
                    IntPtr expiryKeyPtr = Marshal.StringToHGlobalAnsi(ExpiresAtAttributeName);
                    IntPtr expiryValuePtr = Marshal.StringToHGlobalAnsi(
                        expiry.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
                    g_hash_table_insert(attributes, expiryKeyPtr, expiryValuePtr);
                }

                // Create the secret value object from the secret string
                byte[] secretBytes = Encoding.UTF8.GetBytes(credential.Secret);
                secretValue = secret_value_new(secretBytes, secretBytes.Length, PlainTextContentType);

                bool result;
                if (existingItem != null)
                {
                    EnsureUnlocked(secService, existingItem);

                    // Update the secret and replace all attributes, which clears any metadata not being stored
                    result = secret_item_set_secret_sync(existingItem, secretValue, IntPtr.Zero, out error);
                    if (error != null || !result)
                    {
                        throw CreateException(Operation.Store, error);
                    }

                    result = secret_item_set_attributes_sync(existingItem, ref schema, attributes, IntPtr.Zero, out error);
                }
                else
                {
                    // Store the secret with the associated attributes
                    result = secret_service_store_sync(
                        secService,
                        ref schema,
                        attributes,
                        null,
                        fullServiceName, // Use full service name as label
                        secretValue,
                        IntPtr.Zero,
                        out error);
                }

                if (error != null)
                {
                    throw CreateException(Operation.Store, error);
                }

                if (!result)
                {
                    throw CreateException(Operation.Store, null);
                }
            }
            finally
            {
                if (queryAttrs != null) g_hash_table_destroy(queryAttrs);
                if (results != null) g_list_free_full(results, g_object_unref);
                if (attributes != null) g_hash_table_destroy(attributes);
                if (secretValue != null) secret_value_unref(secretValue);
                if (error != null) g_error_free(error);
            }
        }

        public unsafe bool Remove(CredentialKey key)
        {
            GHashTable* queryAttrs = null;
            GList* results = null;
            GError* error = null;

            try
            {
                SecretService* secService = GetSecretService();

                queryAttrs = CreateSearchQuery(key.Service, key.Account);
                results = Search(secService, queryAttrs);

                SecretItem* item = FindItem(results, key.Account);
                if (item == null)
                {
                    return false;
                }

                EnsureUnlocked(secService, item);

                bool result = secret_item_delete_sync(item, IntPtr.Zero, out error);
                if (error != null || !result)
                {
                    throw CreateException(Operation.Erase, error);
                }

                return true;
            }
            finally
            {
                if (queryAttrs != null) g_hash_table_destroy(queryAttrs);
                if (results != null) g_list_free_full(results, g_object_unref);
                if (error != null) g_error_free(error);
            }
        }

        #endregion

        private unsafe GHashTable* CreateSearchQuery(string service, string account)
        {
            // Build search query
            GHashTable* queryAttrs = g_hash_table_new_full(
                g_str_hash, g_str_equal,
                Marshal.FreeHGlobal, Marshal.FreeHGlobal);

            // If we've be given a service then filter on the service attribute
            if (!string.IsNullOrWhiteSpace(service))
            {
                string fullServiceName = CreateServiceName(service);
                IntPtr keyPtr = Marshal.StringToHGlobalAnsi(ServiceAttributeName);
                IntPtr valuePtr = Marshal.StringToHGlobalAnsi(fullServiceName);
                g_hash_table_insert(queryAttrs, keyPtr, valuePtr);
            }

            // If we've be given a username then filter on the account attribute
            if (!string.IsNullOrWhiteSpace(account))
            {
                IntPtr keyPtr = Marshal.StringToHGlobalAnsi(AccountAttributeName);
                IntPtr valuePtr = Marshal.StringToHGlobalAnsi(account);
                g_hash_table_insert(queryAttrs, keyPtr, valuePtr);
            }

            return queryAttrs;
        }

        private static unsafe GList* Search(SecretService* secService, GHashTable* queryAttrs)
        {
            SecretSchema schema = GetSchema();
            GError* error = null;

            try
            {
                GList* results = secret_service_search_sync(
                    secService,
                    ref schema,
                    queryAttrs,
                    SecretSearchFlags.SECRET_SEARCH_UNLOCK | SecretSearchFlags.SECRET_SEARCH_ALL,
                    IntPtr.Zero,
                    out error);

                if (error != null)
                {
                    if (results != null) g_list_free_full(results, g_object_unref);
                    throw CreateException(Operation.Search, error);
                }

                return results;
            }
            finally
            {
                if (error != null) g_error_free(error);
            }
        }

        /// <summary>
        /// Find the item stored for exactly the given account in a list of search results.
        /// </summary>
        /// <remarks>
        /// Searching without an account matches items for every account, so compare account attributes exactly.
        /// Credentials without an account are stored without an account attribute.
        /// </remarks>
        private static unsafe SecretItem* FindItem(GList* results, string account)
        {
            for (GList* itemPtr = results; itemPtr != null && itemPtr->data != IntPtr.Zero; itemPtr = (GList*)itemPtr->next)
            {
                var item = (SecretItem*)itemPtr->data;
                string itemAccount = GetItemAttribute(item, AccountAttributeName);
                if (StringComparer.Ordinal.Equals(string.IsNullOrWhiteSpace(itemAccount) ? null : itemAccount, account))
                {
                    return item;
                }
            }

            return null;
        }

        private static unsafe void EnsureUnlocked(SecretService* secService, SecretItem* item)
        {
            // Searches can return locked items even when unlocking was requested.
            if (!secret_item_get_locked(item))
            {
                return;
            }

            GError* error = null;
            try
            {
                var toUnlockList = new GList
                {
                    data = (IntPtr) item,
                    next = IntPtr.Zero,
                    prev = IntPtr.Zero
                };

                int numUnlocked = secret_service_unlock_sync(
                    secService,
                    &toUnlockList,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    out error
                );

                if (error != null || numUnlocked != 1)
                {
                    throw CreateException(Operation.Unlock, error);
                }
            }
            finally
            {
                if (error != null) g_error_free(error);
            }
        }

        private static unsafe string GetItemAttribute(SecretItem* item, string name)
        {
            GHashTable* secretAttrs = null;
            IntPtr keyPtr = IntPtr.Zero;

            try
            {
                secretAttrs = secret_item_get_attributes(item);
                keyPtr = Marshal.StringToHGlobalAnsi(name);
                IntPtr valuePtr = g_hash_table_lookup(secretAttrs, keyPtr);
                return Marshal.PtrToStringAuto(valuePtr);
            }
            finally
            {
                if (secretAttrs != null) g_hash_table_unref(secretAttrs);
                if (keyPtr != IntPtr.Zero) Marshal.FreeHGlobal(keyPtr);
            }
        }

        private unsafe StoredCredential CreateCredentialFromItem(SecretItem* item)
        {
            SecretValue* value = null;
            IntPtr passwordPtr = IntPtr.Zero;
            GError* error = null;

            try
            {
                string serviceName = GetItemAttribute(item, ServiceAttributeName);
                string account = GetItemAttribute(item, AccountAttributeName);
                string expiryValue = GetItemAttribute(item, ExpiresAtAttributeName);

                // Load the secret value
                bool loaded = secret_item_load_secret_sync(item, IntPtr.Zero, out error);
                if (error != null || !loaded)
                {
                    throw CreateException(Operation.Load, error);
                }

                value = secret_item_get_secret(item);
                if (value == null)
                {
                    throw CreateException(Operation.Load, null);
                }

                // Extract the secret/password
                passwordPtr = secret_value_get(value, out int passwordLength);
                string password = Marshal.PtrToStringAuto(passwordPtr, passwordLength);

                string service = GetServiceFromServiceName(serviceName);

                var metadata = CredentialMetadata.Empty;
                if (DateTimeOffsetEx.TryParseUnixTimeSeconds(expiryValue, out DateTimeOffset expiry))
                {
                    metadata = metadata with { ExpiresAt = expiry };
                }

                return new StoredCredential(new CredentialKey(service, account), password, metadata);
            }
            finally
            {
                if (value != null) secret_value_unref(value);
                if (error != null) g_error_free(error);
            }
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

        private static unsafe SecretService* GetSecretService()
        {
            IntPtr connection = IntPtr.Zero;
            GError* error = null;

            try
            {
                // Diagnose bus connectivity separately from Secret Service availability.
                connection = Gio.g_bus_get_sync(Gio.GBusType.G_BUS_TYPE_SESSION, IntPtr.Zero, out error);
                if (error != null || connection == IntPtr.Zero)
                {
                    throw CreateException(Operation.ConnectBus, error);
                }

                SecretService* service = secret_service_get_sync(
                    SecretServiceFlags.SECRET_SERVICE_OPEN_SESSION | SecretServiceFlags.SECRET_SERVICE_LOAD_COLLECTIONS,
                    IntPtr.Zero, out error);

                if (error != null || service == null)
                {
                    throw CreateException(Operation.OpenService, error);
                }

                return service;
            }
            finally
            {
                if (error != null) g_error_free(error);
                if (connection != IntPtr.Zero) g_object_unref(connection);
            }
        }

        private static unsafe InteropException CreateException(Operation operation, GError* error)
        {
            if (error == null)
            {
                return CreateException(operation, null, -1, null);
            }

            string domain = Marshal.PtrToStringUTF8(g_quark_to_string(error->domain));
            string message = Marshal.PtrToStringUTF8(error->message);
            return CreateException(operation, domain, error->code, message);
        }

        internal static InteropException CreateException(
            Operation operation, string errorDomain, int errorCode, string errorMessage)
        {
            string message = operation switch
            {
                Operation.ConnectBus => "Failed to connect to the D-Bus session bus.",
                Operation.OpenService => "Failed to open the Secret Service session.",
                Operation.Search => "Failed to search for credentials.",
                Operation.Unlock => "Failed to unlock the secret item.",
                Operation.Store => "Failed to store credentials.",
                Operation.Erase => "Failed to erase credentials.",
                Operation.Load => "Failed to load the secret.",
                _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
            };

            bool serviceUnavailable = errorDomain == Gio.DBusErrorDomain &&
                (errorCode == (int)Gio.GDBusError.G_DBUS_ERROR_SERVICE_UNKNOWN ||
                 errorCode == (int)Gio.GDBusError.G_DBUS_ERROR_NAME_HAS_NO_OWNER);

            string hint;
            if (errorDomain == Gio.IOErrorDomain && errorCode == (int)Gio.GIOError.G_IO_ERROR_CANCELLED)
            {
                hint = "The operation was cancelled.";
            }
            else if (errorDomain == Libsecret.ErrorDomain && errorCode == (int)SecretError.SECRET_ERROR_IS_LOCKED)
            {
                hint = "The secret item or collection is locked. Unlock it using your Secret Service provider before retrying.";
            }
            else if (operation == Operation.ConnectBus)
            {
                hint = "Ensure a D-Bus session bus is running and accessible. In a headless session, start your shell or workload with 'dbus-run-session'.";
            }
            else if (operation == Operation.OpenService)
            {
                hint = (serviceUnavailable ? "No Secret Service provider is available. " : string.Empty) +
                    "Ensure a Secret Service provider, such as GNOME Keyring or KWallet, is installed and running on the same D-Bus session bus.";
            }
            else if (serviceUnavailable)
            {
                hint = "A required D-Bus service is unavailable. Ensure your Secret Service provider and its prompting components are installed and running.";
            }
            else if (operation == Operation.Unlock)
            {
                hint = "Unlock the item or its collection using your Secret Service provider before retrying. An unlock prompt may have been dismissed or cancelled.";
            }
            else if (operation == Operation.Store && errorMessage == null)
            {
                hint = "The collection could not be created or unlocked. A Secret Service prompt may have been dismissed or cancelled.";
            }
            else if (operation == Operation.Load && errorMessage == null)
            {
                hint = "No secret value was returned. Ensure the item and its collection are unlocked.";
            }
            else
            {
                hint = null;
            }

            if (hint != null)
            {
                message += Environment.NewLine + hint;
            }

            message += Environment.NewLine + $"See {Constants.HelpUrls.GcmCredentialStores} for more information.";

            Exception innerException = errorMessage == null ? null : new Exception($"{errorDomain}: {errorMessage}");
            return new InteropException(message, errorCode, innerException);
        }

        private static SecretSchema GetSchema()
        {
            var schema = new SecretSchema
            {
                name = SchemaName,
                flags = SECRET_SCHEMA_DONT_MATCH_NAME,
                attributes = new SecretSchemaAttribute[32]
            };

            schema.attributes[0] = new SecretSchemaAttribute
            {
                name = ServiceAttributeName,
                type = SECRET_SCHEMA_ATTRIBUTE_STRING
            };

            schema.attributes[1] = new SecretSchemaAttribute
            {
                name = AccountAttributeName,
                type = SECRET_SCHEMA_ATTRIBUTE_STRING
            };

            schema.attributes[2] = new SecretSchemaAttribute
            {
                name = ExpiresAtAttributeName,
                type = SECRET_SCHEMA_ATTRIBUTE_INTEGER
            };

            return schema;
        }
    }
}
