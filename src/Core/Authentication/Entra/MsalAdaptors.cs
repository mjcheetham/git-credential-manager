using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform;
using GitCredentialManager.UI;
using GitCredentialManager.UI.Controls;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensibility;

namespace GitCredentialManager.Authentication.Entra;

internal class MsalHttpClientFactoryAdaptor : IMsalHttpClientFactory
{
    private readonly IHttpClientFactory _factory;
    private HttpClient _instance;

    public MsalHttpClientFactoryAdaptor(IHttpClientFactory factory)
    {
        EnsureArgument.NotNull(factory, nameof(factory));

        _factory = factory;
    }

    // MSAL calls this method each time it wants to use an HTTP client.
    // We ensure we only create a single instance to avoid socket exhaustion.
    public HttpClient GetHttpClient() =>
        _instance ??= _factory.CreateClient();
}

internal class MsalParentWindowAdapter : IDisposable
{
    private readonly object _parentWindow;
    private readonly bool _createIfMissing;
    private readonly CancellationTokenSource _cts = new();

    private readonly Func<CancellationToken, IntPtr> _createWindow;

    public static MsalParentWindowAdapter Create(object parentWindow, bool createIfMissing = false)
    {
        return new MsalParentWindowAdapter(parentWindow, createIfMissing, ProgressWindow.ShowAndGetHandle);
    }

    internal MsalParentWindowAdapter(object parentWindow, bool createIfMissing,
        Func<CancellationToken, IntPtr> createWindow)
    {
        EnsureArgument.NotNull(createWindow, nameof(createWindow));

        _parentWindow = parentWindow;
        _createIfMissing = createIfMissing;
        _createWindow = createWindow;
    }

    public object GetWindow()
    {
        // Try to use the parent window we were given (if any)
        if (_parentWindow is IntPtr p && p != IntPtr.Zero)
        {
            return _parentWindow;
        }

        // Create a stub window to use as a parent instead (if the caller requires it)
        if (_createIfMissing)
        {
            return _createWindow(_cts.Token);
        }

        return null;
    }

    public void Dispose()
    {
        // Close and clean up any stub window we may have created
        _cts.Cancel();
    }
}

internal class MsalAvaloniaCustomWebUi : ICustomWebUi
{
    private readonly ICommandContext _context;
    private readonly IntPtr _parent;

    public MsalAvaloniaCustomWebUi(ICommandContext context, IntPtr? parent = null)
    {
        EnsureArgument.NotNull(context, nameof(context));
        _context = context;
        _parent = parent ?? IntPtr.Zero;
    }

    public async Task<Uri> AcquireAuthorizationCodeAsync(Uri authorizationUri, Uri redirectUri, CancellationToken ct)
    {
        Uri finalUri = null;
        bool useSystemBrowser = false;

        // Force response_mode=form_post to ensure the authorization code is returned
        // in a POST request body instead of a query string.
        authorizationUri = new(authorizationUri + "&response_mode=form_post");

        WebViewWindow CreateDialog()
        {
            // Windows WebView1 allows for device authentication which is often required
            // for Entra work & school accounts that are subject to conditional access policies.
            var webView = new WebViewWindow(preferLegacyWebViews: true)
            {
                Source = authorizationUri,
                CanResize = false,
                Title = "Git Credential Manager",
                UserAgent = Constants.GetHttpUserAgent(),
                ShowOpenInBrowser = true, // Allow an escape hatch to switch to system browser if embedded fails
                ShowNavigationControls = false, // Entra provides appropriate navigation in its UI
            };
            webView.EnvironmentRequested += OnEnvironmentRequested;
            webView.NavigationStarted += OnNavigationStarted;
            webView.OpenInBrowserRequested += OnOpenInBrowser;
            webView.WebResourceRequested += OnWebResourceRequested;
            webView.Resize(550, 750);
            return webView;
        }

        void OnEnvironmentRequested(object sender, WebViewEnvironmentRequestedEventArgs e)
        {
            const string userAgentAppName = "git-credential-manager";

#if DEBUG
            e.EnableDevTools = true;
#else
            e.EnableDevTools = false;
#endif

            // Use private/incognito mode for all web views to avoid caching credentials in the browser
            // so we always get a fresh login prompt and avoid any issues with cached credentials.
            switch (e)
            {
                case WindowsWebView1EnvironmentRequestedEventArgs wv1Args:
                    break;
                case WindowsWebView2EnvironmentRequestedEventArgs wv2Args:
                    wv2Args.IsInPrivateModeEnabled = true;
                    break;
                case AppleWKWebViewEnvironmentRequestedEventArgs wkArgs:
                    wkArgs.NonPersistentDataStore = true;
                    wkArgs.ApplicationNameForUserAgent = userAgentAppName;
                    break;
                case LinuxWpeWebViewEnvironmentRequestedEventArgs wpeArgs:
                    break;
                case GtkWebViewEnvironmentRequestedEventArgs gtkArgs:
                    gtkArgs.EphemeralDataManager = true;
                    gtkArgs.ApplicationNameForUserAgent = userAgentAppName;
                    break;
            }
        }

        void OnNavigationStarted(object sender, WebViewNavigationStartingEventArgs e)
        {
            if (sender is WebViewWindow window &&
                e.Request is not null && redirectUri.IsBaseOf(e.Request))
            {
                finalUri = e.Request;
                e.Cancel = true;
                window.Close();
            }
        }

        void OnOpenInBrowser(object sender, WebViewOpenInBrowserEventArgs e)
        {
            e.ShouldCloseWindow = true;
            useSystemBrowser = true;
        }

        void OnWebResourceRequested(object sender, WebResourceRequestedEventArgs e)
        {
            // Intercept the POST request
            if (e.Request.Method == HttpMethod.Post)
            {
                ;
            }
        }

        await AvaloniaUi.ShowWindowAsync(CreateDialog, _parent, ct);

        if (useSystemBrowser)
        {
            // We don't have a way to switch MSAL to use the system browser mid-flow.
            // The only thing we can do to escape back up to the original caller is to
            // throw a specific exception that they can catch and handle; retrying the
            // flow with the system browser instead.
            throw new SystemBrowserSwitchException();
        }

        if (finalUri is null)
        {
            throw new OperationCanceledException("User cancelled the authentication dialog.");
        }

        return finalUri;
    }

    public class SystemBrowserSwitchException : Exception;
}
