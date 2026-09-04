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
                await viewModel.CreateFromDialogAsync(result);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"新增试验项点失败：{ex}");
        }
    }

    /// <summary>
    /// 打开编辑弹窗，修改成功后刷新页面数据。
    /// </summary>
    private async void OnEditPointClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TestPointManagementViewModel viewModel || viewModel.SelectedPoint is not { } point || GetOwner() is not { } owner)
            return;

        try
        {
            var dialog = new TestPointDialogWindow
            {
                DataContext = new TestPointDialogViewModel(true, point.Id, point.Name, point.ExecutorCode, point.SortOrder, point.IsEnabled, viewModel.ExecutorCodes)
            };
            var result = await ShowDialogAsync<TestPointDialogResult>(owner, dialog);
            if (result is not null)
                await viewModel.UpdateFromDialogAsync(point.Id, result);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"编辑试验项点失败：{ex}");
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
            throw new InvalidOperationException("TestPointManagementView 必须由 MainWindow 承载，才能显示弹窗遮罩。");

        return await mainWindow.ShowDialogWithOverlayAsync<TResult>(dialog);
    }
}
