using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using XXX.TestBench.App.ViewModels;

namespace XXX.TestBench.App.Views;

/// <summary>
/// 产品组合级试验参数编辑弹窗。
/// </summary>
public partial class ProductParameterDialogWindow : Window
{
    public ProductParameterDialogWindow()
    {
        InitializeComponent();
        Icon = AppIconProvider.Create();
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ProductParameterDialogViewModel viewModel
            && viewModel.TryBuildResult(out var result))
            Close(result);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
            BeginMoveDrag(e);
    }
}
