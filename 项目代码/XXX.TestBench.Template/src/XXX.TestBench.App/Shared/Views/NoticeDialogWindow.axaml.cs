using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace XXX.TestBench.App.Views;

/// <summary>
/// 统一展示参数管理操作成功或失败结果的弹窗。
/// </summary>
public partial class NoticeDialogWindow : Window
{
    public NoticeDialogWindow()
    {
        InitializeComponent();
        Icon = AppIconProvider.Create();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
            BeginMoveDrag(e);
    }
}
