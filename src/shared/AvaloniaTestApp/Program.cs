using Avalonia;
using Avalonia.Controls;
using GitCredentialManager.UI;

namespace GitCredentialManager;

static class Program
{
    public static void Main(string[] args)
    {
        AppBuilder builder = BuildAvaloniaApp().SetupWithoutStarting();
        var window = new TestWindow { DataContext = new TestWindowViewModel() };
        window.Show();
        window.Activate();
        builder.Instance!.Run(window);
    }

// Required for Avalonia designer
    static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<AvaloniaApp>()
            .UsePlatformDetect()
            .LogToTrace();
}
