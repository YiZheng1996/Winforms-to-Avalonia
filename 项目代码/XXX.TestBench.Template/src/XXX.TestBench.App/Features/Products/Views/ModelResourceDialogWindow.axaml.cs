using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using XXX.TestBench.App.ViewModels;

namespace XXX.TestBench.App.Views;

/// <summary>型号报表资源导入窗口；仅负责选择本地文件和返回结果。</summary>
public partial class ModelResourceDialogWindow : Window
{
    public ModelResourceDialogWindow() => InitializeComponent();

    private async void OnEvaluationTemplateClick(object? sender, RoutedEventArgs e)
        => await ChooseFileAsync(path => ((ModelResourceDialogViewModel)DataContext!).EvaluationTemplatePath = path,
            "选择产品判定 Excel 模板", ".xlsx");

    private async void OnEvaluationMappingClick(object? sender, RoutedEventArgs e)
        => await ChooseFileAsync(path => ((ModelResourceDialogViewModel)DataContext!).EvaluationMappingPath = path,
            "选择产品判定 JSON 映射", ".json");

    private async void OnProcessTemplateClick(object? sender, RoutedEventArgs e)
        => await ChooseFileAsync(path => ((ModelResourceDialogViewModel)DataContext!).ProcessTemplatePath = path,
            "选择过程报告 Excel 模板", ".xlsx");

    private async void OnProcessMappingClick(object? sender, RoutedEventArgs e)
        => await ChooseFileAsync(path => ((ModelResourceDialogViewModel)DataContext!).ProcessMappingPath = path,
            "选择过程报告 JSON 映射", ".json");

    private async void OnGuidancePackageClick(object? sender, RoutedEventArgs e)
        => await ChooseFileAsync(path => ((ModelResourceDialogViewModel)DataContext!).GuidancePackagePath = path,
            "选择准备指导 ZIP 包", ".zip");

    private async void OnGuidancePreviewClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ModelResourceDialogViewModel viewModel)
            return;
        try
        {
            var snapshot = await viewModel.LoadGuidancePreviewAsync();
            if (snapshot is null)
                throw new InvalidOperationException("该型号尚未配置准备指导。");
            var preview = new PreparationGuidancePreviewWindow
            {
                DataContext = new PreparationGuidancePreviewViewModel(snapshot)
            };
            await preview.ShowDialog(this);
        }
        catch (Exception ex)
        {
            viewModel.StatusMessage = "读取准备指导失败：" + ex.Message;
        }
    }

    private async Task ChooseFileAsync(Action<string> setPath, string title, string extension)
    {
        if (DataContext is not ModelResourceDialogViewModel || TopLevel.GetTopLevel(this) is not { } topLevel)
            return;
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(extension switch
            {
                ".xlsx" => "Excel 工作簿",
                ".zip" => "ZIP 压缩包",
                _ => "JSON 文件"
            })
            {
                Patterns = ["*" + extension]
            }]
        });
        if (files.Count == 1 && files[0].Path.IsFile)
            setPath(files[0].Path.LocalPath);
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ModelResourceDialogViewModel viewModel
            && await viewModel.SaveAsync())
            Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }
}
