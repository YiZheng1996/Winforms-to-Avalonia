using Avalonia.Controls;
using Avalonia.Interactivity;
using XXX.TestBench.App;
using XXX.TestBench.App.ViewModels;

namespace XXX.TestBench.App.Views;

/// <summary>
/// 试验项点管理页面视图。
/// </summary>
public partial class TestPointManagementView : UserControl
{
    /// <summary>
    /// 初始化试验项点管理页面。
    /// </summary>
    public TestPointManagementView() => InitializeComponent();

    /// <summary>
    /// 打开新增弹窗，创建成功后刷新页面数据。
    /// </summary>
    private async void OnAddPointClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TestPointManagementViewModel viewModel || GetOwner() is not { } owner)
            return;

        try
        {
            var dialog = new TestPointDialogWindow
            {
                DataContext = new TestPointDialogViewModel(false, null, string.Empty, string.Empty, 1, true, viewModel.ExecutorCodes)
            };
            var result = await ShowDialogAsync<TestPointDialogResult>(owner, dialog);
            if (result is not null)
                await ShowFeedbackAsync(owner, await viewModel.CreateFromDialogAsync(result));
        }
        catch (Exception ex)
        {
            await ShowFeedbackAsync(owner, OperationFeedback.Failure(ex.Message));
        }
    }

    /// <summary>
    /// 打开编辑弹窗，修改成功后刷新页面数据。
    /// </summary>
    private async void OnEditPointClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TestPointManagementViewModel viewModel || GetOwner() is not { } owner)
            return;

        if (viewModel.SelectedPoint is not { } point)
        {
            await ShowFeedbackAsync(owner, OperationFeedback.Failure("请先选择试验项点"));
            return;
        }

        try
        {
            var dialog = new TestPointDialogWindow
            {
                DataContext = new TestPointDialogViewModel(true, point.Id, point.Name, point.ExecutorCode, point.SortOrder, point.IsEnabled, viewModel.ExecutorCodes)
            };
            var result = await ShowDialogAsync<TestPointDialogResult>(owner, dialog);
            if (result is not null)
                await ShowFeedbackAsync(owner, await viewModel.UpdateFromDialogAsync(point.Id, result));
        }
        catch (Exception ex)
        {
            await ShowFeedbackAsync(owner, OperationFeedback.Failure(ex.Message));
        }
    }

    /// <summary>
    /// 启用或停用当前选中的试验项点。
    /// </summary>
    private async void OnTogglePointClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TestPointManagementViewModel viewModel || GetOwner() is not { } owner)
            return;

        if (viewModel.SelectedPoint is not { } point)
        {
            await ShowFeedbackAsync(owner, OperationFeedback.Failure("请先选择试验项点"));
            return;
        }

        var action = point.IsEnabled ? "停用" : "启用";
        if (await ConfirmAsync(owner, $"{action}试验项点", $"确定要{action}试验项点“{point.Name}”吗？", $"确认{action}") is not true)
            return;

        try
        {
            await ShowFeedbackAsync(owner, await viewModel.TogglePointFromDialogAsync());
        }
        catch (Exception ex)
        {
            await ShowFeedbackAsync(owner, OperationFeedback.Failure(ex.Message));
        }
    }

    /// <summary>
    /// 删除当前选中的试验项点。
    /// </summary>
    private async void OnDeletePointClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TestPointManagementViewModel viewModel || GetOwner() is not { } owner)
            return;

        if (viewModel.SelectedPoint is not { } point)
        {
            await ShowFeedbackAsync(owner, OperationFeedback.Failure("请先选择试验项点"));
            return;
        }

        const string messageSuffix = "如果该项点已被型号配置或试验记录引用，删除会被拒绝，请改用停用。";
        if (await ConfirmAsync(owner, "删除试验项点", $"确定删除试验项点“{point.Name}”吗？\n{messageSuffix}", "确认删除") is not true)
            return;

        try
        {
            await ShowFeedbackAsync(owner, await viewModel.DeletePointFromDialogAsync());
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
            throw new InvalidOperationException("TestPointManagementView 必须由 MainWindow 承载，才能显示弹窗遮罩。");

        return await mainWindow.ShowDialogWithOverlayAsync<TResult>(dialog);
    }
}
