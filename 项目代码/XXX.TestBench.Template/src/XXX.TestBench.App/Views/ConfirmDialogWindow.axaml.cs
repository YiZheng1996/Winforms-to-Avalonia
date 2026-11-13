using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using XXX.TestBench.App.ViewModels;

namespace XXX.TestBench.App.Views;

/// <summary>
/// 通用危险操作确认弹窗。
/// </summary>
public partial class ConfirmDialogWindow : Window
{
    public ConfirmDialogWindow()
    {
        InitializeComponent();
        Icon = AppIconProvider.Create();
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e) => Close(true);
    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
            BeginMoveDrag(e);
    }
}
