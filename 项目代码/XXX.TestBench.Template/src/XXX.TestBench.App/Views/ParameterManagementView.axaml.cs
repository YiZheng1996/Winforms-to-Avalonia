using Avalonia.Controls;
using Avalonia.Interactivity;
using XXX.TestBench.App;
using XXX.TestBench.App.ViewModels;

namespace XXX.TestBench.App.Views;

/// <summary>
/// 参数管理页面视图。
/// </summary>
public partial class ParameterManagementView : UserControl
{
    /// <summary>
    /// 初始化参数管理页面。
    /// </summary>
    public ParameterManagementView() => InitializeComponent();

    /// <summary>
    /// 打开新增产品类型弹窗，创建成功后刷新页面数据。
    /// </summary>
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

    /// <summary>
    /// 打开编辑产品类型弹窗，修改成功后刷新页面数据。
    /// </summary>
    private async void OnEditTypeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ParameterManagementViewModel viewModel
            || viewModel.SelectedType is not { } type
            || GetOwner() is not { } owner)
            return;

        try
        {
            var dialog = new ProductMasterDataDialogWindow
            {
                DataContext = new ProductMasterDataDialogViewModel(
                    false,
                    viewModel.ModelTypeOptions,
                    isEdit: true,
                    name: type.Name)
            };
            var result = await ShowDialogAsync<ProductMasterDataDialogResult>(owner, dialog);
            if (result is not null)
                await viewModel.UpdateTypeFromDialogAsync(type.Id, result);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"编辑产品类型失败：{ex}");
        }
    }

    /// <summary>
    /// 打开新增产品型号弹窗，创建成功后刷新页面数据。
    /// </summary>
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

    /// <summary>
    /// 打开编辑产品型号弹窗，修改成功后刷新页面数据；所属产品类型保持原值。
    /// </summary>
    private async void OnEditModelClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ParameterManagementViewModel viewModel
            || viewModel.SelectedModel is not { } model
            || GetOwner() is not { } owner)
            return;

        try
        {
            var dialog = new ProductMasterDataDialogWindow
            {
                DataContext = new ProductMasterDataDialogViewModel(
                    true,
                    viewModel.ModelTypeOptions,
                    isEdit: true,
                    name: model.Name,
                    typeId: model.ProductTypeId)
            };
            var result = await ShowDialogAsync<ProductMasterDataDialogResult>(owner, dialog);
            if (result is not null)
                await viewModel.UpdateModelFromDialogAsync(model.Id, result);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"编辑产品型号失败：{ex}");
        }
    }

    /// <summary>
    /// 获取承载当前页面的窗口。
    /// </summary>
    private Window? GetOwner() => TopLevel.GetTopLevel(this) as Window;

    /// <summary>
    /// 通过主窗口带遮罩显示弹窗。
    /// </summary>
    private static async Task<TResult?> ShowDialogAsync<TResult>(Window owner, Window dialog)
    {
        if (owner is not MainWindow mainWindow)
            throw new InvalidOperationException("ParameterManagementView 必须由 MainWindow 承载，才能显示弹窗遮罩。");

        return await mainWindow.ShowDialogWithOverlayAsync<TResult>(dialog);
    }
}
