using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform;
using GitCredentialManager.UI;
using GitCredentialManager.UI.Controls;

namespace GitCredentialManager.Authentication.OAuth;

public class OAuth2EmbeddedWebBrowserOptions
{
    /// <summary>
    /// The parent window handle to use for the embedded web browser dialog.
    /// If set to <c>IntPtr.Zero</c>, the dialog will be shown as a top-level window.
    /// </summary>
    public IntPtr ParentWindowHandle { get; set; } = IntPtr.Zero;

    /// <summary>
    /// The title of the embedded web browser dialog.
    /// The default value of "Git Credential Manager" will be shown if not set.
    /// </summary>
    public string Title { get; set; }

    /// <summary>
    /// The initial size of the embedded web browser dialog.
    /// </summary>
    public (int Width, int Height) InitialSize { get; set; } = (550, 750);
}

public class OAuth2EmbeddedWebBrowser : IOAuth2WebBrowser
{
    private readonly ITrace _trace;
    private readonly OAuth2EmbeddedWebBrowserOptions _options;

    public OAuth2EmbeddedWebBrowser(ITrace trace, OAuth2EmbeddedWebBrowserOptions browserOptions = null)
    {
        EnsureArgument.NotNull(trace, nameof(trace));

        _trace = trace;
        _options = browserOptions ?? new OAuth2EmbeddedWebBrowserOptions();
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
                Title = _options.Title ?? "Git Credential Manager",
                UserAgent = Constants.GetHttpUserAgent(),
                ShowOpenInBrowser = true,
                ShowNavigationControls = false,
            };

            // Enforce minimum size
            int width = Math.Max(_options.InitialSize.Width, 550);
            int height = Math.Max(_options.InitialSize.Height, 750);

            webView.Resize(width, height);
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

        await AvaloniaUi.ShowWindowAsync(CreateDialog, _options.ParentWindowHandle, ct);

        if (openInBrowser)
        {
            _trace.WriteLine("User requested to open the authorization URL in the system browser.");
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
