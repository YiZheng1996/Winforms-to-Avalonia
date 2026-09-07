using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
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
    public LoginView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 登录窗口挂载后将输入焦点放到账号框，方便启动后直接输入账号。
    /// 使用输入优先级延后一轮调度，确保窗口已经完成显示和焦点初始化。
    /// </summary>
    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(FocusLoginName, DispatcherPriority.Input);
    }

    private void FocusLoginName()
    {
        if (LoginNameBox.IsEffectivelyVisible && LoginNameBox.IsEnabled)
            LoginNameBox.Focus();
    }

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
