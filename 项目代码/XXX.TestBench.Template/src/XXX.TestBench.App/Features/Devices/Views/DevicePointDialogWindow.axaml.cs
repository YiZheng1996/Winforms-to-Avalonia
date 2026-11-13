using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using XXX.TestBench.App.ViewModels;

namespace XXX.TestBench.App.Views;

/// <summary>
/// 设备点位编辑弹窗。
/// </summary>
public partial class DevicePointDialogWindow : Window
{
    /// <summary>
    /// 初始化弹窗。
    /// </summary>
    public DevicePointDialogWindow()
    {
        InitializeComponent();
        Icon = AppIconProvider.Create();
    }

    /// <summary>
    /// 确认按钮：校验输入并关闭弹窗返回结果。
    /// </summary>
    private async void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DevicePointDialogViewModel viewModel
            || !viewModel.TryBuildResult(out var result))
            return;

        if (viewModel.RequiresS7OptimizedBlockAccessConfirmation
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

            result = result with { S7OptimizedBlockAccessConfirmed = true };
        }

        Close(result);
    }

    /// <summary>
    /// 取消按钮：直接关闭弹窗。
    /// </summary>
    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close(null);
            e.Handled = true;
        }
    }

    /// <summary>
    /// 按下标题栏时拖动窗口。
    /// </summary>
    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Button)
            return;

        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
            BeginMoveDrag(e);
    }
}
