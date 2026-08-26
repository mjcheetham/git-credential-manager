using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;

namespace GitCredentialManager.UI.Controls;

public partial class WebViewWindow : Window
{
    // Keep! Required for visual designer
    public WebViewWindow() : this(false) { }

    public WebViewWindow(bool preferLegacyWebViews)
    {
        InitializeComponent();

        // Must set the adapter preference before it is attached to the visual tree!
        if (PlatformUtils.IsWindows() && preferLegacyWebViews)
        {
            WebView.AdapterPreference =
            [
                WebViewAdapterType.WebView1, // WebView1 should always be available in practice,
                WebViewAdapterType.WebView2 // but add a WebView2 fallback just in case.
            ];
        }
    }

    public event EventHandler<WebViewEnvironmentRequestedEventArgs> EnvironmentRequested;

    public event EventHandler<WebViewNavigationStartingEventArgs> NavigationStarted;

    public event EventHandler<WebViewOpenInBrowserEventArgs> OpenInBrowserRequested;

    public Uri Source
    {
        get => WebView.Source;
        set => WebView.Source = value;
    }

    public string UserAgent
    {
        get => WebView.UserAgent;
        set => WebView.UserAgent = value;
    }

    public bool ShowOpenInBrowser
    {
        get => OpenInBrowserButton.IsVisible;
        set => OpenInBrowserButton.IsVisible = value;
    }

    public bool ShowNavigationControls
    {
        get => NavigationControls.IsVisible;
        set => NavigationControls.IsVisible = value;
    }

    public void Resize(int width, int height)
    {
        Width = width;
        Height = height;
    }

    private void OnNavigationCompleted(object sender, WebViewNavigationCompletedEventArgs e)
    {
        WebView.IsVisible = true;
        BackButton.IsEnabled = WebView.CanGoBack;
        ForwardButton.IsEnabled = WebView.CanGoForward;
    }

    private void OnNavigationStarted(object sender, WebViewNavigationStartingEventArgs e)
    {
        NavigationStarted?.Invoke(this, e);
    }

    private void OnEnvironmentRequested(object sender, WebViewEnvironmentRequestedEventArgs e)
    {
        EnvironmentRequested?.Invoke(this, e);
    }

    private void OnBackClicked(object sender, RoutedEventArgs e)
    {
        WebView.GoBack();
    }

    private void OnForwardClicked(object sender, RoutedEventArgs e)
    {
        WebView.GoForward();
    }

    private void OnOpenInBrowserClicked(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        var args = new WebViewOpenInBrowserEventArgs(WebView.Source);
        OpenInBrowserRequested?.Invoke(this, args);
        if (args.ShouldCloseWindow)
        {
            Close();
        }
    }
}

public class WebViewOpenInBrowserEventArgs(Uri uri) : EventArgs
{
    /// <summary>
    /// The current URI that the web view window is displaying.
    /// </summary>
    public Uri Uri => uri;

    /// <summary>
    /// Whether the current web view window should be closed after the event is handled.
    /// </summary>
    public bool ShouldCloseWindow { get; set; }
}
