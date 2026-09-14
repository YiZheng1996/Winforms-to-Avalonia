using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using XXX.TestBench.App.ViewModels;

namespace XXX.TestBench.App.Views;

/// <summary>
/// 只读点位诊断窗口。读取失败和旧 Revision 丢弃都直接显示在结果行内。
/// </summary>
public partial class DevicePointDiagnosticsWindow : Window
{
    public DevicePointDiagnosticsWindow()
    {
        InitializeComponent();
        Icon = AppIconProvider.Create();
    }

    private async void OnReadSelectedClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DevicePointDiagnosticsViewModel viewModel)
            return;
        await viewModel.ReadSelectedAsync();
    }

    private async void OnReadScopeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DevicePointDiagnosticsViewModel viewModel)
            return;
        await viewModel.ReadScopeAsync();
    }

    private void OnCancelReadClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DevicePointDiagnosticsViewModel viewModel)
            viewModel.CancelCurrentRead();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (DataContext is DevicePointDiagnosticsViewModel viewModel)
                viewModel.CancelCurrentRead();
            Close();
            e.Handled = true;
        }
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
            BeginMoveDrag(e);
    }
}
