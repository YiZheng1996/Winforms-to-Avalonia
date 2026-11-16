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
    private bool _compositionStarted;

    /// <summary>
    /// 是否在框架初始化完成时创建生产组合根和首个窗口；Headless 测试可关闭，仍保留 Avalonia 基类生命周期。
    /// </summary>
    protected virtual bool EnableProductionStartup => true;

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
        // 启动守卫：同一个 Application 实例只初始化一次组合根；Headless 每测试会话会重建 Application。
        if (EnableProductionStartup
            && ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && !_compositionStarted)
        {
            _compositionStarted = true;
            var baseDir = AppContext.BaseDirectory;
            var configRoot = Path.Combine(baseDir, "config");
            var dataRoot = Path.Combine(baseDir, "data");
            _composition = AppComposition.Create(configRoot, dataRoot);

            if (_composition.Shell is null)
                throw new InvalidOperationException("应用组合根未创建外壳视图模型。");

            // 登录窗口是桌面生命周期的初始主窗口；认证完成后会被 LoginWindow 替换为工艺主窗口。
            // 固定为主窗口关闭即退出，避免退出工艺界面后又回到登录界面。
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            desktop.Exit += (_, _) => _composition?.Dispose();

            if (_composition.RequiresInstallationSetup)
            {
                var setupWindow = new FirstRunSetupWindow(_composition.StartupError)
                {
                    Icon = AppIconProvider.Create()
                };

                setupWindow.SetupCompleted += (_, _) =>
                {
                    _composition?.Dispose();
                    var configuredComposition = AppComposition.Create(configRoot, dataRoot);
                    _composition = configuredComposition;

                    Window nextWindow;
                    if (configuredComposition.StartupError is null && configuredComposition.Shell is { } configuredShell)
                    {
                        nextWindow = new LoginWindow(configuredShell)
                        {
                            Icon = AppIconProvider.Create()
                        };
                    }
                    else if (configuredComposition.Shell is { } faultShell)
                    {
                        nextWindow = new MainWindow
                        {
                            DataContext = faultShell,
                            WindowState = WindowState.Maximized,
                            Icon = AppIconProvider.Create()
                        };
                    }
                    else
                    {
                        nextWindow = new MainWindow { Icon = AppIconProvider.Create() };
                    }

                    desktop.MainWindow = nextWindow;
                    nextWindow.Show();
                    setupWindow.Close();
                };

                desktop.MainWindow = setupWindow;
            }
            else if (_composition.StartupError is null && _composition.Shell is { } loginShell)
            {
                desktop.MainWindow = new LoginWindow(loginShell)
                {
                    Icon = AppIconProvider.Create()
                };
            }
            else if (_composition.Shell is { } startupFaultShell)
            {
                // 启动故障没有可用认证服务，直接显示诊断主窗口，不进入登录流程。
                var faultWindow = new MainWindow
                {
                    DataContext = startupFaultShell,
                    WindowState = WindowState.Maximized,
                    Icon = AppIconProvider.Create()
                };
                desktop.MainWindow = faultWindow;
            }
            else
            {
                throw new InvalidOperationException("应用组合根未创建启动视图模型。");
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
