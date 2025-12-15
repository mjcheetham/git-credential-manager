using System.ComponentModel;
using Avalonia.Styling;
using GitCredentialManager.UI;

namespace GitCredentialManager;

public class TestWindowViewModel : UI.ViewModels.ViewModel
{
    private static readonly CommandContext Context = new();

    public TestWindowViewModel()
    {
        GitHubSelectAccountAddAccountCommand = new(() =>
            {
                GitHubSelectAccount.Accounts.Add(new GitHub.UI.ViewModels.AccountViewModel
                {
                    UserName = GitHubSelectAccountNewAccountUserName
                });
                GitHubSelectAccountNewAccountUserName = null;
            },
            () => !string.IsNullOrWhiteSpace(GitHubSelectAccountNewAccountUserName));

        GitHubSelectAccountRemoveAccountCommand = new(account =>
            {
                if (account is null) return;
                GitHubSelectAccount.Accounts.Remove(account);
            },
            x => x is not null);

        PropertyChanged += OnPropertyChanged;
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(GitHubSelectAccountNewAccountUserName):
                GitHubSelectAccountAddAccountCommand.RaiseCanExecuteChanged();
                break;
        }
    }

    private string? _lastWindowResult;

    public string? LastWindowResult
    {
        get => _lastWindowResult;
        set => SetAndRaisePropertyChanged(ref _lastWindowResult, value);
    }

    public static ThemeVariant[] ThemeVariants { get; } =
    [
        ThemeVariant.Default, ThemeVariant.Light, ThemeVariant.Dark
    ];

    public ThemeVariant SelectedThemeVariant
    {
        get => Avalonia.Application.Current?.RequestedThemeVariant ?? ThemeVariant.Default;
        set
        {
            Avalonia.Application.Current!.RequestedThemeVariant = value;
            RaisePropertyChanged();
        }
    }

    #region Generic

    public UI.ViewModels.CredentialsViewModel GenericCredentials { get; } = new();

    public UI.ViewModels.DefaultAccountViewModel GenericDefaultAccount { get; } = new(Context.Environment);

    public UI.ViewModels.DeviceCodeViewModel GenericDeviceCode { get; } = new(Context.Environment)
        { UserCode = "ABCD-EFGH", VerificationUrl = "https://example.com/device" };

    public UI.ViewModels.OAuthViewModel GenericOAuth { get; } = new()
        { ShowBrowserLogin = true, ShowDeviceCodeLogin = true };

    #endregion

    #region Bitbucket

    public Atlassian.Bitbucket.UI.ViewModels.CredentialsViewModel BitbucketCredentials { get; } = new(Context.Environment)
        { ShowBasic = true, ShowOAuth = true };

    #endregion

    #region GitHub

    private string? _gitHubSelectAccountNewAccountUserName;

    public GitHub.UI.ViewModels.CredentialsViewModel GitHubCredentials { get; } = new(Context.Environment, Context.ProcessManager)
        { ShowBrowserLogin = true, ShowDeviceLogin = true, ShowTokenLogin = true, ShowBasicLogin = true };

    public GitHub.UI.ViewModels.SelectAccountViewModel GitHubSelectAccount { get; } = new(Context.Environment)
        { Accounts = { new() { UserName = "user1" }, new() { UserName = "user2" } } };

    public GitHub.UI.ViewModels.DeviceCodeViewModel GitHubDeviceCode { get; } = new(Context.Environment)
        { UserCode = "ABCD-1234", VerificationUrl = "https://example.com/device" };

    public GitHub.UI.ViewModels.TwoFactorViewModel GitHubTwoFactor { get; } = new(Context.Environment, Context.ProcessManager);

    public string? GitHubSelectAccountNewAccountUserName
    {
        get => _gitHubSelectAccountNewAccountUserName;
        set => SetAndRaisePropertyChanged(ref _gitHubSelectAccountNewAccountUserName, value);
    }

    public RelayCommand GitHubSelectAccountAddAccountCommand { get; }

    public RelayCommand<GitHub.UI.ViewModels.AccountViewModel> GitHubSelectAccountRemoveAccountCommand { get; }

    #endregion

    #region GitLab

    public GitLab.UI.ViewModels.CredentialsViewModel GitLabCredentials { get; } = new(Context.Environment)
        { ShowBrowserLogin = true, ShowTokenLogin = true, ShowBasicLogin = true };

    #endregion
}
