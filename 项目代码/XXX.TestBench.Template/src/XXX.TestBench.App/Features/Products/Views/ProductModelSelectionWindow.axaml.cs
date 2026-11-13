using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using XXX.TestBench.App;
using XXX.TestBench.App.ViewModels;

namespace XXX.TestBench.App.Views;

/// <summary>
/// 产品型号选择弹窗。
/// </summary>
public partial class ProductModelSelectionWindow : Window
{
    /// <summary>
    /// 初始化弹窗。
    /// </summary>
    public ProductModelSelectionWindow()
    {
        InitializeComponent();
        Icon = AppIconProvider.Create();
    }

    /// <summary>
    /// 确认按钮：关闭弹窗并返回当前选中的型号。
    /// </summary>
    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ProductModelSelectionViewModel viewModel
            && viewModel.SelectedOption is not null)
            Close(viewModel.SelectedOption);
    }

    /// <summary>
    /// 取消按钮：直接关闭弹窗。
    /// </summary>
    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);

    /// <summary>
    /// 按下标题栏时拖动窗口。
    /// </summary>
    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
            BeginMoveDrag(e);
    }
}
