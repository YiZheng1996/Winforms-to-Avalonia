using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using XXX.TestBench.App.ViewModels;

namespace XXX.TestBench.App.Views;

/// <summary>
/// 登录页面视图。
/// </summary>
public partial class LoginView : UserControl
{
    /// <summary>
    /// 初始化登录页面。
    /// </summary>
    public LoginView() => InitializeComponent();

    /// <summary>
    /// 密码输入框回车直接登录：复用登录按钮的 LoginCommand（账号、密码均非空时才可执行）。
    /// </summary>
    private void OnPasswordKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not ShellViewModel shell)
            return;

        if (shell.LoginCommand.CanExecute(null))
        {
            e.Handled = true;
            shell.LoginCommand.Execute(null);
        }
    }

    /// <summary>
    /// 取消登录：放弃登录并退出应用程序。
    /// </summary>
    private void OnCancelLoginClick(object? sender, RoutedEventArgs e)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.TryShutdown();
    }
}