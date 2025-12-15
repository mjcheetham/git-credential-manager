using Avalonia;
using Avalonia.Controls;
using GitCredentialManager.UI;

namespace GitCredentialManager;

static class Program
{
    public static void Main(string[] args)
    {
        AppBuilder builder = BuildAvaloniaApp().SetupWithoutStarting();
        builder.Instance!.RunWithMainWindow<TestWindow>();
// Required for Avalonia designer
    }

    static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<AvaloniaApp>()
            .UsePlatformDetect()
            .LogToTrace();
}
