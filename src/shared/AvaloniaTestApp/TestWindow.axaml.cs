using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using GitCredentialManager.UI.Controls;
using GitCredentialManager.UI.Views;
using CredentialsView = GitCredentialManager.UI.Views.CredentialsView;
using DeviceCodeView = GitCredentialManager.UI.Views.DeviceCodeView;

namespace GitCredentialManager;

public partial class TestWindow : Window
{
    public TestWindow() => InitializeComponent();

    private TestWindowViewModel? ViewModel => DataContext as TestWindowViewModel;

    private async void OpenDialogWindow<T>(UI.ViewModels.WindowViewModel? viewModel = null)
    {
        try
        {
            Control view = Activator.CreateInstance(typeof(T)) as Control
                           ?? throw new InvalidOperationException($"Could not create view of type {typeof(T).FullName}.");

            viewModel ??= new UI.ViewModels.WindowViewModel();

            var window = new DialogWindow(view)
            {
                DataContext = viewModel
            };

            if (ViewModel is not null)
            {
                ViewModel.LastWindowResult = "(open)";
            }

            await window.ShowDialog(this);

            if (ViewModel is not null)
            {
                ViewModel.LastWindowResult = viewModel.WindowResult.ToString().ToLowerInvariant();
            }
        }
        catch
        {
            // do nothing
        }
    }

    private void Reset(object? sender, RoutedEventArgs e) => DataContext = new TestWindowViewModel();

    #region Generic

    private void ShowGenericCredentials(object? sender, RoutedEventArgs e) =>
        OpenDialogWindow<CredentialsView>(ViewModel?.GenericCredentials);

    private void ShowGenericDefaultAccount(object? sender, RoutedEventArgs e) =>
        OpenDialogWindow<DefaultAccountView>(ViewModel?.GenericDefaultAccount);

    private void ShowGenericDeviceCode(object? sender, RoutedEventArgs e) =>
        OpenDialogWindow<DeviceCodeView>(ViewModel?.GenericDeviceCode);

    private void ShowGenericOAuth(object? sender, RoutedEventArgs e) =>
        OpenDialogWindow<OAuthView>(ViewModel?.GenericOAuth);

    #endregion

    #region Bitbucket

    private void ShowBitbucketCredentials(object? sender, RoutedEventArgs e) =>
        OpenDialogWindow<Atlassian.Bitbucket.UI.Views.CredentialsView>(ViewModel?.BitbucketCredentials);

    #endregion

    #region GitHub

    private void ShowGitHubCredentials(object? sender, RoutedEventArgs e) =>
        OpenDialogWindow<GitHub.UI.Views.CredentialsView>(ViewModel?.GitHubCredentials);

    private void ShowGitHubSelectAccount(object? sender, RoutedEventArgs e) =>
        OpenDialogWindow<GitHub.UI.Views.SelectAccountView>(ViewModel?.GitHubSelectAccount);

    private void ShowGitHubDeviceCode(object? sender, RoutedEventArgs e) =>
        OpenDialogWindow<GitHub.UI.Views.DeviceCodeView>(ViewModel?.GitHubDeviceCode);

    private void ShowGitHubTwoFactor(object? sender, RoutedEventArgs e) =>
        OpenDialogWindow<GitHub.UI.Views.TwoFactorView>(ViewModel?.GitHubTwoFactor);

    #endregion

    #region GitLab

    private void ShowGitLabCredentials(object? sender, RoutedEventArgs e) =>
        OpenDialogWindow<GitLab.UI.Views.CredentialsView>(ViewModel?.GitLabCredentials);

    #endregion
}
