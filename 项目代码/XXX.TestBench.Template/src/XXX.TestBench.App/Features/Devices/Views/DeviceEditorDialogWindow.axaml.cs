using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using XXX.TestBench.App.ViewModels;

namespace XXX.TestBench.App.Views;

/// <summary>
/// 独立设备编辑窗口。
/// </summary>
public partial class DeviceEditorDialogWindow : Window
{
    public DeviceEditorDialogWindow()
    {
        InitializeComponent();
        Icon = AppIconProvider.Create();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);

    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DeviceEditorViewModel viewModel)
            viewModel.MoveBack();
    }

    private void OnNextClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DeviceEditorViewModel viewModel)
            return;

        if (viewModel.CurrentStep < DeviceEditorViewModel.WizardStepCount)
        {
            viewModel.MoveNext();
            return;
        }

        OnConfirmClick(sender, e);
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close(null);
            e.Handled = true;
        }
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DeviceEditorViewModel viewModel
            && viewModel.TryBuild(out var entry))
            Close(new DeviceEditorDialogResult(entry));
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Button)
            return;

        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
            BeginMoveDrag(e);
    }
}
