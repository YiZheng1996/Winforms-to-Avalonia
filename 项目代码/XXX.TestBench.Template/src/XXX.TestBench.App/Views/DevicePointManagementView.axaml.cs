using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using XXX.TestBench.App.ViewModels;
using XXX.TestBench.Core.Configuration;

namespace XXX.TestBench.App.Views;

/// <summary>
/// 设备点位管理页面视图。
/// </summary>
public partial class DevicePointManagementView : UserControl
{
    /// <summary>
    /// 初始化设备点位管理页面。
    /// </summary>
    public DevicePointManagementView() => InitializeComponent();

    /// <summary>
    /// 选择保存位置并下载固定格式的设备点位导入模板。
    /// </summary>
    private async void OnDownloadTemplateClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DevicePointManagementViewModel viewModel
            || TopLevel.GetTopLevel(this) is not { StorageProvider: { } storageProvider })
            return;

        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "下载设备点位导入模板",
            SuggestedFileName = DevicePointTemplateDefinition.DefaultFileName,
            DefaultExtension = "xlsx",
            ShowOverwritePrompt = true,
            FileTypeChoices =
            [
                new FilePickerFileType("Excel 点位模板")
                {
                    Patterns = ["*.xlsx"]
                }
            ]
        });
        var path = file?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
            await viewModel.DownloadTemplateAsync(path);
    }

    /// <summary>
    /// 打开新增弹窗，创建成功后刷新页面数据。
    /// </summary>
    private async void OnAddPointClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DevicePointManagementViewModel viewModel || GetOwner() is not { } owner)
            return;

        var dialog = new DevicePointDialogWindow
        {
            DataContext = new DevicePointDialogViewModel(false, null)
        };
        var result = await ShowDialogAsync<DevicePointDialogResult>(owner, dialog);
        if (result is not null)
            await viewModel.CreateFromDialogAsync(result);
    }

    /// <summary>
    /// 打开编辑弹窗，修改成功后刷新页面数据。
    /// </summary>
    private async void OnEditPointClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DevicePointManagementViewModel viewModel
            || viewModel.SelectedPoint is not { } row
            || GetOwner() is not { } owner)
            return;

        var dialog = new DevicePointDialogWindow
        {
            DataContext = new DevicePointDialogViewModel(true, row.Entry)
        };
        var result = await ShowDialogAsync<DevicePointDialogResult>(owner, dialog);
        if (result is not null)
            await viewModel.UpdateFromDialogAsync(row, result);
    }

    /// <summary>
    /// 选择点位文件后执行批量导入。
    /// </summary>
    private async void OnImportClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DevicePointManagementViewModel viewModel
            || !viewModel.CanEdit
            || TopLevel.GetTopLevel(this) is not { StorageProvider: { } storageProvider })
            return;

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "按模板导入设备点位",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("设备点位模板（Excel/CSV）")
                {
                    Patterns = ["*.xlsx", "*.csv"]
                }
            ]
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
            await viewModel.ImportAsync(path);
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
            throw new InvalidOperationException("DevicePointManagementView 必须由 MainWindow 承载，才能显示弹窗遮罩。");

        return await mainWindow.ShowDialogWithOverlayAsync<TResult>(dialog);
    }
}
