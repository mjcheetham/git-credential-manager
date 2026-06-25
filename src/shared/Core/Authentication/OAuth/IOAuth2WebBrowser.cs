using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace GitCredentialManager.Authentication.OAuth
{
    public interface IOAuth2WebBrowser
    {
        Uri UpdateRedirectUri(Uri uri);

        Task<Uri> GetAuthenticationCodeAsync(Uri authorizationUri, Uri redirectUri, CancellationToken ct);
    }

    public abstract class OAuth2WebBrowser : IOAuth2WebBrowser
    {
        public abstract Task<Uri> GetAuthenticationCodeAsync(Uri authorizationUri, Uri redirectUri, CancellationToken ct);

        public virtual Uri UpdateRedirectUri(Uri uri)
        {
            if (!uri.IsLoopback)
            {
                throw new ArgumentException("Only localhost is supported as a redirect URI.", nameof(uri));
            }

            // If a port has been specified use it, otherwise find a free one
            if (uri.IsDefaultPort)
            {
                int port = OAuth2SystemWebBrowser.GetFreeTcpPort();
                return new UriBuilder(uri) {Port = port}.Uri;
            }

            return uri;
        }

        private static int GetFreeTcpPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);

            try
            {
                listener.Start();
                return ((IPEndPoint) listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }
    }
}
