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
                await ShowFeedbackAsync(owner, await viewModel.CreateTypeFromDialogAsync(result));
        }
        catch (Exception ex)
        {
            await ShowFeedbackAsync(owner, OperationFeedback.Failure(ex.Message));
        }
    }

    /// <summary>
    /// 打开编辑产品类型弹窗，修改成功后刷新页面数据。
    /// </summary>
    private async void OnEditTypeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ParameterManagementViewModel viewModel || GetOwner() is not { } owner)
            return;

        if (viewModel.SelectedType is not { } type)
        {
            await ShowFeedbackAsync(owner, OperationFeedback.Failure("请先选择产品类型"));
            return;
        }

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
                await ShowFeedbackAsync(owner, await viewModel.UpdateTypeFromDialogAsync(type.Id, result));
        }
        catch (Exception ex)
        {
            await ShowFeedbackAsync(owner, OperationFeedback.Failure(ex.Message));
        }
    }

    /// <summary>
    /// 启用或停用当前选中的产品类型。
    /// </summary>
    private async void OnToggleTypeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ParameterManagementViewModel viewModel || GetOwner() is not { } owner)
            return;

        if (viewModel.SelectedType is not { } type)
        {
            await ShowFeedbackAsync(owner, OperationFeedback.Failure("请先选择产品类型"));
            return;
        }

        var action = type.IsEnabled ? "停用" : "启用";
        if (await ConfirmAsync(owner, $"{action}产品类型", $"确定要{action}产品类型“{type.Name}”吗？", $"确认{action}") is not true)
            return;

        try
        {
            await ShowFeedbackAsync(owner, await viewModel.ToggleSelectedTypeAsync());
        }
        catch (Exception ex)
        {
            await ShowFeedbackAsync(owner, OperationFeedback.Failure(ex.Message));
        }
    }

    /// <summary>
    /// 删除当前选中的产品类型。
    /// </summary>
    private async void OnDeleteTypeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ParameterManagementViewModel viewModel || GetOwner() is not { } owner)
            return;

        if (viewModel.SelectedType is not { } type)
        {
            await ShowFeedbackAsync(owner, OperationFeedback.Failure("请先选择产品类型"));
            return;
        }

        const string messageSuffix = "如果该类型已有产品型号或试验项点，删除会被拒绝。";
        if (await ConfirmAsync(owner, "删除产品类型", $"确定删除产品类型“{type.Name}”吗？\n{messageSuffix}", "确认删除") is not true)
            return;

        try
        {
            await ShowFeedbackAsync(owner, await viewModel.DeleteSelectedTypeFromDialogAsync());
        }
        catch (Exception ex)
        {
            await ShowFeedbackAsync(owner, OperationFeedback.Failure(ex.Message));
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
                await ShowFeedbackAsync(owner, await viewModel.CreateModelFromDialogAsync(result));
        }
        catch (Exception ex)
        {
            await ShowFeedbackAsync(owner, OperationFeedback.Failure(ex.Message));
        }
    }

    /// <summary>
    /// 打开编辑产品型号弹窗，修改成功后刷新页面数据；所属产品类型保持原值。
    /// </summary>
    private async void OnEditModelClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ParameterManagementViewModel viewModel || GetOwner() is not { } owner)
            return;

        if (viewModel.SelectedModel is not { } model)
        {
            await ShowFeedbackAsync(owner, OperationFeedback.Failure("请先选择产品型号"));
            return;
        }

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
                await ShowFeedbackAsync(owner, await viewModel.UpdateModelFromDialogAsync(model.Id, result));
        }
        catch (Exception ex)
        {
            await ShowFeedbackAsync(owner, OperationFeedback.Failure(ex.Message));
        }
    }

    /// <summary>
    /// 启用或停用当前选中的产品型号。
    /// </summary>
    private async void OnToggleModelClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ParameterManagementViewModel viewModel || GetOwner() is not { } owner)
            return;

        if (viewModel.SelectedModel is not { } model)
        {
            await ShowFeedbackAsync(owner, OperationFeedback.Failure("请先选择产品型号"));
            return;
        }

        var action = model.IsEnabled ? "停用" : "启用";
        if (await ConfirmAsync(owner, $"{action}产品型号", $"确定要{action}产品型号“{model.Name}”吗？", $"确认{action}") is not true)
            return;

        try
        {
            await ShowFeedbackAsync(owner, await viewModel.ToggleSelectedModelAsync());
        }
        catch (Exception ex)
        {
            await ShowFeedbackAsync(owner, OperationFeedback.Failure(ex.Message));
        }
    }

    /// <summary>
    /// 删除当前选中的产品型号。
    /// </summary>
    private async void OnDeleteModelClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ParameterManagementViewModel viewModel || GetOwner() is not { } owner)
            return;

        if (viewModel.SelectedModel is not { } model)
        {
            await ShowFeedbackAsync(owner, OperationFeedback.Failure("请先选择产品型号"));
            return;
        }

        const string messageSuffix = "如果该型号已有试验记录，删除会被拒绝，请改用停用。";
        if (await ConfirmAsync(owner, "删除产品型号", $"确定删除产品型号“{model.Name}”吗？\n{messageSuffix}", "确认删除") is not true)
            return;

        try
        {
            await ShowFeedbackAsync(owner, await viewModel.DeleteSelectedModelFromDialogAsync());
        }
        catch (Exception ex)
        {
            await ShowFeedbackAsync(owner, OperationFeedback.Failure(ex.Message));
        }
    }

    /// <summary>
    /// 打开项目级参数编辑弹窗。
    /// </summary>
    private async void OnEditProjectParameterClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ParameterManagementViewModel viewModel || GetOwner() is not { } owner)
            return;

        await EditParameterAsync(
            owner,
            new ParameterValueDialogViewModel(
                "编辑项目级参数",
                "修改后立即对所有产品生效",
                "试验时间",
                "s",
                "1～3600",
                viewModel.TestTimeInput,
                integerOnly: true),
            viewModel.SaveProjectParameterFromDialogAsync);
    }

    /// <summary>
    /// 打开产品组合级参数编辑弹窗。
    /// </summary>
    private async void OnEditProductParameterClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ParameterManagementViewModel viewModel || GetOwner() is not { } owner)
            return;

        if (viewModel.SelectedParamModel is not { } model)
        {
            await ShowFeedbackAsync(owner, OperationFeedback.Failure("请先选择产品型号"));
            return;
        }

        var typeName = viewModel.ParamTypeOptions
            .FirstOrDefault(type => type.Id == model.ProductTypeId)?.Name
            ?? $"编号 {model.ProductTypeId}";
        try
        {
            var dialog = new ProductParameterDialogWindow
            {
                DataContext = new ProductParameterDialogViewModel(
                    "编辑产品参数",
                    $"产品类型：{typeName}  ·  产品型号：{model.Name}",
                    viewModel.TestVoltageInput,
                    viewModel.ProtectCurrentInput)
            };
            var result = await ShowDialogAsync<ProductParameterDialogResult>(owner, dialog);
            if (result is not null)
            {
                await ShowFeedbackAsync(
                    owner,
                    await viewModel.SaveProductParameterFromDialogAsync(result.TestVoltage, result.ProtectCurrent));
            }
        }
        catch (Exception ex)
        {
            await ShowFeedbackAsync(owner, OperationFeedback.Failure(ex.Message));
        }
    }

    private async Task EditParameterAsync(
        Window owner,
        ParameterValueDialogViewModel viewModel,
        Func<string, Task<OperationFeedback>> saveAsync)
    {
        try
        {
            var dialog = new ParameterValueDialogWindow { DataContext = viewModel };
            var value = await ShowDialogAsync<string>(owner, dialog);
            if (value is not null)
                await ShowFeedbackAsync(owner, await saveAsync(value));
        }
        catch (Exception ex)
        {
            await ShowFeedbackAsync(owner, OperationFeedback.Failure(ex.Message));
        }
    }

    /// <summary>
    /// 获取承载当前页面的窗口。
    /// </summary>
    private Window? GetOwner() => TopLevel.GetTopLevel(this) as Window;

    private async Task<bool?> ConfirmAsync(Window owner, string title, string message, string confirmText)
    {
        var dialog = new ConfirmDialogWindow
        {
            DataContext = new ConfirmDialogViewModel(title, message, confirmText)
        };
        return await ShowDialogAsync<bool>(owner, dialog);
    }

    private static async Task ShowFeedbackAsync(Window owner, OperationFeedback feedback)
    {
        var dialog = new NoticeDialogWindow
        {
            DataContext = new NoticeDialogViewModel(
                feedback.Succeeded ? "操作成功" : "操作失败",
                feedback.Message,
                !feedback.Succeeded)
        };
        await ShowDialogAsync<object?>(owner, dialog);
    }

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
