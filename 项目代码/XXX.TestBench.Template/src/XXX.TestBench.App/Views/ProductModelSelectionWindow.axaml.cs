using Avalonia.Controls;
using Avalonia.Interactivity;
using XXX.TestBench.App;
using XXX.TestBench.App.ViewModels;

namespace XXX.TestBench.App.Views;

public partial class ProductModelSelectionWindow : Window
{
    public ProductModelSelectionWindow()
    {
        InitializeComponent();
        Icon = AppIconProvider.Create();
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ProductModelSelectionViewModel viewModel
            && viewModel.SelectedOption is not null)
            Close(viewModel.SelectedOption);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
}
