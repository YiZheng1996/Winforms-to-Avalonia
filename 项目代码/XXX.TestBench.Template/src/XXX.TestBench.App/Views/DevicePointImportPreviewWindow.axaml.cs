using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

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

    private void OnConfirmClick(object? sender, RoutedEventArgs e) => Close(true);
    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
            BeginMoveDrag(e);
    }
}
