using Avalonia.Controls;
using Avalonia.Interactivity;
using XXX.TestBench.App;
using XXX.TestBench.App.ViewModels;

namespace XXX.TestBench.App.Views;

public partial class ParameterManagementView : UserControl
{
    public ParameterManagementView() => InitializeComponent();

    private async void OnAddTypeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ParameterManagementViewModel viewModel || GetOwner() is not { } owner)
            return;

        try
        {
            var dialog = new ProductMasterDataDialogWindow
            {
                DataContext = new ProductMasterDataDialogViewModel(false, viewModel.ModelTypeOptions)
            };
            var result = await ShowDialogAsync<ProductMasterDataDialogResult>(owner, dialog);
            if (result is not null)
                await viewModel.CreateTypeFromDialogAsync(result);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"新增产品类型失败：{ex}");
        }
    }

    private async void OnAddModelClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ParameterManagementViewModel viewModel || GetOwner() is not { } owner)
            return;

        try
        {
            var dialog = new ProductMasterDataDialogWindow
            {
                DataContext = new ProductMasterDataDialogViewModel(
                    true,
                    viewModel.ModelTypeOptions.Where(type => type.IsEnabled))
            };
            var result = await ShowDialogAsync<ProductMasterDataDialogResult>(owner, dialog);
            if (result is not null)
                await viewModel.CreateModelFromDialogAsync(result);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"新增产品型号失败：{ex}");
        }
    }

    private Window? GetOwner() => TopLevel.GetTopLevel(this) as Window;

    private static async Task<TResult?> ShowDialogAsync<TResult>(Window owner, Window dialog)
    {
        if (owner is not MainWindow mainWindow)
            throw new InvalidOperationException("ParameterManagementView 必须由 MainWindow 承载，才能显示弹窗遮罩。");

        return await mainWindow.ShowDialogWithOverlayAsync<TResult>(dialog);
    }
}
