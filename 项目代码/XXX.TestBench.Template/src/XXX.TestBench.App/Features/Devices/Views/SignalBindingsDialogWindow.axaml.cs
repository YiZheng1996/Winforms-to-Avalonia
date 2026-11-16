using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using XXX.TestBench.App.ViewModels;
using XXX.TestBench.Core.Application;

namespace XXX.TestBench.App.Views;

/// <summary>
/// 项目级业务信号绑定窗口。
/// </summary>
public partial class SignalBindingsDialogWindow : Window
{
    /// <summary>
    /// 页面宿主提供的受页面门保护的保存入口。
    /// </summary>
    public Func<CancellationToken, bool, Task<DeviceConfigurationApplyResult>>? SaveHandler { get; init; }

    public SignalBindingsDialogWindow()
    {
        InitializeComponent();
        Icon = AppIconProvider.Create();
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SignalBindingsDialogViewModel viewModel)
            return;

        if (SaveHandler is null)
        {
            viewModel.ReportSaveUnavailable();
            return;
        }
        var result = await SaveHandler(CancellationToken.None, false);
        if (!result.Ok
            && result.RequiresS7OptimizedBlockAccessConfirmation
            && result.S7OptimizedBlockAccessNotice is { } notice)
        {
            var confirmation = new ConfirmDialogWindow
            {
                DataContext = new ConfirmDialogViewModel(
                    notice.ConfirmationTitle,
                    notice.ConfirmationMessage,
                    notice.ConfirmButtonText)
            };
            if (await confirmation.ShowDialog<bool>(this) is not true)
                return;

            result = await SaveHandler(CancellationToken.None, true);
        }

        if (result.Ok)
            Close(result);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
            BeginMoveDrag(e);
    }
}
