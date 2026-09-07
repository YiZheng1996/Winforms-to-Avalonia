using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using XXX.TestBench.App.ViewModels;

namespace XXX.TestBench.App;

/// <summary>
/// 登录窗口。认证完成后创建工艺主窗口并关闭自身，避免登录页面成为主窗口内容的一部分。
/// </summary>
public partial class LoginWindow : Window
{
    private ShellViewModel? _shell;
    private bool _mainWindowOpened;

    /// <summary>
    /// 提供 Avalonia 资源加载器和设计器使用的无参构造函数。
    /// </summary>
    public LoginWindow()
    {
        InitializeComponent();
        Closed += OnClosed;
    }

    /// <summary>
    /// 初始化登录窗口并订阅认证完成通知。
    /// </summary>
    public LoginWindow(ShellViewModel shell)
        : this()
    {
        ArgumentNullException.ThrowIfNull(shell);

        _shell = shell;
        DataContext = shell;
        _shell.AuthenticationSucceeded += OnAuthenticationSucceeded;
    }

    /// <summary>
    /// 认证与首个页面加载完成后切换到工艺主窗口。
    /// </summary>
    private void OnAuthenticationSucceeded(object? sender, EventArgs e)
    {
        if (_mainWindowOpened)
            return;

        if (_shell is null
            || Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        var mainWindow = new MainWindow
        {
            DataContext = _shell,
            WindowState = WindowState.Maximized,
            Icon = AppIconProvider.Create()
        };

        // 先替换桌面生命周期的主窗口，再关闭登录窗口，避免 OnMainWindowClose 误判为应用退出。
        desktop.MainWindow = mainWindow;
        _mainWindowOpened = true;
        mainWindow.Show();
        Close();
    }

    /// <summary>
    /// 关闭登录窗口后解除事件订阅，避免窗口被视图模型长期持有。
    /// </summary>
    private void OnClosed(object? sender, EventArgs e)
    {
        if (_shell is not null)
            _shell.AuthenticationSucceeded -= OnAuthenticationSucceeded;
        Closed -= OnClosed;
    }
}
