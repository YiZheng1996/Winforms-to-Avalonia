using Avalonia.Controls;
using Avalonia.Interactivity;
using XXX.TestBench.App;
using XXX.TestBench.App.ViewModels;

namespace XXX.TestBench.App.Views;

/// <summary>
/// 项点配置页面视图。
/// </summary>
public partial class PointConfigurationView : UserControl
{
    /// <summary>
    /// 初始化页面。
    /// </summary>
    public PointConfigurationView() => InitializeComponent();

    /// <summary>
    /// 移除已配置项点前进行确认，并在操作完成后用弹窗反馈结果。
    /// </summary>
    private async void OnRemoveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PointConfigurationViewModel viewModel || GetOwner() is not { } owner)
            return;

        if (viewModel.SelectedConfigured is not { } point)
        {
            await ShowFeedbackAsync(owner, OperationFeedback.Failure("请先选择已配置项点"));
            return;
        }

        if (await ConfirmAsync(owner, "移除配置项点", $"确定将“{point.Name}”移出当前型号的试验序列吗？\n移除后需要点击“保存配置”才会写入数据库。", "确认移除") is not true)
            return;

        await ShowFeedbackAsync(owner, await viewModel.RemoveSelectedFromPageAsync());
    }

    /// <summary>
    /// 保存型号项点配置，并以弹窗反馈结果。
    /// </summary>
    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PointConfigurationViewModel viewModel || GetOwner() is not { } owner)
            return;

        try
        {
            await ShowFeedbackAsync(owner, await viewModel.SaveFromDialogAsync());
        }
        catch (Exception ex)
        {
            await ShowFeedbackAsync(owner, OperationFeedback.Failure(ex.Message));
        }
    }

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

    private static async Task<TResult?> ShowDialogAsync<TResult>(Window owner, Window dialog)
    {
        if (owner is not MainWindow mainWindow)
            throw new InvalidOperationException("PointConfigurationView 必须由 MainWindow 承载，才能显示弹窗遮罩。");

        return await mainWindow.ShowDialogWithOverlayAsync<TResult>(dialog);
    }
}
