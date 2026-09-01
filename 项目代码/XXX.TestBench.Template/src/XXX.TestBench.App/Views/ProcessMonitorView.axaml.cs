using Avalonia.Controls;
using Avalonia.Threading;
using XXX.TestBench.App.ViewModels;

namespace XXX.TestBench.App.Views;

public partial class ProcessMonitorView : UserControl
{
    private DispatcherTimer? _timer;

    public ProcessMonitorView() => InitializeComponent();

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += async (_, _) => { if (DataContext is ProcessMonitorViewModel vm) await vm.RefreshAsync(); };
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _timer?.Stop();
        _timer = null;
        base.OnDetachedFromVisualTree(e);
    }
}
