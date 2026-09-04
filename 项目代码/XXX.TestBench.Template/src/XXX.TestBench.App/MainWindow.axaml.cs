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

    /// <summary>
    /// 初始化主窗口并启动时钟。
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        UpdateClock();
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
        Closed += (_, _) => _clockTimer.Stop();
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
            var options = await shell.LoadProductModelOptionsAsync();
            var dialog = new ProductModelSelectionWindow
            {
                DataContext = new ProductModelSelectionViewModel(options)
            };
            var selected = await ShowDialogWithOverlayAsync<ProductModelSelectionOption>(dialog);
            if (selected is not null)
                shell.SelectProductModel(selected);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"产品型号选择失败：{ex}");
        }
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
