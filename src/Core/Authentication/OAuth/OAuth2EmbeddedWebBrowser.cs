using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform;
using GitCredentialManager.UI;
using GitCredentialManager.UI.Controls;

namespace GitCredentialManager.Authentication.OAuth;

public class OAuth2EmbeddedWebBrowser : IOAuth2WebBrowser
{
    private readonly ICommandContext _context;
    private readonly IntPtr _parentHandle;
    private readonly string _title;
    private readonly (int Width, int Height) _size;

    public OAuth2EmbeddedWebBrowser(ICommandContext context, string title = null, (int Width, int Height)? size = null, IntPtr? parentHandle = null)
    {
        EnsureArgument.NotNull(context, nameof(context));

        _context = context;
        _parentHandle = parentHandle ?? IntPtr.Zero;
        _title = title ?? "Git Credential Manager";
        _size = size ?? (550, 750);

        // Enforce minimum size
        if (_size.Width < 550) _size.Width = 550;
        if (_size.Height < 750) _size.Height = 750;
    }

    // Nothing to do here since we capture the URI in the webview navigation event
    public Uri UpdateRedirectUri(Uri uri) => uri;

    public async Task<IDictionary<string, string>> GetAuthenticationResponseAsync(
        Uri authorizationUri, Uri redirectUri, OAuth2ResponseMode responseMode, CancellationToken ct)
    {
        IDictionary<string, string> finalParams = null;
        bool openInBrowser = false;

        WebViewWindow CreateDialog()
        {
            var webView = new WebViewWindow
            {
                Source = authorizationUri,
                CanResize = false,
                Title = _title,
                UserAgent = Constants.GetHttpUserAgent(_context.Trace2),
                ShowOpenInBrowser = true,
                ShowNavigationControls = false,
            };
            webView.Resize(_size.Width, _size.Height);
            webView.NavigationStarted += OnNavigationStarted;
            webView.EnvironmentRequested += OnEnvironmentRequested;
            webView.OpenInBrowserRequested += OnOpenInBrowserRequested;

            return webView;
        }

        void OnEnvironmentRequested(object sender, WebViewEnvironmentRequestedEventArgs e)
        {
#if DEBUG
            e.EnableDevTools = true;
#else
            e.EnableDevTools = false;
#endif

            switch (e)
            {
                case AppleWKWebViewEnvironmentRequestedEventArgs webkitArgs:
                    webkitArgs.NonPersistentDataStore = true;
                    break;

                case WindowsWebView2EnvironmentRequestedEventArgs wv2Args:
                    wv2Args.IsInPrivateModeEnabled = true;
                    wv2Args.AllowSingleSignOnUsingOSPrimaryAccount = true;
                    break;

                case GtkWebViewEnvironmentRequestedEventArgs gtkArgs:
                    gtkArgs.EphemeralDataManager = true;
                    break;
            }
        }

        void OnNavigationStarted(object sender, WebViewNavigationStartingEventArgs e)
        {
            if (sender is WebViewWindow window && e.Request is not null && redirectUri.IsBaseOf(e.Request))
            {
                finalParams = e.Request.GetQueryParameters();
                e.Cancel = true;
                window.Close();
            }
        }


        void OnOpenInBrowserRequested(object sender, WebViewOpenInBrowserEventArgs e)
        {
            e.ShouldCloseWindow = true;
            openInBrowser = true;
        }

        await AvaloniaUi.ShowWindowAsync(CreateDialog, _parentHandle, ct);

        if (openInBrowser)
        {
            _context.Trace.WriteLine("User requested to open the authorization URL in the system browser.");
            throw new SystemBrowserRequestedException();
        }

        if (finalParams is null)
        {
            throw new OperationCanceledException("User cancelled the authentication dialog.");
        }

        return finalParams;
    }

    /// <summary>
    /// Exception thrown when the user requests to open the authorization URL in the
    /// system browser instead of using the embedded web view.
    /// </summary>
    public class SystemBrowserRequestedException : Exception;
}
