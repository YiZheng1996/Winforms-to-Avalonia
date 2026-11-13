using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using XXX.TestBench.App.ViewModels;

namespace XXX.TestBench.App.Views;

/// <summary>
/// 设备点位整体导入前的确认预览弹窗。
/// </summary>
public partial class DevicePointImportPreviewWindow : Window
{
    public DevicePointImportPreviewWindow()
    {
        InitializeComponent();
        Icon = AppIconProvider.Create();
    }

    private async void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DevicePointImportPreviewViewModel viewModel
            && viewModel.RequiresS7OptimizedBlockAccessConfirmation
            && viewModel.S7OptimizedBlockAccessNotice is { } notice)
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
        }

        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close(false);
            e.Handled = true;
        }
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
            BeginMoveDrag(e);
    }
}
