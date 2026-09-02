using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using XXX.TestBench.App.Composition;

namespace XXX.TestBench.App;

public partial class App : Application
{
    private AppComposition? _composition;
    private static bool _compositionStarted;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // 启动守卫：同一进程只初始化一次组合根，避免 Headless 每测试会话重复创建窗口/组合根导致挂起
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && !_compositionStarted)
        {
            _compositionStarted = true;
            var baseDir = AppContext.BaseDirectory;
            var configRoot = Path.Combine(baseDir, "config");
            var dataRoot = Path.Combine(baseDir, "data");
            _composition = AppComposition.Create(configRoot, dataRoot);
            var mainWindow = new MainWindow
            {
                DataContext = _composition.Shell,
                WindowState = WindowState.Maximized
            };
            mainWindow.Icon = AppIconProvider.Create();
            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
