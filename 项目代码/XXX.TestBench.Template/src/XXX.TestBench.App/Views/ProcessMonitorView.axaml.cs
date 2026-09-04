using Avalonia.Controls;
using Avalonia.Threading;
using XXX.TestBench.App.ViewModels;

namespace XXX.TestBench.App.Views;

/// <summary>
/// 过程监视页面视图。
/// </summary>
public partial class ProcessMonitorView : UserControl
{
    private DispatcherTimer? _timer;

    /// <summary>
    /// 初始化过程监视页面。
    /// </summary>
    public ProcessMonitorView() => InitializeComponent();

    /// <summary>
    /// 页面挂载后启动定时刷新。
    /// </summary>
    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += async (_, _) => { if (DataContext is ProcessMonitorViewModel vm) await vm.RefreshAsync(); };
        _timer.Start();
    }

    /// <summary>
    /// 页面卸载后停止定时刷新。
    /// </summary>
    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _timer?.Stop();
        _timer = null;
        base.OnDetachedFromVisualTree(e);
    }
}
