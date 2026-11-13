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
    public SignalBindingsDialogWindow()
    {
        InitializeComponent();
        Icon = AppIconProvider.Create();
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SignalBindingsDialogViewModel viewModel)
            return;

        var result = await viewModel.SaveAsync();
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

            result = await viewModel.SaveAsync(s7OptimizedBlockAccessConfirmed: true);
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
