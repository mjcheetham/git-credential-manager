using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GitCredentialManager.UI.Views;

public partial class ProgressWindow : Window
{
    public ProgressWindow()
    {
        InitializeComponent();
    }

    public IntPtr ShowAndGetHandle(CancellationToken ct)
    {
        var handle = new TaskCompletionSource<IntPtr>();

        Loaded += (sender, args) => handle.TrySetResult(TryGetPlatformHandle()?.Handle ?? IntPtr.Zero);
        ct.Register(() =>
        {
            handle.TrySetResult(IntPtr.Zero);
            Avalonia.Threading.Dispatcher.UIThread.Post(Close);
        });

        if (ct.IsCancellationRequested)
        {
            handle.TrySetResult(IntPtr.Zero);
        }
        else
        {
            Show();
        }

        return handle.Task.GetAwaiter().GetResult();
    }
}
