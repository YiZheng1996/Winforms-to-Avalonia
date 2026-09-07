using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using XXX.TestBench.App.Composition;

namespace XXX.TestBench.App;

/// <summary>
/// 应用入口类，负责加载界面资源并创建主窗口。
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// 组合根引用，保存应用创建的服务。
    /// </summary>
    private AppComposition? _composition;
    /// <summary>
    /// 启动守卫，保证同一进程只初始化一次。
    /// </summary>
    private static bool _compositionStarted;

    /// <summary>
    /// 加载界面资源。
    /// </summary>
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// 框架初始化完成后创建主窗口并进入运行。
    /// </summary>
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

            if (_composition.Shell is not { } shell)
                throw new InvalidOperationException("应用组合根未创建外壳视图模型。");

            // 登录窗口是桌面生命周期的初始主窗口；认证完成后会被 LoginWindow 替换为工艺主窗口。
            // 固定为主窗口关闭即退出，避免退出工艺界面后又回到登录界面。
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;

            if (_composition.StartupError is null)
            {
                desktop.MainWindow = new LoginWindow(shell)
                {
                    Icon = AppIconProvider.Create()
                };
            }
            else
            {
                // 启动故障没有可用认证服务，直接显示诊断主窗口，不进入登录流程。
                var faultWindow = new MainWindow
                {
                    DataContext = shell,
                    WindowState = WindowState.Maximized,
                    Icon = AppIconProvider.Create()
                };
                desktop.MainWindow = faultWindow;
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
