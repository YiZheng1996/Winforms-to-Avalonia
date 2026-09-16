using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using XXX.TestBench.App.ViewModels;
using XXX.TestBench.App.Views;

namespace XXX.TestBench.App;

/// <summary>
/// 主窗口，承载导航与各业务页面。
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// 顶栏时钟定时器。
    /// </summary>
    private readonly DispatcherTimer _clockTimer;
    private bool _isExiting;

    /// <summary>
    /// 初始化主窗口并启动时钟。
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        UpdateClock();
        SizeChanged += (_, _) => UpdateProcessProductInfoLayout();
        DataContextChanged += (_, _) => UpdateProcessProductInfoLayout();
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
        Closed += (_, _) => _clockTimer.Stop();
        UpdateProcessProductInfoLayout();
    }

    /// <summary>
    /// 点击左侧导航按钮后切换到对应页面。
    /// </summary>
    private async void OnNavClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: NavigationItemViewModel item } && DataContext is ShellViewModel shell)
            await shell.NavigateCommand.ExecuteAsync(item);
    }

    /// <summary>
    /// 退出工艺主窗口：先撤销当前会话，再关闭主窗口并结束应用，不返回登录窗口。
    /// </summary>
    private async void OnExitClick(object? sender, RoutedEventArgs e)
    {
        if (_isExiting)
            return;

        _isExiting = true;
        e.Handled = true;
        try
        {
            if (DataContext is ShellViewModel shell)
                await shell.LogoutAsync();
        }
        catch (Exception ex)
        {
            // 退出动作不能因为审计/会话撤销异常而把应用留在半退出状态。
            System.Diagnostics.Debug.WriteLine($"退出应用前撤销会话失败：{ex}");
        }
        finally
        {
            Close();
        }
    }

    /// <summary>
    /// 按页面标题跳转，供界面上的快捷入口使用。
    /// </summary>
    private async void OnNavigateToTitleClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string title } || DataContext is not ShellViewModel shell)
            return;

        var item = shell.NavItems.FirstOrDefault(candidate => candidate.Title == title);
        if (item is not null)
            await shell.NavigateCommand.ExecuteAsync(item);
    }

    /// <summary>
    /// 打开产品型号选择弹窗，并把选择结果交给主视图模型。
    /// </summary>
    private async void OnProductSelectorClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel shell)
            return;

        try
        {
            var beforeOpen = await shell.CheckProductModelChangeAllowedAsync();
            if (!beforeOpen.Succeeded)
            {
                await ShowFeedbackAsync(beforeOpen);
                return;
            }

            var options = await shell.LoadProductModelOptionsAsync();
            var dialog = new ProductModelSelectionWindow
            {
                DataContext = new ProductModelSelectionViewModel(options)
            };
            var selected = await ShowDialogWithOverlayAsync<ProductModelSelectionOption>(dialog);
            if (selected is not null)
            {
                var result = await shell.TrySelectProductModelAsync(selected);
                if (!result.Succeeded)
                    await ShowFeedbackAsync(result);
            }
        }
        catch (Exception ex)
        {
            await ShowFeedbackAsync(OperationFeedback.Failure("产品型号选择未完成，请查看日志"));
            System.Diagnostics.Debug.WriteLine($"产品型号选择失败：{ex}");
        }
    }

    private void UpdateProcessProductInfoLayout()
    {
        if (ProcessProductInfoWide is null || ProcessProductInfoCompact is null)
            return;
        var width = WorkspaceRegion?.Bounds.Width ?? Bounds.Width;
        var compact = width > 0 && width < 1180;
        var isProcessPage = (DataContext as ShellViewModel)?.IsProcessMonitorPage == true;
        ProcessProductInfoWide.IsVisible = isProcessPage && !compact;
        ProcessProductInfoCompact.IsVisible = isProcessPage && compact;
        ProductInfoBar.MinHeight = isProcessPage && compact ? 104 : 70;
    }

    private async Task ShowFeedbackAsync(OperationFeedback feedback)
    {
        var dialog = new NoticeDialogWindow
        {
            DataContext = new NoticeDialogViewModel(
                feedback.Succeeded ? "操作成功" : "操作失败",
                feedback.Message,
                !feedback.Succeeded)
        };
        await ShowDialogWithOverlayAsync<object?>(dialog);
    }

    /// <summary>
    /// 刷新顶栏显示的日期和时间。
    /// </summary>
    private void UpdateClock()
    {
        var now = DateTime.Now;
        CurrentDateTextBlock.Text = now.ToString("yyyy-MM-dd");
        CurrentTimeTextBlock.Text = now.ToString("HH:mm:ss");
    }

    /// <summary>
    /// 带遮罩显示模态弹窗，关闭后隐藏遮罩。
    /// </summary>
    public async Task<TResult?> ShowDialogWithOverlayAsync<TResult>(Window dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);

        DialogOverlay.IsVisible = true;
        try
        {
            return await dialog.ShowDialog<TResult>(this);
        }
        finally
        {
            DialogOverlay.IsVisible = false;
        }
    }
}
