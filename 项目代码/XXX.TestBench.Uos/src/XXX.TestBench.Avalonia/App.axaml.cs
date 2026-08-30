using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using XXX.TestBench.Avalonia.Composition;

namespace XXX.TestBench.Avalonia;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var composition = AppComposition.Create();
            var lifetimeCancellation = new CancellationTokenSource();
            desktop.MainWindow = new MainWindow
            {
                DataContext = composition.MainWindow
            };
            desktop.Exit += (_, _) =>
            {
                // 先取消尚未完成的只读轮询，再释放 Runtime，避免退出后迟到结果回写 UI。
                lifetimeCancellation.Cancel();
                composition.DisposeAsync().AsTask().GetAwaiter().GetResult();
                lifetimeCancellation.Dispose();
            };
            _ = composition.MainWindow.StartAsync(lifetimeCancellation.Token);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
