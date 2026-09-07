using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using XXX.TestBench.App.ViewModels;
using XXX.TestBench.Core.Application;

namespace XXX.TestBench.App.Views;

/// <summary>
/// 通道和设备候选配置窗口。
/// </summary>
public partial class DeviceConfigurationDialogWindow : Window
{
    public DeviceConfigurationDialogWindow()
    {
        InitializeComponent();
        Icon = AppIconProvider.Create();
    }

    private void OnAddChannelClick(object? sender, RoutedEventArgs e)
        => (DataContext as DeviceConfigurationEditorViewModel)?.AddChannel();

    private void OnEditChannelClick(object? sender, RoutedEventArgs e)
        => (DataContext as DeviceConfigurationEditorViewModel)?.EditSelectedChannel();

    private void OnDeleteChannelClick(object? sender, RoutedEventArgs e)
        => (DataContext as DeviceConfigurationEditorViewModel)?.DeleteSelectedChannel();

    private void OnAddDeviceClick(object? sender, RoutedEventArgs e)
        => (DataContext as DeviceConfigurationEditorViewModel)?.AddDevice();

    private void OnEditDeviceClick(object? sender, RoutedEventArgs e)
        => (DataContext as DeviceConfigurationEditorViewModel)?.EditSelectedDevice();

    private void OnDeleteDeviceClick(object? sender, RoutedEventArgs e)
        => (DataContext as DeviceConfigurationEditorViewModel)?.DeleteSelectedDevice();

    private void OnCommitChannelClick(object? sender, RoutedEventArgs e)
        => (DataContext as DeviceConfigurationEditorViewModel)?.CommitChannelEditor();

    private void OnCommitDeviceClick(object? sender, RoutedEventArgs e)
        => (DataContext as DeviceConfigurationEditorViewModel)?.CommitDeviceEditor();

    private void OnCancelEditorClick(object? sender, RoutedEventArgs e)
        => (DataContext as DeviceConfigurationEditorViewModel)?.CancelEditor();

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DeviceConfigurationEditorViewModel viewModel)
            return;

        var result = await viewModel.SaveAsync();
        if (result.Ok)
            Close(result);
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
            BeginMoveDrag(e);
    }
}
