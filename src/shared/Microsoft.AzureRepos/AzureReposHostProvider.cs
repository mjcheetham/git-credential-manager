using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GitCredentialManager;
using GitCredentialManager.Authentication;
using GitCredentialManager.Commands;
using GitCredentialManager.Tty;
using Spectre.Console;
using KnownGitCfg = GitCredentialManager.Constants.GitConfiguration;

namespace Microsoft.AzureRepos
{
    public class AzureReposHostProvider : DisposableObject, IHostProvider, IConfigurableComponent, ICommandProvider
    {
        private readonly ICommandContext _context;
        private readonly IAzureDevOpsRestApi _azDevOps;
        private readonly IMicrosoftAuthentication _msAuth;
        private readonly IAzureDevOpsAuthorityCache _authorityCache;
        private readonly IAzureReposBindingManager _bindingManager;

        public AzureReposHostProvider(ICommandContext context)
            : this(context, new AzureDevOpsRestApi(context), new MicrosoftAuthentication(context),
                new AzureDevOpsAuthorityCache(context), new AzureReposBindingManager(context))
        {
        }

        public AzureReposHostProvider(ICommandContext context, IAzureDevOpsRestApi azDevOps,
            IMicrosoftAuthentication msAuth, IAzureDevOpsAuthorityCache authorityCache,
            IAzureReposBindingManager bindingManager)
        {
            EnsureArgument.NotNull(context, nameof(context));
            EnsureArgument.NotNull(azDevOps, nameof(azDevOps));
            EnsureArgument.NotNull(msAuth, nameof(msAuth));
            EnsureArgument.NotNull(authorityCache, nameof(authorityCache));
            EnsureArgument.NotNull(bindingManager, nameof(bindingManager));

            _context = context;
            _azDevOps = azDevOps;
            _msAuth = msAuth;
            _authorityCache = authorityCache;
            _bindingManager = bindingManager;
        }

        #region IHostProvider

        public string Id => "azure-repos";

        public string Name => "Azure Repos";

        public IEnumerable<string> SupportedAuthorityIds => MicrosoftAuthentication.AuthorityIds;

        public bool IsSupported(GitRequest request)
        {
            if (request is null)
            {
                return false;
            }

            // We do not recommend unencrypted HTTP communications to Azure Repos,
            // but we report `true` here for HTTP so that we can show a helpful
            // error message for the user in `CreateCredentialAsync`.
            return request.TryGetHostAndPort(out string hostName, out _)
                   && (StringComparer.OrdinalIgnoreCase.Equals(request.Protocol, "http") ||
                       StringComparer.OrdinalIgnoreCase.Equals(request.Protocol, "https")) &&
                   UriHelpers.IsAzureDevOpsHost(hostName);
        }

        public bool IsSupported(HttpResponseMessage response)
        {
            // Azure DevOps Server (TFS) is handled by the generic provider, which supports basic auth, and WIA detection.
            return false;
        }

        public async Task<GitResponse> GetCredentialAsync(GitRequest request)
        {
            if (UseManagedIdentity(out string mid))
            {
                _context.Trace.WriteLine($"Getting Azure Access Token for managed identity {mid}...");
                var azureResult = await _msAuth.GetTokenForManagedIdentityAsync(mid, AzureDevOpsConstants.AzureDevOpsResourceId);
                return new GitResponse(
                    new GitCredential(mid, azureResult.AccessToken)
                );
            }

            if (UseWorkloadFederation(out MicrosoftWorkloadFederationOptions fedOpts))
            {
                _context.Trace.WriteLine($"Getting Azure Access Token using WIF (scenario: {fedOpts.Scenario})...");
                var azureResult = await _msAuth.GetTokenUsingWorkloadFederationAsync(fedOpts, AzureDevOpsConstants.AzureDevOpsDefaultScopes);
                return new GitResponse(
                    new GitCredential(fedOpts.ClientId, azureResult.AccessToken)
                );
            }

            if (UseServicePrincipal(out MicrosoftServicePrincipalIdentity sp))
            {
                _context.Trace.WriteLine($"Getting Azure Access Token for service principal {sp.TenantId}/{sp.Id}...");
                var azureResult = await _msAuth.GetTokenForServicePrincipalAsync(sp, AzureDevOpsConstants.AzureDevOpsDefaultScopes);
                return new GitResponse(
                    new GitCredential(sp.Id, azureResult.AccessToken)
                );
            }

            if (UsePersonalAccessTokens())
            {
                Uri remoteWithUserUri = request.GetRemoteUri(includeUser: true);
                string service = GetServiceName(remoteWithUserUri);
                string account = GetAccountNameForCredentialQuery(request);

                _context.Trace.WriteLine($"Looking for existing credential in store with service={service} account={account}...");

                ICredential credential = _context.CredentialStore.Get(service, account);
                if (credential == null)
                {
                    _context.Trace.WriteLine("No existing credentials found.");

                    // No existing credential was found, create a new one
                    _context.Trace.WriteLine("Creating new credential...");
                    credential = await GeneratePersonalAccessTokenAsync(request);
                    _context.Trace.WriteLine("Credential created.");
                }
                else
                {
                    _context.Trace.WriteLine("Existing credential found.");
                }

                return new GitResponse(credential);
            }
            else
            {
                // Include the username request here so that we may use it as an override
                // for user account lookups when getting Azure Access Tokens.
                var azureResult = await GetAzureAccessTokenAsync(request);
                var azureCredential = new GitCredential(azureResult.Account.UserName, azureResult.AccessToken);
                return new GitResponse(azureCredential);
            }
        }

        public async Task StoreCredentialAsync(GitRequest request)
        {
            Uri remoteUri = request.GetRemoteUri();

            if (UseManagedIdentity(out _))
            {
                _context.Trace.WriteLine("Nothing to store for managed identity authentication.");
            }
            else if (UseWorkloadFederation(out _))
            {
                _context.Trace.WriteLine("Nothing to store for federated identity authentication.");
            }
            else if (UseServicePrincipal(out _))
            {
                _context.Trace.WriteLine("Nothing to store for service principal authentication.");
            }
            else if (UsePersonalAccessTokens())
            {
                string service = GetServiceName(remoteUri);

                // We always store credentials against the given username argument for
                // both vs.com and dev.azure.com-style URLs.
                string account = request.UserName;

                // Add or update the credential in the store.
                _context.Trace.WriteLine($"Storing credential with service={service} account={account}...");
                _context.CredentialStore.AddOrUpdate(service, account, request.Password);
                _context.Trace.WriteLine("Credential was successfully stored.");
            }
            else
            {
                string orgName = UriHelpers.GetOrganizationName(remoteUri);
                _context.Trace.WriteLine($"Recording binding for user {request.UserName} -> organization '{orgName}'...");

                // The incoming user name can be either a UPN or a HomeAccountId — match the cache
                // by either field, then fall back to a `MicrosoftAccount.FromIdentifier`-classified
                // binding when the cache has nothing to say. The binding manager always writes both
                // fields it knows.
                IReadOnlyList<IMicrosoftAccount> cached =
                    await _msAuth.GetUserAccountsAsync(GetClientId(), msaPt: true);
                IMicrosoftAccount match = cached.FirstOrDefault(a =>
                    StringComparer.OrdinalIgnoreCase.Equals(a.UserName, request.UserName) ||
                    StringComparer.Ordinal.Equals(a.HomeAccountId, request.UserName));
                IMicrosoftAccount toBind = match ?? MicrosoftAccount.FromIdentifier(request.UserName);
                _bindingManager.Bind(AzureReposBindingScope.ForOrg(orgName), toBind);
            }
        }

        public Task EraseCredentialAsync(GitRequest request)
        {
            Uri remoteUri = request.GetRemoteUri();

            if (UseManagedIdentity(out _))
            {
                _context.Trace.WriteLine("Nothing to erase for managed identity authentication.");
            }
            else if (UseWorkloadFederation(out _))
            {
                _context.Trace.WriteLine("Nothing to erase for federated identity authentication.");
            }
            else if (UseServicePrincipal(out _))
            {
                _context.Trace.WriteLine("Nothing to erase for service principal authentication.");
            }
            else if (UsePersonalAccessTokens())
            {
                string service = GetServiceName(remoteUri);
                string account = GetAccountNameForCredentialQuery(request);

                // Try to locate an existing credential
                _context.Trace.WriteLine(
                    $"Erasing stored credential in store with service={service} account={account}...");
                if (_context.CredentialStore.Remove(service, account))
                {
                    _context.Trace.WriteLine("Credential was successfully erased.");
                }
                else
                {
                    _context.Trace.WriteLine("No credential was erased.");
                }
            }
            else
            {
                string orgName = UriHelpers.GetOrganizationName(remoteUri);

                _context.Trace.WriteLine($"Clearing binding for organization '{orgName}'...");

                // Remove the most specific binding first; a global binding remains if a local one
                // covered it and only the local one was the cause of the credential failure.
                var localOrg = AzureReposBindingScope.ForOrg(orgName, isLocal: true);
                if (_bindingManager.GetAccount(localOrg) != null)
                {
                    _bindingManager.Unbind(localOrg);
                }
                else
                {
                    _bindingManager.Unbind(AzureReposBindingScope.ForOrg(orgName, isLocal: false));
                }

                // Clear the authority cache in case this was the reason for failure
                _authorityCache.EraseAuthority(orgName);
            }

            return Task.CompletedTask;
        }

        protected override void ReleaseManagedResources()
        {
            _azDevOps.Dispose();
            base.ReleaseManagedResources();
        }

        private void ThrowIfUnsafeRemote(GitRequest request)
        {
            if (!_context.Settings.AllowUnsafeRemotes &&
                StringComparer.OrdinalIgnoreCase.Equals(request.Protocol, "http"))
            {
                throw new Trace2Exception(_context.Trace2,
                    "Unencrypted HTTP is not recommended for Azure Repos. " +
                    "Ensure the repository remote URL is using HTTPS " +
                    $"or see {Constants.HelpUrls.GcmUnsafeRemotes} about how to allow unsafe remotes.");
            }
        }

        private async Task<ICredential> GeneratePersonalAccessTokenAsync(GitRequest request)
        {
            ThrowIfDisposed();
            ThrowIfUnsafeRemote(request);

            Uri remoteUserUri = request.GetRemoteUri(includeUser: true);
            Uri orgUri = UriHelpers.CreateOrganizationUri(remoteUserUri, out _);

            // Determine the MS authentication authority for this organization
            _context.Trace.WriteLine("Determining Microsoft Authentication Authority...");
            string authAuthority = await _azDevOps.GetAuthorityAsync(orgUri);
            _context.Trace.WriteLine($"Authority is '{authAuthority}'.");

            // Get an AAD access token for the Azure DevOps SPS
            _context.Trace.WriteLine("Getting Azure AD access token...");
            IMicrosoftAuthenticationResult result = await _msAuth.GetTokenForUserAsync(
                authAuthority,
                GetClientId(),
                GetRedirectUri(),
                AzureDevOpsConstants.AzureDevOpsDefaultScopes,
                null,
                msaPt: true);
            _context.Trace.WriteLineSecrets(
                $"Acquired Azure access token. Account='{result.Account.UserName}' Token='{{0}}'", new object[] {result.AccessToken});

            // Ask the Azure DevOps instance to create a new PAT
            var patScopes = new[]
            {
                AzureDevOpsConstants.PersonalAccessTokenScopes.ReposWrite,
                AzureDevOpsConstants.PersonalAccessTokenScopes.ArtifactsRead
            };
            _context.Trace.WriteLine($"Creating Azure DevOps PAT with scopes '{string.Join(", ", patScopes)}'...");
            string pat = await _azDevOps.CreatePersonalAccessTokenAsync(
                orgUri,
                result.AccessToken,
                patScopes);
            _context.Trace.WriteLineSecrets("PAT created. PAT='{0}'", new object[] {pat});

            return new GitCredential(result.Account.UserName, pat);
        }

        private async Task<IMicrosoftAuthenticationResult> GetAzureAccessTokenAsync(GitRequest request)
        {
            ThrowIfUnsafeRemote(request);

            Uri remoteWithUserUri = request.GetRemoteUri(includeUser: true);
            string userName = request.UserName;

            Uri orgUri = UriHelpers.CreateOrganizationUri(remoteWithUserUri, out string orgName);

            _context.Trace.WriteLine($"Determining Microsoft Authentication authority for Azure DevOps organization '{orgName}'...");
            if (TryGetAuthorityFromHeaders(request.WwwAuth, out string authAuthority))
            {
                _context.Trace.WriteLine("Authority was found in WWW-Authenticate headers from Git request.");
            }
            else
            {
                // Try to get the authority from the cache
                authAuthority = _authorityCache.GetAuthority(orgName);
                if (authAuthority is null)
                {
                    // If there is no cached value we must query for it and cache it for future use
                    _context.Trace.WriteLine($"No cached authority value - querying {orgUri} for authority...");
                    authAuthority = await _azDevOps.GetAuthorityAsync(orgUri);
                    _authorityCache.UpdateAuthority(orgName, authAuthority);
                }
                else
                {
                    _context.Trace.WriteLine("Authority was found in cache.");
                }
            }

            _context.Trace.WriteLine($"Authority is '{authAuthority}'.");

            //
            // If the remote URI is a classic "*.visualstudio.com" host name and we have a user specified from the
            // remote then take that as the current AAD/MSA user in the first instance.
            //
            // For "dev.azure.com" host names we only use the user info part of the remote when this doesn't
            // match the Azure DevOps organization name. Our friends in Azure DevOps decided "borrow" the username
            // part of the remote URL to include the organization name (not an actual username).
            //
            // The user-info string can be either a UPN or a HomeAccountId —
            // `MicrosoftAccount.FromIdentifier` decides which field of the `IMicrosoftAccount`
            // to populate so msauth's HomeAccountId-first / UPN-fallback resolution sees it in
            // the right slot.
            //
            // If we have no specified user from the remote (or this is org@dev.azure.com/org/..) then query the
            // binding manager for the account to use for this organization, if one is bound. The tenant id
            // (derived from the authority URL) lets us also consider tenant-scoped bindings.
            //
            IMicrosoftAccount account = null;
            var icmp = StringComparer.OrdinalIgnoreCase;
            if (!string.IsNullOrWhiteSpace(userName) &&
                (UriHelpers.IsVisualStudioComHost(remoteWithUserUri.Host) ||
                 (UriHelpers.IsAzureDevOpsHost(remoteWithUserUri.Host) && !icmp.Equals(orgName, userName))))
            {
                _context.Trace.WriteLine("Using username as specified in remote.");
                account = MicrosoftAccount.FromIdentifier(userName);
            }
            else
            {
                _context.Trace.WriteLine($"Resolving account for organization '{orgName}'...");
                string tenantId = TryExtractTenantIdFromAuthority(authAuthority);
                account = _bindingManager.ResolveAccountBinding(orgName, tenantId);
            }

            _context.Trace.WriteLine(account is null
                ? "No bound account; msauth will pick interactively."
                : $"Using account '{account.UserName ?? account.HomeAccountId}'.");

            // Get an AAD access token for the Azure DevOps SPS
            _context.Trace.WriteLine("Getting Azure AD access token...");
            IMicrosoftAuthenticationResult result = await _msAuth.GetTokenForUserAsync(
                authAuthority,
                GetClientId(),
                GetRedirectUri(),
                AzureDevOpsConstants.AzureDevOpsDefaultScopes,
                account,
                msaPt: true);
            _context.Trace.WriteLineSecrets(
                $"Acquired Azure access token. Account='{result.Account.UserName}' Token='{{0}}'", new object[] {result.AccessToken});

            return result;
        }

        /// <summary>
        /// Extract the tenant id from a Microsoft authentication authority URL of the form
        /// <c>https://login.microsoftonline.com/{tenant}[/...]</c>. Returns <see langword="null"/>
        /// for malformed or unrecognized authorities (including the wildcard <c>common</c> and
        /// <c>organizations</c> values, which don't identify a specific tenant).
        /// </summary>
        private static string TryExtractTenantIdFromAuthority(string authority)
        {
            if (string.IsNullOrWhiteSpace(authority)) return null;
            if (!Uri.TryCreate(authority, UriKind.Absolute, out Uri uri)) return null;

            string first = uri.AbsolutePath.Trim('/').Split('/')[0];
            if (string.IsNullOrEmpty(first)) return null;
            if (StringComparer.OrdinalIgnoreCase.Equals(first, "common") ||
                StringComparer.OrdinalIgnoreCase.Equals(first, "organizations") ||
                StringComparer.OrdinalIgnoreCase.Equals(first, "consumers"))
            {
                return null;
            }
            return first;
        }

        internal /* for testing purposes */ static bool TryGetAuthorityFromHeaders(IEnumerable<string> headers, out string authority)
        {
            authority = null;

            if (headers is null)
            {
                return false;
            }

            var regex = new Regex(@"authorization_uri=""?(?<authority>.+)""?", RegexOptions.Compiled | RegexOptions.IgnoreCase);

            foreach (string header in headers)
            {
                Match match = regex.Match(header);
                if (match.Success)
                {
                    authority = match.Groups["authority"].Value.Trim(new[] { '"', '\'' });
                    return true;
                }
            }

            return false;
        }

        private string GetClientId()
        {
            // Check for developer override value
            if (_context.Settings.TryGetSetting(
                    AzureDevOpsConstants.EnvironmentVariables.DevAadClientId,
                    Constants.GitConfiguration.Credential.SectionName,
                    AzureDevOpsConstants.GitConfiguration.Credential.DevAadClientId,
                    out string clientId))
            {
                return clientId;
            }

            return AzureDevOpsConstants.AadClientId;
        }

        private Uri GetRedirectUri()
        {
            // Check for developer override value
            if (_context.Settings.TryGetSetting(
                    AzureDevOpsConstants.EnvironmentVariables.DevAadRedirectUri,
                    Constants.GitConfiguration.Credential.SectionName, AzureDevOpsConstants.GitConfiguration.Credential.DevAadRedirectUri,
                    out string redirectUriStr) &&
                Uri.TryCreate(redirectUriStr, UriKind.Absolute, out Uri redirectUri))
            {
                return redirectUri;
            }

            return AzureDevOpsConstants.AadRedirectUri;
        }

        /// <remarks>
        /// For dev.azure.com-style URLs we use the path arg to get the Azure DevOps organization name.
        /// We ensure the presence of the path arg by setting credential.useHttpPath = true at install time.
        ///
        /// The result of this workaround is that we are now unable to determine if the user wanted to store
        /// credentials with the full path or not for dev.azure.com-style URLs.
        ///
        /// Rather than always assume we're storing credentials against the full path, and therefore resulting
        /// in an personal access token being created per remote URL/repository, we never store against
        /// the full path and always store with the organization URL "dev.azure.com/org".
        ///
        /// For visualstudio.com-style URLs we know the AzDevOps organization name from the host arg, and
        /// don't set the useHttpPath option. This means if we get the full path for a vs.com-style URL
        /// we can store against the full remote path (the intended design).
        ///
        /// Users that need to clone a repository from Azure Repos against the full path therefore must
        /// use the vs.com-style remote URL and not the dev.azure.com one.
        /// </remarks>
        private static string GetServiceName(Uri remoteUri)
        {
            // dev.azure.com
            if (UriHelpers.IsDevAzureComHost(remoteUri.Host))
            {
                // We can never store the new dev.azure.com-style URLs against the full path because
                // we have forced the useHttpPath option to true to in order to retrieve the AzDevOps
                // organization name from Git.
                return UriHelpers.CreateOrganizationUri(remoteUri, out _).AbsoluteUri.TrimEnd('/');
            }

            // *.visualstudio.com
            if (UriHelpers.IsVisualStudioComHost(remoteUri.Host))
            {
                // If we're given the full path for an older *.visualstudio.com-style URL then we should
                // respect that in the service name.
                return remoteUri.WithoutUserInfo().AbsoluteUri.TrimEnd('/');
            }

            throw new InvalidOperationException("Host is not Azure DevOps.");
        }

        private static string GetAccountNameForCredentialQuery(GitRequest request)
        {
            if (!request.TryGetHostAndPort(out string hostName, out _))
            {
                throw new InvalidOperationException("Failed to parse host name and/or port");
            }

            // dev.azure.com
            if (UriHelpers.IsDevAzureComHost(hostName))
            {
                // We ignore the given username for dev.azure.com-style URLs because AzDevOps recommends
                // adding the organization name as the user in the remote URL (resulting in URLs like
                // https://org@dev.azure.com/org/foo/_git/bar) and we don't know if the given username
                // is an actual username, or the org name.
                // Use `null` as the account name so we match all possible credentials (regardless of
                // the account).
                return null;
            }

            // *.visualstudio.com
            if (UriHelpers.IsVisualStudioComHost(hostName))
            {
                // If we're given a username for the vs.com-style URLs we can and should respect any
                // specified username in the remote URL/request arguments.
                return request.UserName;
            }

            throw new InvalidOperationException("Host is not Azure DevOps.");
        }

        /// <summary>
        /// Check if Azure DevOps Personal Access Tokens should be used or not.
        /// </summary>
        /// <returns>True if Personal Access Tokens should be used, false otherwise.</returns>
        private bool UsePersonalAccessTokens()
        {
            // Default to using PATs except on DevBox where we prefer OAuth tokens
            bool defaultValue = !PlatformUtils.IsDevBox();

            if (_context.Settings.TryGetSetting(
                AzureDevOpsConstants.EnvironmentVariables.CredentialType,
                KnownGitCfg.Credential.SectionName,
                AzureDevOpsConstants.GitConfiguration.Credential.CredentialType,
                out string valueStr))
            {
                _context.Trace.WriteLine($"Azure Repos credential type override set to '{valueStr}'");

                switch (valueStr.ToLowerInvariant())
                {
                    case AzureDevOpsConstants.PatCredentialType:
                        return true;

                    case AzureDevOpsConstants.OAuthCredentialType:
                        return false;

                    default:
                        _context.Console.WriteWarning($"unknown Azure Repos credential type '{valueStr}' - using PATs");
                        return defaultValue;
                }
            }

            return defaultValue;
        }

        private bool UseServicePrincipal(out MicrosoftServicePrincipalIdentity sp)
        {
            if (!_context.Settings.TryGetSetting(
                    AzureDevOpsConstants.EnvironmentVariables.ServicePrincipalId,
                    Constants.GitConfiguration.Credential.SectionName,
                    AzureDevOpsConstants.GitConfiguration.Credential.ServicePrincipal,
                    out string spStr) || string.IsNullOrWhiteSpace(spStr))
            {
                sp = null;
                return false;
            }

            string[] split = spStr.Split(new[] { '/' }, count: 2);

            if (split.Length < 1 || string.IsNullOrWhiteSpace(split[0]))
            {
                _context.Console.WriteError("unable to use configured service principal - missing tenant ID in configuration");
                sp = null;
                return false;
            }

            if (split.Length < 2 || string.IsNullOrWhiteSpace(split[1]))
            {
                _context.Console.WriteError("unable to use configured service principal - missing client ID in configuration");
                sp = null;
                return false;
            }

            string tenantId = split[0];
            string clientId = split[1];

            sp = new MicrosoftServicePrincipalIdentity
            {
                Id = clientId,
                TenantId = tenantId,
            };

            bool hasClientSecret = _context.Settings.TryGetSetting(
                AzureDevOpsConstants.EnvironmentVariables.ServicePrincipalSecret,
                Constants.GitConfiguration.Credential.SectionName,
                AzureDevOpsConstants.GitConfiguration.Credential.ServicePrincipalSecret,
                out string clientSecret);

            bool hasCertThumbprint = _context.Settings.TryGetSetting(
                AzureDevOpsConstants.EnvironmentVariables.ServicePrincipalCertificateThumbprint,
                Constants.GitConfiguration.Credential.SectionName,
                AzureDevOpsConstants.GitConfiguration.Credential.ServicePrincipalCertificateThumbprint,
                out string certThumbprint);

            if (hasCertThumbprint && hasClientSecret)
            {
                _context.Console.WriteWarning("both service principal client secret and certificate thumbprint are configured - using certificate");
            }

            if (hasCertThumbprint)
            {
                sp.SendX5C = _context.Settings.TryGetSetting(
                    AzureDevOpsConstants.EnvironmentVariables.ServicePrincipalCertificateSendX5C,
                    Constants.GitConfiguration.Credential.SectionName,
                    AzureDevOpsConstants.GitConfiguration.Credential.ServicePrincipalCertificateSendX5C,
                    out string certHasX5CStr) && certHasX5CStr.ToBooleanyOrDefault(false);

                X509Certificate2 cert = X509Utils.GetCertificateByThumbprint(certThumbprint);
                if (cert is null)
                {
                    _context.Console.WriteError($"unable to find certificate with thumbprint '{certThumbprint}' for service principal");
                    return false;
                }

                sp.Certificate = cert;
            }
            else if (hasClientSecret)
            {
                sp.ClientSecret = clientSecret;
            }

            return true;
        }

        private bool UseManagedIdentity(out string mid)
        {
            return _context.Settings.TryGetSetting(
                       AzureDevOpsConstants.EnvironmentVariables.ManagedIdentity,
                       KnownGitCfg.Credential.SectionName,
                       AzureDevOpsConstants.GitConfiguration.Credential.ManagedIdentity,
                       out mid) &&
                   !string.IsNullOrWhiteSpace(mid);
        }

        private bool UseWorkloadFederation(out MicrosoftWorkloadFederationOptions fedOpts)
        {
            if (!_context.Settings.TryGetSetting(
                    AzureDevOpsConstants.EnvironmentVariables.WorkloadFederation,
                    Constants.GitConfiguration.Credential.SectionName,
                    AzureDevOpsConstants.GitConfiguration.Credential.WorkloadFederation,
                    out string wifStr))
            {
                fedOpts = null;
                return false;
            }

            MicrosoftWorkloadFederationScenario scenario;
            switch (wifStr.ToLowerInvariant())
            {
                case "generic":
                    scenario = MicrosoftWorkloadFederationScenario.Generic;
                    break;

                case "mi":
                case "managedidentity":
                    scenario = MicrosoftWorkloadFederationScenario.ManagedIdentity;
                    break;

                case "github":
                case "githubactions":
                    scenario = MicrosoftWorkloadFederationScenario.GitHubActions;
                    break;

                default: // Unknown scenario value
                    fedOpts = null;
                    return false;
            }

            bool hasClientId = _context.Settings.TryGetSetting(
                AzureDevOpsConstants.EnvironmentVariables.WorkloadFederationClientId,
                Constants.GitConfiguration.Credential.SectionName,
                AzureDevOpsConstants.GitConfiguration.Credential.WorkloadFederationClientId,
                out string clientId);

            bool hasTenantId = _context.Settings.TryGetSetting(
                AzureDevOpsConstants.EnvironmentVariables.WorkloadFederationTenantId,
                Constants.GitConfiguration.Credential.SectionName,
                AzureDevOpsConstants.GitConfiguration.Credential.WorkloadFederationTenantId,
                out string tenantId);

            if (!hasClientId || !hasTenantId)
            {
                _context.Console.WriteError("both client ID and tenant ID are required for workload federation");
                fedOpts = null;
                return false;
            }

            // Audience is optional - the default is "api://AzureADTokenExchange"
            if (!_context.Settings.TryGetSetting(
                    AzureDevOpsConstants.EnvironmentVariables.WorkloadFederationAudience,
                    Constants.GitConfiguration.Credential.SectionName,
                    AzureDevOpsConstants.GitConfiguration.Credential.WorkloadFederationAudience,
                    out string audience) || string.IsNullOrWhiteSpace(audience))
            {
                audience = MicrosoftWorkloadFederationOptions.DefaultAudience;
            }

            fedOpts = new MicrosoftWorkloadFederationOptions
            {
                Scenario = scenario,
                ClientId = clientId,
                TenantId = tenantId,
                Audience = audience
            };

            switch (scenario)
            {
                case MicrosoftWorkloadFederationScenario.Generic:
                    if (!_context.Settings.TryGetSetting(
                            AzureDevOpsConstants.EnvironmentVariables.WorkloadFederationAssertion,
                            Constants.GitConfiguration.Credential.SectionName,
                            AzureDevOpsConstants.GitConfiguration.Credential.WorkloadFederationAssertion,
                            out string assertion) || string.IsNullOrWhiteSpace(assertion))
                    {
                        _context.Console.WriteError("assertion is required for the generic workload federation scenario");
                        fedOpts = null;
                        return false;
                    }

                    // Check if this value points to a file containing the actual assertion (file://<path>)
                    if (Uri.TryCreate(assertion, UriKind.Absolute, out Uri assertionUri)
                        && StringComparer.OrdinalIgnoreCase.Equals(assertionUri.Scheme, "file"))
                    {
                        string filePath = assertionUri.LocalPath;
                        if (!_context.FileSystem.FileExists(filePath))
                        {
                            _context.Console.WriteError($"assertion file not found: {filePath}");
                            fedOpts = null;
                            return false;
                        }

                        _context.Trace.WriteLine($"Reading workload federation assertion from file '{filePath}'...");
                        assertion = _context.FileSystem.ReadAllText(filePath).Trim();
                        if (string.IsNullOrWhiteSpace(assertion))
                        {
                            _context.Console.WriteError($"assertion file is empty: {filePath}");
                            fedOpts = null;
                            return false;
                        }
                    }

                    fedOpts.GenericClientAssertion = assertion;
                    break;

                case MicrosoftWorkloadFederationScenario.ManagedIdentity:
                    if (!_context.Settings.TryGetSetting(
                            AzureDevOpsConstants.EnvironmentVariables.WorkloadFederationManagedIdentity,
                            Constants.GitConfiguration.Credential.SectionName,
                            AzureDevOpsConstants.GitConfiguration.Credential.WorkloadFederationManagedIdentity,
                            out string managedIdentity) || string.IsNullOrWhiteSpace(managedIdentity))
                    {
                        _context.Console.WriteError("managed identity is required for the managed identity workload federation scenario");
                        fedOpts = null;
                        return false;
                    }

                    fedOpts.ManagedIdentityId = managedIdentity;
                    break;

                case MicrosoftWorkloadFederationScenario.GitHubActions:
                    if (!_context.Environment.Variables.TryGetValue(
                            Constants.EnvironmentVariables.GitHubActionsTokenRequestUrl, out string tokenRequestUrl)
                        || !Uri.TryCreate(tokenRequestUrl, UriKind.Absolute, out Uri tokenRequestUri))
                    {
                        _context.Console.WriteError(
                            "unable to get valid token request URL from environment variable for the GitHub Actions workload federation scenario");
                        fedOpts = null;
                        return false;
                    }

                    if (!_context.Environment.Variables.TryGetValue(
                            Constants.EnvironmentVariables.GitHubActionsTokenRequestToken, out string tokenRequestToken)
                        || string.IsNullOrWhiteSpace(tokenRequestToken))
                    {
                        _context.Console.WriteError(
                            "unable to get valid token request token from environment variable for the GitHub Actions workload federation scenario");
                        fedOpts = null;
                        return false;
                    }

                    fedOpts.GitHubTokenRequestUrl = tokenRequestUri;
                    fedOpts.GitHubTokenRequestToken = tokenRequestToken;
                    break;
            }

            return true;
        }

        #endregion

        #region IConfigurationComponent

        string IConfigurableComponent.Name => "Azure Repos provider";

        public Task ConfigureAsync(ConfigurationTarget target)
        {
            string useHttpPathKey = $"{KnownGitCfg.Credential.SectionName}.https://dev.azure.com.{KnownGitCfg.Credential.UseHttpPath}";

            GitConfigurationLevel configurationLevel = target == ConfigurationTarget.System
                ? GitConfigurationLevel.System
                : GitConfigurationLevel.Global;

            IGitConfiguration targetConfig = _context.Git.GetConfiguration();

            if (targetConfig.TryGet(useHttpPathKey, false, out string currentValue) && currentValue.IsTruthy())
            {
                _context.Trace.WriteLine("Git configuration 'credential.useHttpPath' is already set to 'true' for https://dev.azure.com.");
            }
            else
            {
                _context.Trace.WriteLine("Setting Git configuration 'credential.useHttpPath' to 'true' for https://dev.azure.com...");
                targetConfig.Set(configurationLevel, useHttpPathKey, "true");
            }

            return Task.CompletedTask;
        }

        public Task UnconfigureAsync(ConfigurationTarget target)
        {
            string helperKey = $"{Constants.GitConfiguration.Credential.SectionName}.{Constants.GitConfiguration.Credential.Helper}";
            string useHttpPathKey = $"{KnownGitCfg.Credential.SectionName}.https://dev.azure.com.{KnownGitCfg.Credential.UseHttpPath}";

            _context.Trace.WriteLine("Clearing Git configuration 'credential.useHttpPath' for https://dev.azure.com...");

            GitConfigurationLevel configurationLevel = target == ConfigurationTarget.System
                ? GitConfigurationLevel.System
                : GitConfigurationLevel.Global;

            IGitConfiguration targetConfig = _context.Git.GetConfiguration();

            // On Windows, if there is a "manager" or "manager-core" entry remaining in the system config then we must
            // not clear the useHttpPath option otherwise this would break the bundled version of GCM in Git for Windows.
            if (!PlatformUtils.IsWindows() || target != ConfigurationTarget.System ||
                targetConfig.GetAll(helperKey).All(x => !string.Equals(x, "manager") && !string.Equals(x, "manager-core")))
            {
                targetConfig.Unset(configurationLevel, useHttpPathKey);
            }

            return Task.CompletedTask;
        }

        #endregion

        #region ICommandProvider

        ProviderCommand ICommandProvider.CreateCommand()
        {
            //
            // clear-cache
            //
            var clearCacheCmd = new Command("clear-cache", "Clear the Azure authority cache");
            clearCacheCmd.SetHandler(ClearCacheCmd);

            //
            // login [--tenant <id|domain>]
            //
            var loginCmd = new Command("login", "Sign in to a Microsoft account and add it to the credential cache");
            var loginTenantOpt = new Option<string>("--tenant", "Sign in to a specific Microsoft Entra tenant (GUID or domain). Required when adding a guest account whose home tenant is different from the tenant you want to use it for.");
            loginCmd.AddOption(loginTenantOpt);
            loginCmd.SetHandler(LoginCmd, loginTenantOpt);

            //
            // logout (<account> | --all)
            //
            var logoutCmd = new Command("logout", "Remove a Microsoft account from the credential cache");
            var logoutAccountArg = new Argument<string>("account", "Account to remove (UPN or HomeAccountId)")
            {
                Arity = ArgumentArity.ZeroOrOne
            };
            var logoutAllOpt = new Option<bool>("--all", "Remove every cached Microsoft account");
            logoutCmd.AddArgument(logoutAccountArg);
            logoutCmd.AddOption(logoutAllOpt);
            logoutCmd.SetHandler(LogoutCmd, logoutAccountArg, logoutAllOpt);

            //
            // list
            //
            var listCmd = new Command("list", "List Microsoft accounts in the credential cache");
            listCmd.SetHandler(ListCmd);

            //
            // list-bindings [<organization>] [--show-remotes] [--verbose]
            //
            var listBindingsCmd = new Command("list-bindings", "List all user account bindings");
            var orgFilterArg = new Argument<string>("organization", "(optional) Filter results by Azure DevOps organization name")
            {
                Arity = ArgumentArity.ZeroOrOne
            };
            var remoteOpt = new Option<bool>("--show-remotes")
            {
                Description = "Also show Azure DevOps remote user bindings for the current repository"
            };
            var verboseOpt = new Option<bool>(new[] { "--verbose", "-v" }, "Verbose output - show remote URLs");
            listBindingsCmd.AddArgument(orgFilterArg);
            listBindingsCmd.AddOption(remoteOpt);
            listBindingsCmd.AddOption(verboseOpt);
            listBindingsCmd.SetHandler(ListBindingsCmd, orgFilterArg, remoteOpt, verboseOpt);

            //
            // bind <account> [--tenant <id> | --org <name>] [--local]
            //
            var bindCmd = new Command("bind", "Bind a Microsoft account to a tenant or Azure DevOps organization");
            var bindAccountArg = new Argument<string>("account", "Account to bind (UPN or HomeAccountId of an account from `azure-repos list`)")
            {
                Arity = ArgumentArity.ExactlyOne
            };
            var bindTenantOpt = new Option<string>("--tenant", "Bind for any organization backed by the given Microsoft Entra tenant");
            var bindOrgOpt = new Option<string>("--org", "Bind for the given Azure DevOps organization");
            var localOpt = new Option<bool>("--local", "Target the local repository Git configuration");
            bindCmd.AddArgument(bindAccountArg);
            bindCmd.AddOption(bindTenantOpt);
            bindCmd.AddOption(bindOrgOpt);
            bindCmd.AddOption(localOpt);
            bindCmd.SetHandler(BindCmd, bindAccountArg, bindTenantOpt, bindOrgOpt, localOpt);

            //
            // unbind [--tenant <id> | --org <name>] [--local]
            //
            var unbindCmd = new Command("unbind", "Remove a Microsoft account binding for a tenant or Azure DevOps organization");
            var unbindTenantOpt = new Option<string>("--tenant", "Remove the binding for the given Microsoft Entra tenant");
            var unbindOrgOpt = new Option<string>("--org", "Remove the binding for the given Azure DevOps organization");
            unbindCmd.AddOption(unbindTenantOpt);
            unbindCmd.AddOption(unbindOrgOpt);
            unbindCmd.AddOption(localOpt);
            unbindCmd.SetHandler(UnbindCmd, unbindTenantOpt, unbindOrgOpt, localOpt);

            var rootCmd = new ProviderCommand(this);
            rootCmd.AddCommand(loginCmd);
            rootCmd.AddCommand(logoutCmd);
            rootCmd.AddCommand(listCmd);
            rootCmd.AddCommand(listBindingsCmd);
            rootCmd.AddCommand(bindCmd);
            rootCmd.AddCommand(unbindCmd);
            rootCmd.AddCommand(clearCacheCmd);
            return rootCmd;
        }

        private void ClearCacheCmd()
        {
            _authorityCache.Clear();
            _context.Console.WriteLine("Authority cache cleared");
        }

        private async Task<int> LoginCmd(string tenantId)
        {
            // Pick the authority MSAL signs in against. By default we use the wildcard
            // `organizations` authority so the user can pick any work/school account; an
            // explicit --tenant constrains to one tenant (the only way to pre-stage a
            // guest-account record for a non-home tenant).
            string authority = !string.IsNullOrWhiteSpace(tenantId)
                ? $"{AzureDevOpsConstants.AadAuthorityBaseUrl}/{tenantId}"
                : $"{AzureDevOpsConstants.AadAuthorityBaseUrl}/organizations";

            IMicrosoftAuthenticationResult result;
            try
            {
                result = await _msAuth.GetTokenForUserAsync(
                    authority,
                    GetClientId(),
                    GetRedirectUri(),
                    AzureDevOpsConstants.AzureDevOpsDefaultScopes,
                    account: null,
                    msaPt: true);
            }
            catch (Exception ex)
            {
                _context.Console.WriteError($"sign-in failed: {ex.Message}");
                return -1;
            }

            if (result.Account is null || string.IsNullOrWhiteSpace(result.Account.HomeAccountId))
            {
                _context.Console.WriteError(
                    "sign-in succeeded but no account identifier was returned");
                return -1;
            }

            _context.Console.WriteLine($"Signed in as {result.Account.UserName}.");
            return 0;
        }

        private async Task<int> LogoutCmd(string account, bool all)
        {
            bool hasAccount = !string.IsNullOrWhiteSpace(account);
            if (all == hasAccount)
            {
                _context.Console.WriteError("specify either <account> or --all");
                return -1;
            }

            IReadOnlyList<IMicrosoftAccount> cached =
                await _msAuth.GetUserAccountsAsync(GetClientId(), msaPt: true);

            if (cached.Count == 0)
            {
                _context.Console.WriteLine("No accounts cached.");
                return 0;
            }

            IEnumerable<IMicrosoftAccount> targets;
            if (all)
            {
                targets = cached;
            }
            else
            {
                IMicrosoftAccount[] matches = cached.Where(a =>
                        StringComparer.OrdinalIgnoreCase.Equals(a.UserName, account) ||
                        StringComparer.Ordinal.Equals(a.HomeAccountId, account))
                    .ToArray();
                if (matches.Length == 0)
                {
                    _context.Console.WriteError($"no cached account matches '{account}'");
                    return -1;
                }
                if (matches.Length > 1)
                {
                    _context.Console.WriteError(
                        $"'{account}' is ambiguous; specify the HomeAccountId of the account to remove:");
                    foreach (IMicrosoftAccount m in matches)
                    {
                        _context.Console.WriteLine($"  {m.UserName}  ({m.HomeAccountId})");
                    }
                    return -1;
                }
                targets = matches;
            }

            int removed = 0;
            foreach (IMicrosoftAccount target in targets)
            {
                if (await _msAuth.RemoveUserAccountAsync(GetClientId(), target, msaPt: true))
                {
                    _context.Console.WriteLine($"Signed out {target.UserName}.");
                    removed++;
                }
            }

            return removed > 0 ? 0 : -1;
        }

        private async Task<int> ListCmd()
        {
            IReadOnlyList<IMicrosoftAccount> cached =
                await _msAuth.GetUserAccountsAsync(GetClientId(), msaPt: true);

            if (cached.Count == 0)
            {
                _context.Console.WriteLine("No accounts cached.");
                return 0;
            }

            foreach (IMicrosoftAccount account in cached
                         .OrderBy(a => a.UserName ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            {
                _context.Console.WriteLine(account.UserName ?? "(unknown)");
                _context.Console.WriteLine($"  {account.HomeAccountId}");
            }

            return 0;
        }

        private class RemoteBinding
        {
            public string Remote { get; set; }
            public bool IsPush { get; set; }
            public Uri Uri { get; set; }
        }

        private async Task<int> ListBindingsCmd(string organization, bool showRemotes, bool verbose)
        {
            // Group bindings into per-scope-key buckets so we can render one heading per
            // tenant/org with its global and local bindings beneath.
            var byHeading = new SortedDictionary<string, (IMicrosoftAccount Global, IMicrosoftAccount Local)>(StringComparer.OrdinalIgnoreCase);
            foreach (AzureReposBinding b in _bindingManager.GetBindings())
            {
                string heading = b.Scope switch
                {
                    AzureReposBindingScope.Org o    => $"dev.azure.com/{o.OrgName}",
                    AzureReposBindingScope.Tenant t => $"login.microsoftonline.com/{t.TenantId}",
                    _ => null,
                };
                if (heading is null) continue;
                if (!string.IsNullOrWhiteSpace(organization) &&
                    b.Scope is AzureReposBindingScope.Org filterOrg &&
                    !StringComparer.OrdinalIgnoreCase.Equals(filterOrg.OrgName, organization))
                {
                    continue;
                }
                if (!byHeading.TryGetValue(heading, out var pair)) pair = (null, null);
                if (b.Scope.IsLocal) pair.Local = b.Account; else pair.Global = b.Account;
                byHeading[heading] = pair;
            }

            // Cache MSAL accounts once so we can enrich legacy `.accountid`-only bindings with
            // a UPN for display. New bindings carry both fields and don't need this lookup.
            IReadOnlyList<IMicrosoftAccount> cached =
                await _msAuth.GetUserAccountsAsync(GetClientId(), msaPt: true);
            var upnByAccountId = cached.ToDictionary(a => a.HomeAccountId, a => a.UserName, StringComparer.OrdinalIgnoreCase);
            string Display(IMicrosoftAccount a)
            {
                if (a is null) return null;
                if (!string.IsNullOrWhiteSpace(a.UserName)) return a.UserName;
                return upnByAccountId.TryGetValue(a.HomeAccountId, out string upn) ? upn : a.HomeAccountId;
            }

            var orgRemotes = new Dictionary<string, ICollection<RemoteBinding>>();
            if (showRemotes)
            {
                if (!_context.Git.IsInsideRepository())
                {
                    _context.Console.WriteWarning("not inside a git repository (--show-remotes has no effect)");
                }

                static bool IsAzureDevOpsHttpRemote(string url, out Uri uri)
                {
                    return Uri.TryCreate(url, UriKind.Absolute, out uri) &&
                           (StringComparer.OrdinalIgnoreCase.Equals(Uri.UriSchemeHttp, uri.Scheme) ||
                            StringComparer.OrdinalIgnoreCase.Equals(Uri.UriSchemeHttps, uri.Scheme)) &&
                           UriHelpers.IsAzureDevOpsHost(uri.Host);
                }

                foreach (GitRemote remote in _context.Git.GetRemotes())
                {
                    if (IsAzureDevOpsHttpRemote(remote.FetchUrl, out Uri fetchUri))
                    {
                        string fetchOrg = UriHelpers.GetOrganizationName(fetchUri);
                        orgRemotes.Append($"dev.azure.com/{fetchOrg}",
                            new RemoteBinding { IsPush = false, Remote = remote.Name, Uri = fetchUri });
                    }
                    if (IsAzureDevOpsHttpRemote(remote.PushUrl, out Uri pushUri))
                    {
                        string pushOrg = UriHelpers.GetOrganizationName(pushUri);
                        orgRemotes.Append($"dev.azure.com/{pushOrg}",
                            new RemoteBinding { IsPush = true, Remote = remote.Name, Uri = pushUri });
                    }
                }
            }

            var headings = new SortedSet<string>(byHeading.Keys, StringComparer.OrdinalIgnoreCase);
            headings.UnionWith(orgRemotes.Keys);

            var icmp = StringComparer.OrdinalIgnoreCase;
            foreach (string heading in headings)
            {
                _context.Console.WriteLine($"{heading}:");

                if (byHeading.TryGetValue(heading, out var pair))
                {
                    if (pair.Global != null)
                    {
                        _context.Console.WriteLine($"  (global) -> {Display(pair.Global)}");
                    }
                    if (pair.Local != null)
                    {
                        _context.Console.WriteLine($"  (local)  -> {Display(pair.Local)}");
                    }
                }

                if (!orgRemotes.TryGetValue(heading, out var remotes)) continue;

                IEnumerable<IGrouping<string, RemoteBinding>> byRemote = remotes.GroupBy(r => r.Remote);
                string orgForRemote = heading.StartsWith("dev.azure.com/", StringComparison.OrdinalIgnoreCase)
                    ? heading.Substring("dev.azure.com/".Length)
                    : null;
                foreach (var group in byRemote)
                {
                    _context.Console.WriteLine($"  {group.Key}:");
                    foreach (RemoteBinding rb in group)
                    {
                        // dev.azure.com URLs use the user-info slot to carry the org name; ignore that
                        // pseudo-user when reporting the remote's preferred account.
                        if (!rb.Uri.TryGetUserInfo(out string remoteUser, out _) ||
                            (UriHelpers.IsDevAzureComHost(rb.Uri.Host) &&
                             orgForRemote != null && icmp.Equals(remoteUser, orgForRemote)))
                        {
                            remoteUser = "(inherit)";
                        }

                        string url = verbose ? $"{rb.Uri.WithoutUserInfo()} " : null;
                        _context.Console.WriteLine(rb.IsPush
                            ? $"    {url}(push)  -> {remoteUser}"
                            : $"    {url}(fetch) -> {remoteUser}");
                    }
                }
            }

            return 0;
        }

        private async Task<int> BindCmd(string account, string tenantId, string orgName, bool local)
        {
            if (!TryParseBindingScope(tenantId, orgName, local, out AzureReposBindingScope scope, out string error))
            {
                _context.Console.WriteError(error);
                return -1;
            }

            if (string.IsNullOrWhiteSpace(account))
            {
                _context.Console.WriteError("account is required");
                return -1;
            }

            // Look the account up in the MSAL cache. If found, bind the cached account directly
            // — it carries both a UPN and a stable HomeAccountId. If not found we warn but still
            // record a UPN-or-HomeAccountId binding from whatever the user supplied; one classify
            // here decides which field of the IMicrosoftAccount we populate.
            IReadOnlyList<IMicrosoftAccount> cached =
                await _msAuth.GetUserAccountsAsync(GetClientId(), msaPt: true);
            IMicrosoftAccount match = cached.FirstOrDefault(a =>
                StringComparer.OrdinalIgnoreCase.Equals(a.UserName, account) ||
                StringComparer.Ordinal.Equals(a.HomeAccountId, account));
            IMicrosoftAccount toBind;
            if (match != null)
            {
                toBind = match;
            }
            else
            {
                _context.Console.WriteError(
                    $"'{account}' is not in the MSAL cache. Recording the binding anyway; "
                    + "run `azure-repos login` first to sign in.");
                toBind = MicrosoftAccount.FromIdentifier(account);
            }

            _bindingManager.Bind(scope, toBind);
            return 0;
        }

        private Task<int> UnbindCmd(string tenantId, string orgName, bool local)
        {
            if (!TryParseBindingScope(tenantId, orgName, local, out AzureReposBindingScope scope, out string error))
            {
                _context.Console.WriteError(error);
                return Task.FromResult(-1);
            }

            _bindingManager.Unbind(scope);
            return Task.FromResult(0);
        }

        private bool TryParseBindingScope(
            string tenantId, string orgName, bool local,
            out AzureReposBindingScope scope, out string error)
        {
            scope = null;
            error = null;

            int specified = (string.IsNullOrWhiteSpace(tenantId) ? 0 : 1)
                          + (string.IsNullOrWhiteSpace(orgName) ? 0 : 1);
            if (specified != 1)
            {
                error = "specify exactly one of --tenant or --org";
                return false;
            }

            if (local && !_context.Git.IsInsideRepository())
            {
                error = "not inside a git repository (cannot use --local)";
                return false;
            }

            scope = !string.IsNullOrWhiteSpace(tenantId)
                ? AzureReposBindingScope.ForTenant(tenantId, local)
                : AzureReposBindingScope.ForOrg(orgName, local);
            return true;
        }

        #endregion
    }
}
