using Avalonia.Controls;
using Avalonia.Threading;
using XXX.TestBench.App.ViewModels;

namespace XXX.TestBench.App.Views;

/// <summary>
/// 设备校准页面视图。
/// </summary>
public partial class DeviceCalibrationView : UserControl
{
    private DispatcherTimer? _timer;

    /// <summary>
    /// 初始化设备校准页面。
    /// </summary>
    public DeviceCalibrationView() => InitializeComponent();

    /// <summary>
    /// 页面挂载后启动定时刷新。
    /// </summary>
    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += async (_, _) => { if (DataContext is DeviceCalibrationViewModel vm) await vm.RefreshAsync(); };
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
