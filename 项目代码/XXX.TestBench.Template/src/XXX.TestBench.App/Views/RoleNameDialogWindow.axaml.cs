using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using XXX.TestBench.App.ViewModels;

namespace XXX.TestBench.App.Views;

/// <summary>
/// 角色名称弹窗。
/// </summary>
public partial class RoleNameDialogWindow : Window
{
    public RoleNameDialogWindow()
    {
        InitializeComponent();
        Icon = AppIconProvider.Create();
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is RoleNameDialogViewModel viewModel && viewModel.TryBuildResult(out var name))
            Close(name);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
            BeginMoveDrag(e);
    }
}
