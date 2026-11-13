using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using XXX.TestBench.App.ViewModels;
using XXX.TestBench.App.ViewModels.Process;
using XXX.TestBench.Core.Application;

namespace XXX.TestBench.App.Views;

/// <summary>
/// 过程监视页面视图：页面只设置一个 250 ms 的 UI 缓存刷新定时器。
/// </summary>
public partial class ProcessMonitorView : UserControl
{
    private DispatcherTimer? _timer;
    private ProcessMonitorViewModel? _observedViewModel;
    private bool _isAttached;

    public ProcessMonitorView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        SizeChanged += (_, _) => UpdateResponsiveLayout();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        AttachViewModel(DataContext as ProcessMonitorViewModel);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += OnRefreshTick;
        _timer.Start();
        UpdateResponsiveLayout();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        _timer?.Stop();
        if (_timer is not null)
            _timer.Tick -= OnRefreshTick;
        _timer = null;
        DetachViewModel();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_isAttached)
            AttachViewModel(DataContext as ProcessMonitorViewModel);
    }

    private void AttachViewModel(ProcessMonitorViewModel? viewModel)
    {
        if (ReferenceEquals(_observedViewModel, viewModel))
            return;
        DetachViewModel();
        _observedViewModel = viewModel;
        if (_observedViewModel is not null)
        {
            _observedViewModel.ConfirmationHandler = ConfirmWriteAsync;
            _observedViewModel.ActivateRuntimeObservation();
        }
    }

    private void DetachViewModel()
    {
        if (_observedViewModel is null)
            return;
        _observedViewModel.ConfirmationHandler = null;
        _observedViewModel.DeactivateRuntimeObservation();
        _observedViewModel = null;
    }

    private async void OnRefreshTick(object? sender, EventArgs e)
    {
        if (!_isAttached || DataContext is not ProcessMonitorViewModel viewModel)
            return;
        await viewModel.RefreshAsync();
    }

    private void UpdateResponsiveLayout()
    {
        if (CardGridWide is null || CardGridCompact is null)
            return;
        var compact = Bounds.Width > 0 && Bounds.Width < 1180;
        CardGridWide.IsVisible = !compact;
        CardGridCompact.IsVisible = compact;
    }

    private async void OnPointBindingsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ProcessMonitorViewModel viewModel
            || !viewModel.CanManageProcessBindings
            || GetOwner() is not MainWindow owner)
            return;

        try
        {
            var dialog = new ProcessPointBindingsDialogWindow
            {
                DataContext = viewModel.CreateProcessBindingsDialog()
            };
            var result = await owner.ShowDialogWithOverlayAsync<DeviceConfigurationApplyResult>(dialog);
            if (result?.Ok == true)
                await viewModel.LoadAsync();
        }
        catch (Exception ex)
        {
            await ShowFeedbackAsync(owner, OperationFeedback.Failure("点位绑定未完成，请查看日志"));
            System.Diagnostics.Debug.WriteLine($"工艺点位绑定失败：{ex}");
        }
    }

    private async Task<bool> ConfirmWriteAsync(string title, string message, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (GetOwner() is not MainWindow owner)
            return false;
        var dialog = new ConfirmDialogWindow
        {
            DataContext = new ConfirmDialogViewModel(title, message, "确认操作")
        };
        return await owner.ShowDialogWithOverlayAsync<bool>(dialog) is true;
    }

    private static async Task ShowFeedbackAsync(MainWindow owner, OperationFeedback feedback)
    {
        var dialog = new NoticeDialogWindow
        {
            DataContext = new NoticeDialogViewModel(
                feedback.Succeeded ? "操作成功" : "操作失败",
                feedback.Message,
                !feedback.Succeeded)
        };
        await owner.ShowDialogWithOverlayAsync<object?>(dialog);
    }

    private MainWindow? GetOwner()
        => TopLevel.GetTopLevel(this) as MainWindow;
}
