using System;
using System.Net.Http;
using System.Threading.Tasks;
using GitCredentialManager;
using GitCredentialManager.Authentication.OAuth;
using System.Net.Http.Headers;
using System.Linq;
using GitCredentialManager.Tty;

namespace GitLab
{
    public class GitLabHostProvider : HostProvider
    {
        // https://docs.gitlab.com/ee/integration/oauth_provider.html#authorized-applications
        private static readonly string[] GitLabOAuthScopes =
        {
            "write_repository",
            "read_repository"
        };

        // https://docs.gitlab.com/ee/api/oauth2.html#access-git-over-https-with-access-token
        private const string OAuthUserName = "oauth2";

        private readonly IGitLabAuthentication _gitLabAuth;

        public GitLabHostProvider(ICommandContext context)
            : this(context, new GitLabAuthentication(context)) { }

        public GitLabHostProvider(ICommandContext context, IGitLabAuthentication gitLabAuth)
            : base(context)
        {
            EnsureArgument.NotNull(gitLabAuth, nameof(gitLabAuth));

            _gitLabAuth = gitLabAuth;
        }

        public override string Id => "gitlab";

        public override string Name => "GitLab";

        public override bool IsSupported(GitRequest request)
        {
            if (request is null)
            {
                return false;
            }

            // We do not support unencrypted HTTP communications to GitLab,
            // but we report `true` here for HTTP so that we can show a helpful
            // error message for the user in `CreateCredentialAsync`.
            if (!StringComparer.OrdinalIgnoreCase.Equals(request.Protocol, "http") &&
                !StringComparer.OrdinalIgnoreCase.Equals(request.Protocol, "https"))
            {
                return false;
            }

            if (GitLabConstants.IsGitLabDotCom(request.GetRemoteUri()))
            {
                return true;
            }

            // Split port number and hostname from host request argument
            if (!request.TryGetHostAndPort(out string hostName, out _))
            {
                return false;
            }

            string[] domains = hostName.Split(new char[] { '.' });

            // GitLab[.subdomain].domain.tld
            if (domains.Length >= 3 &&
                StringComparer.OrdinalIgnoreCase.Equals(domains[0], "gitlab"))
            {
                return true;
            }

            if (request.WwwAuth.Any(x => x.Contains("realm=\"GitLab\"")))
            {
                return true;
            }

            return false;
        }

        public override bool IsSupported(HttpResponseMessage response)
        {
            if (response == null)
            {
                return false;
            }

            // as seen at eg. https://salsa.debian.org/apt-team/apt.git
            // not always present https://gitlab.com/gitlab-org/gitlab/-/issues/349464
            return response.Headers.Contains("X-Gitlab-Feature-Category");
        }

        public override async Task<ICredential> GenerateCredentialAsync(GitRequest request)
        {
            ThrowIfDisposed();

            // We should not allow unencrypted communication and should inform the user
            if (!Context.Settings.AllowUnsafeRemotes &&
                StringComparer.OrdinalIgnoreCase.Equals(request.Protocol, "http"))
            {
                throw new Exception(
                    "Unencrypted HTTP is not recommended for GitLab. " +
                    "Ensure the repository remote URL is using HTTPS " +
                    $"or see {Constants.HelpUrls.GcmUnsafeRemotes} about how to allow unsafe remotes.");
            }

            Uri remoteUri = request.GetRemoteUri();

            AuthenticationModes authModes = GetSupportedAuthenticationModes(remoteUri);

            AuthenticationPromptResult promptResult = await _gitLabAuth.GetAuthenticationAsync(remoteUri, request.UserName, authModes);

            switch (promptResult.AuthenticationMode)
            {
                case AuthenticationModes.Basic:
                case AuthenticationModes.Pat:
                    return promptResult.Credential;

                case AuthenticationModes.Browser:
                    return await GenerateOAuthCredentialAsync(request);

                default:
                    throw new ArgumentOutOfRangeException(nameof(promptResult));
            }
        }

        internal AuthenticationModes GetSupportedAuthenticationModes(Uri targetUri)
        {
            // Check for an explicit override for supported authentication modes
            if (Context.Settings.TryGetSetting(
                GitLabConstants.EnvironmentVariables.AuthenticationModes,
                Constants.GitConfiguration.Credential.SectionName, GitLabConstants.GitConfiguration.Credential.AuthenticationModes,
                out string authModesStr))
            {
                if (Enum.TryParse(authModesStr, true, out AuthenticationModes authModes) && authModes != AuthenticationModes.None)
                {
                    Context.Trace.WriteLine($"Supported authentication modes override present: {authModes}");
                    return authModes;
                }
                else
                {
                    Context.Trace.WriteLine($"Invalid value for supported authentication modes override setting: '{authModesStr}'");
                }
            }

            // GitLab.com has well-known supported auth modes
            if (GitLabConstants.IsGitLabDotCom(targetUri))
            {
                return GitLabConstants.DotComAuthenticationModes;
            }

            // Try to detect what auth modes are available for this non-GitLab.com host.
            // Assume that PATs are always available to give at least one option to users!
            var modes = AuthenticationModes.Pat;

            // If there is a configured OAuth client ID (that isn't GitLab.com's client ID)
            // then assume OAuth is possible.
            string oauthClientId = GitLabOAuth2Client.GetClientId(Context.Settings);
            if (!GitLabConstants.IsGitLabDotComClientId(oauthClientId))
            {
                modes |= AuthenticationModes.Browser;
            }
            else
            {
                // Tell the user that they may wish to configure OAuth for this GitLab instance
                Context.Console.WriteWarning(
                    $"missing OAuth configuration for {targetUri.Host} - see {GitLabConstants.HelpUrls.GitLab} for more information");
            }

            // Would like to query password_authentication_enabled_for_git, but can't unless logged in https://gitlab.com/gitlab-org/gitlab/-/issues/349463.
            // For now assume password auth is always available.
            bool supportsBasic = true;
            if (supportsBasic)
            {
                modes |= AuthenticationModes.Basic;
            }

            return modes;
        }

        // <remarks>Stores OAuth tokens as a side effect</remarks>
        public override async Task<GitResponse> GetCredentialAsync(GitRequest request)
        {
            string service = GetServiceName(request);
            StoredCredential storedCredential = Context.CredentialStore.FindFirst(service, request.UserName);
            // Only ask the server about OAuth access tokens that have not already passed their stored expiry
            if (storedCredential?.Key.Account == OAuthUserName &&
                (storedCredential.Metadata.IsExpired ||
                 await IsOAuthTokenExpired(request.GetRemoteUri(), storedCredential.Secret)))
            {
                Context.Trace.WriteLine("Removing expired OAuth access token...");
                Context.CredentialStore.Remove(storedCredential.Key);
                storedCredential = null;
            }

            if (storedCredential != null)
            {
                return new GitResponse(new GitCredential(storedCredential.Key.Account, storedCredential.Secret));
            }

            ICredential credential = null;

            string refreshService = GetRefreshTokenServiceName(request);
            StoredCredential refreshToken = Context.CredentialStore.FindFirst(refreshService, request.UserName);
            if (refreshToken?.Metadata.IsExpired == true)
            {
                // Don't attempt to use a refresh token we know has expired
                Context.Trace.WriteLine("Stored OAuth refresh token has expired.");
                refreshToken = null;
            }

            if (refreshToken != null)
            {
                Context.Trace.WriteLine("Refreshing OAuth token...");
                try
                {
                    credential = await RefreshOAuthCredentialAsync(request, refreshToken.Secret);
                }
                catch (Exception e)
                {
                    Context.Console.WriteError($"OAuth token refresh failed: {e.Message}");
                }
            }

            credential ??= await GenerateCredentialAsync(request);

            return new GitResponse(credential);
        }

        private async Task<bool> IsOAuthTokenExpired(Uri baseUri, string accessToken)
        {
            // https://docs.gitlab.com/ee/api/oauth2.html#retrieve-the-token-information
            Uri infoUri = new Uri(baseUri, "/oauth/token/info");
            using (HttpClient httpClient = Context.HttpClientFactory.CreateClient())
            {
                httpClient.Timeout = TimeSpan.FromSeconds(15);
                httpClient.DefaultRequestHeaders.Authorization
                         = new AuthenticationHeaderValue("Bearer", accessToken);
                try
                {
                    HttpResponseMessage response = await httpClient.GetAsync(infoUri);
                    return response.StatusCode == System.Net.HttpStatusCode.Unauthorized;
                }
                catch (Exception e)
                {
                    Context.Console.WriteError($"OAuth token info request failed: {e.Message}");
                    return false;
                }
            }
        }

        private async Task<GitCredential> GenerateOAuthCredentialAsync(GitRequest request)
        {
            OAuth2TokenResult result = await _gitLabAuth.GetOAuthTokenViaBrowserAsync(request.GetRemoteUri(), GitLabOAuthScopes);
            return StoreOAuthTokens(request, result);
        }

        private async Task<GitCredential> RefreshOAuthCredentialAsync(GitRequest request, string refreshToken)
        {
            OAuth2TokenResult result = await _gitLabAuth.GetOAuthTokenViaRefresh(request.GetRemoteUri(), refreshToken);
            return StoreOAuthTokens(request, result);
        }

        private GitCredential StoreOAuthTokens(GitRequest request, OAuth2TokenResult result)
        {
            // Store the access token now since we know it is valid, whereas Git only asks us to store it
            // once the Git operation has succeeded. The refresh token is stored under a separate service.
            Context.Trace.WriteLine("Pre-emptively storing OAuth access and refresh tokens...");
            Context.CredentialStore.Set(
                new StoredCredential(new CredentialKey(GetServiceName(request), OAuthUserName), result.AccessToken));
            Context.CredentialStore.Set(
                new StoredCredential(new CredentialKey(GetRefreshTokenServiceName(request), OAuthUserName), result.RefreshToken));

            return new GitCredential(OAuthUserName, result.AccessToken);
        }

        protected override void ReleaseManagedResources()
        {
            _gitLabAuth.Dispose();
            base.ReleaseManagedResources();
        }

        private string GetRefreshTokenServiceName(GitRequest request)
        {
            var builder = new UriBuilder(GetServiceName(request));
            builder.Host = "oauth-refresh-token." + builder.Host;
            return builder.Uri.ToString();
        }

        public override Task EraseCredentialAsync(GitRequest request)
        {
            // delete any refresh token too
            Context.CredentialStore.Remove(new CredentialKey(GetRefreshTokenServiceName(request), OAuthUserName));
            return base.EraseCredentialAsync(request);
        }
    }
}
