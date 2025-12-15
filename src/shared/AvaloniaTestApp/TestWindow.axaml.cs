using Avalonia.Controls;
using GitCredentialManager.UI;
using GitCredentialManager.UI.Controls;

namespace GitCredentialManager;

public partial class TestWindow : Window
{
    private static readonly CommandContext Context = new();

    private static readonly Dictionary<Type, UI.ViewModels.ViewModel> ViewModels = new()
    {
        //
        // Generic
        //
        {
            typeof(UI.Views.CredentialsView),
            new UI.ViewModels.CredentialsViewModel()
        },
        {
            typeof(UI.Views.DefaultAccountView),
            new UI.ViewModels.DefaultAccountViewModel(Context.Environment)
                { UserName = "TestUser" }
        },
        {
            typeof(UI.Views.DeviceCodeView),
            new UI.ViewModels.DeviceCodeViewModel(Context.Environment)
                { UserCode = "ABCD-1234", VerificationUrl = "https://example.com" }
        },
        {
            typeof(UI.Views.OAuthView),
            new UI.ViewModels.OAuthViewModel { ShowBrowserLogin = true, ShowDeviceCodeLogin = true }
        },

        //
        // Bitbucket
        //
        {
            typeof(Atlassian.Bitbucket.UI.Views.CredentialsView),
            new Atlassian.Bitbucket.UI.ViewModels.CredentialsViewModel(Context.Environment)
                { ShowBasic = true, ShowOAuth = true }
        },

        //
        // GitHub
        //
        {
            typeof(GitHub.UI.Views.CredentialsView),
            new GitHub.UI.ViewModels.CredentialsViewModel(Context.Environment, Context.ProcessManager)
                { ShowBrowserLogin = true, ShowDeviceLogin = true, ShowBasicLogin = true, ShowTokenLogin = true }
        },
        {
            typeof(GitHub.UI.Views.SelectAccountView),
            new GitHub.UI.ViewModels.SelectAccountViewModel(Context.Environment)
            {
                Accounts =
                [
                    new GitHub.UI.ViewModels.AccountViewModel { UserName = "User1" },
                    new GitHub.UI.ViewModels.AccountViewModel { UserName = "User2" }
                ]
            }
        },
        {
            typeof(GitHub.UI.Views.DeviceCodeView),
            new GitHub.UI.ViewModels.DeviceCodeViewModel(Context.Environment)
                { UserCode = "ABCD-1234", VerificationUrl = "https://example.com" }
        },
        {
            typeof(GitHub.UI.Views.TwoFactorView),
            new GitHub.UI.ViewModels.TwoFactorViewModel(Context.Environment, Context.ProcessManager)
        },

        //
        // GitLab
        //
        {
            typeof(GitLab.UI.Views.CredentialsView),
            new GitLab.UI.ViewModels.CredentialsViewModel(Context.Environment)
                { ShowBrowserLogin = true, ShowTokenLogin = true, ShowBasicLogin = true }
        }
    };

    public TestWindow() => InitializeComponent();

    public RelayCommand<Type> OpenDialogWindowCommand => new(OpenDialogWindow);

    private void OpenDialogWindow(Type viewType)
    {
        Control view = Activator.CreateInstance(viewType) as Control
                       ?? throw new InvalidOperationException($"Could not create view of type {viewType.FullName}.");

        var window = new DialogWindow(view)
        {
            DataContext = ViewModels.TryGetValue(viewType, out var viewModel)
                ? viewModel
                : new UI.ViewModels.WindowViewModel()
        };

        window.Show(this);
    }
}
