using Microsoft.Identity.Client;

namespace GitCredentialManager.Authentication.Entra;

public partial class EntraAuthentication : AuthenticationBase, IEntraAuthentication
{
    private readonly IMsalHttpClientFactory _httpFactory;

    public EntraAuthentication(ICommandContext context)
        : base(context)
    {
        _httpFactory = new MsalHttpClientFactoryAdaptor(context.HttpClientFactory);
    }

    private class AuthResult : IEntraAuthenticationResult
    {
        private AuthResult() { }

        public static IEntraAuthenticationResult FromMsalResult(AuthenticationResult result) =>
            new AuthResult
            {
                AccessToken = result.AccessToken,
            };

        public string AccessToken { get; private init; }
    }
}
