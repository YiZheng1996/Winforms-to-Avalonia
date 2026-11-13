using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using XXX.TestBench.App;
using XXX.TestBench.App.ViewModels;

namespace XXX.TestBench.App.Views;

/// <summary>
/// 产品主数据编辑弹窗，用于新增或编辑产品类型与型号。
/// </summary>
public partial class ProductMasterDataDialogWindow : Window
{
    /// <summary>
    /// 初始化弹窗。
    /// </summary>
    public ProductMasterDataDialogWindow()
    {
        InitializeComponent();
        Icon = AppIconProvider.Create();
    }

    /// <summary>
    /// 确认按钮：校验输入并关闭弹窗返回结果。
    /// </summary>
    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ProductMasterDataDialogViewModel viewModel
            && viewModel.TryBuildResult(out var result))
            Close(result);
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
