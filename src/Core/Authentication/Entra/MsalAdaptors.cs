using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform;
using GitCredentialManager.Interop.Windows.Native;
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

    public static MsalParentWindowAdapter Create(object parentWindow, bool createIfMissing = false)
    {
        return new MsalParentWindowAdapter(parentWindow, createIfMissing);
    }

    private MsalParentWindowAdapter(object parentWindow, bool createIfMissing = false)
    {
        _parentWindow = parentWindow;
        _createIfMissing = createIfMissing;
    }

    public object GetWindow()
    {
        if (_parentWindow is IntPtr p && p != IntPtr.Zero)
        {
            return _parentWindow;
        }

        // Create a stub window to use as a parent
        if (_createIfMissing)
        {
            return ProgressWindow.ShowAndGetHandle(_cts.Token);
        }

        // On Windows we can try and get the console window parent handle if that exists
        if (PlatformUtils.IsWindows())
        {
            IntPtr consoleHandle = Kernel32.GetConsoleWindow();
            IntPtr parentHandle = User32.GetAncestor(consoleHandle, GetAncestorFlags.GetRootOwner);

            if (parentHandle != IntPtr.Zero)
            {
                return parentHandle;
            }
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

        WebViewWindow CreateDialog()
        {
            // Windows WebView1 allows for device authentication which is often required
            // for Entra work & school accounts that are subject to conditional access policies.
            var webView = new WebViewWindow(preferLegacyWebViews: true)
            {
                Source = authorizationUri,
                CanResize = false,
                Title = "Git Credential Manager",
                UserAgent = Constants.GetHttpUserAgent(_context.Trace2),
                ShowOpenInBrowser = true, // Allow an escape hatch to switch to system browser if embedded fails
                ShowNavigationControls = false, // Entra provides appropriate navigation in its UI
            };
            webView.EnvironmentRequested += OnEnvironmentRequested;
            webView.NavigationStarted += OnNavigationStarted;
            webView.OpenInBrowserRequested += OnOpenInBrowser;
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
