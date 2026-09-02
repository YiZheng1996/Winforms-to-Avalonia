using Avalonia.Controls;
using Avalonia.Interactivity;
using XXX.TestBench.App;
using XXX.TestBench.App.ViewModels;

namespace XXX.TestBench.App.Views;

public partial class ParameterDefinitionDialogWindow : Window
{
    public ParameterDefinitionDialogWindow()
    {
        InitializeComponent();
        Icon = AppIconProvider.Create();
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ParameterDefinitionDialogViewModel viewModel
            && viewModel.TryBuildResult(out var result))
            Close(result);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
}
