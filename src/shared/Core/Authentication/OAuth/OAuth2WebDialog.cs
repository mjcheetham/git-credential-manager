using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform;
using GitCredentialManager.UI;

namespace GitCredentialManager.Authentication.OAuth;

public class OAuth2WebDialog(int width = 500, int height = 750) : OAuth2WebBrowser
{
    private record AuthFlowState
    {
        public Uri RedirectUri;
        public Uri FinalUri;
    }

    private readonly ConcurrentDictionary<NativeWebDialog, AuthFlowState> _instances = new();
    private readonly int _width = width;
    private readonly int _height = height;

    public override async Task<Uri> GetAuthenticationCodeAsync(Uri authorizationUri, Uri redirectUri, CancellationToken ct)
    {
        // Create a new state bucket for this authentication flow
        var state = new AuthFlowState
        {
            RedirectUri = redirectUri
        };

        await AvaloniaUi.ShowWebDialogAsync(d => OnDialogCreate(d, state), OnDialogDestroy, authorizationUri, IntPtr.Zero, ct);

        return state.FinalUri;
    }

    private void OnDialogCreate(NativeWebDialog dialog, AuthFlowState state)
    {
        dialog.EnvironmentRequested += OnEnvironmentRequested;
        dialog.NavigationStarted += OnNavigationStarted;
        dialog.Resize(_width, _height);
        dialog.Title = "Git Credential Manager";

        _instances[dialog] = state;
    }

    private void OnDialogDestroy(NativeWebDialog dialog)
    {
        _instances.TryRemove(dialog, out _);
    }

    private void OnEnvironmentRequested(object sender, WebViewEnvironmentRequestedEventArgs e)
    {
#if DEBUG
        // Enable developer tools only in debug builds
        e.EnableDevTools = true;
#endif

        if (sender is NativeWebDialog dialog)
        {
            IPlatformHandle handle = dialog.TryGetWebViewPlatformHandle();
            switch (handle)
            {
                case IAppleWKWebViewPlatformHandle appleHandle:
                    break;
                case IWindowsWebView1PlatformHandle win1Handle:
                    break;
                case IWindowsWebView2PlatformHandle win2Handle:
                    break;
                case IGtkWebViewPlatformHandle gtkHandle:
                    break;
            }
        }

        // Use ephemeral or non-persistent data stores to avoid caching credentials or other sensitive data
        switch (e)
        {
            case AppleWKWebViewEnvironmentRequestedEventArgs appleArgs:
                //appleArgs.NonPersistentDataStore = true;
                break;
            case WindowsWebView2EnvironmentRequestedEventArgs windowsArgs:
                windowsArgs.UserDataFolder = null; // Use ephemeral data store
                break;
            case GtkWebViewEnvironmentRequestedEventArgs gtkArgs:
                gtkArgs.EphemeralDataManager = true;
                break;
        }
    }


    private void OnNavigationStarted(object sender, WebViewNavigationStartingEventArgs e)
    {
        if (e.Request is null || sender is not NativeWebDialog dialog ||
            !_instances.TryGetValue(dialog, out AuthFlowState state) ||
            !state.RedirectUri.IsBaseOf(e.Request))
        {
            return;
        }

        state.FinalUri = e.Request;
        e.Cancel = true;
        dialog.Close();
    }
}
