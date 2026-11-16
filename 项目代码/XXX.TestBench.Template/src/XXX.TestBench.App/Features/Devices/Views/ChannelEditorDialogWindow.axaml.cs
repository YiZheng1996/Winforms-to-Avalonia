using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using XXX.TestBench.App.ViewModels;
using XXX.TestBench.Core.Configuration;

namespace XXX.TestBench.App.Views;

/// <summary>
/// 独立通道编辑窗口。
/// </summary>
public partial class ChannelEditorDialogWindow : Window
{
    public ChannelEditorDialogWindow()
    {
        InitializeComponent();
        Icon = AppIconProvider.Create();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);

    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ChannelEditorViewModel viewModel)
            viewModel.MoveBack();
    }

    private void OnNextClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ChannelEditorViewModel viewModel)
            return;

        if (viewModel.CurrentStep < ChannelEditorViewModel.WizardStepCount)
        {
            viewModel.MoveNext();
            return;
        }

        OnConfirmClick(sender, e);
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ChannelEditorViewModel viewModel
            && viewModel.TryBuild(out var entry))
            Close(new ChannelEditorDialogResult(entry));
    }

    private void OnTcpChecked(object? sender, RoutedEventArgs e) => SelectTransport(ChannelTransportKind.Tcp);

    private void OnSerialChecked(object? sender, RoutedEventArgs e) => SelectTransport(ChannelTransportKind.Serial);

    private void OnSimulationChecked(object? sender, RoutedEventArgs e) => SelectTransport(ChannelTransportKind.Simulation);

    private void SelectTransport(ChannelTransportKind kind)
    {
        if (DataContext is ChannelEditorViewModel viewModel && viewModel.TransportKind != kind)
            viewModel.TransportKind = kind;
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
            BeginMoveDrag(e);
    }
}
