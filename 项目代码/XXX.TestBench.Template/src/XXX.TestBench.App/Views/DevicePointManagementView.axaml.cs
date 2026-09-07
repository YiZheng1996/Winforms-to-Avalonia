using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using XXX.TestBench.App.ViewModels;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Configuration;

namespace XXX.TestBench.App.Views;

/// <summary>
/// 设备点位管理页面视图。
/// </summary>
public partial class DevicePointManagementView : UserControl
{
    public DevicePointManagementView() => InitializeComponent();

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

    private async void OnAddPointClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DevicePointManagementViewModel viewModel
            || !viewModel.CanAddPoint
            || GetOwner() is not { } owner)
            return;

        var dialog = new DevicePointDialogWindow
        {
            DataContext = new DevicePointDialogViewModel(
                false,
                null,
                viewModel.DialogDeviceOptions,
                viewModel.SelectedPointDialogDeviceId,
                viewModel.CurrentGroups,
                viewModel.SelectedPointDialogGroupId)
        };
        var result = await ShowDialogAsync<DevicePointDialogResult>(owner, dialog);
        if (result is not null)
            await viewModel.CreateFromDialogAsync(result);
    }

    private async void OnEditPointClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DevicePointManagementViewModel viewModel
            || viewModel.SelectedPoint is not { } row
            || GetOwner() is not { } owner)
            return;

        var dialog = new DevicePointDialogWindow
        {
            DataContext = new DevicePointDialogViewModel(
                true,
                row.Entry,
                viewModel.DialogDeviceOptions,
                row.Entry.DeviceId,
                viewModel.CurrentGroups,
                row.Entry.GroupId)
        };
        var result = await ShowDialogAsync<DevicePointDialogResult>(owner, dialog);
        if (result is not null)
            await viewModel.UpdateFromDialogAsync(row, result);
    }

    private async void OnDeletePointClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DevicePointManagementViewModel viewModel
            && viewModel.CanEditSelectedPoint)
            await viewModel.DeletePointAsync();
    }

    private async void OnAddChannelClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DevicePointManagementViewModel viewModel
            || !viewModel.CanAddChannel
            || GetOwner() is not { } owner)
            return;
        await ShowDeviceConfigurationAsync(viewModel, editor => editor.AddChannel(), owner);
    }

    private async void OnAddDeviceClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DevicePointManagementViewModel viewModel
            || !viewModel.CanAddDevice
            || GetOwner() is not { } owner)
            return;
        await ShowDeviceConfigurationAsync(viewModel, editor => editor.AddDevice(), owner);
    }

    private async void OnAddGroupClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DevicePointManagementViewModel viewModel
            || !viewModel.CanAddGroup
            || GetOwner() is not { } owner)
            return;
        var dialog = new PointGroupDialogWindow
        {
            DataContext = viewModel.CreatePointGroupDialog(false)
        };
        var result = await ShowDialogAsync<PointGroupDialogResult>(owner, dialog);
        if (result is not null)
            await viewModel.ApplyPointGroupAsync(result);
    }

    private async void OnEditSelectedNodeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DevicePointManagementViewModel viewModel
            || !viewModel.CanEditSelectedNode
            || viewModel.SelectedTreeNode is not { } node
            || GetOwner() is not { } owner)
            return;

        if (node.Kind == DevicePointTreeNodeKind.Point)
        {
            OnEditPointClick(sender, e);
            return;
        }
        if (node.Kind == DevicePointTreeNodeKind.Group)
        {
            var dialog = new PointGroupDialogWindow
            {
                DataContext = viewModel.CreatePointGroupDialog(true)
            };
            var result = await ShowDialogAsync<PointGroupDialogResult>(owner, dialog);
            if (result is not null)
                await viewModel.ApplyPointGroupAsync(result);
            return;
        }
        await ShowDeviceConfigurationAsync(viewModel, editor =>
        {
            if (node.Kind == DevicePointTreeNodeKind.Channel)
                editor.EditSelectedChannel();
            else if (node.Kind == DevicePointTreeNodeKind.Device)
                editor.EditSelectedDevice();
        }, owner);
    }

    private async void OnDeleteSelectedNodeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DevicePointManagementViewModel viewModel
            || !viewModel.CanDeleteSelectedNode
            || viewModel.SelectedTreeNode is not { } node
            || GetOwner() is not { } owner)
            return;

        if (node.Kind is DevicePointTreeNodeKind.Point or DevicePointTreeNodeKind.Group)
        {
            await viewModel.DeleteSelectedTreeNodeAsync();
            return;
        }
        await ShowDeviceConfigurationAsync(viewModel, editor =>
        {
            if (node.Kind == DevicePointTreeNodeKind.Channel)
                editor.DeleteSelectedChannel();
            else if (node.Kind == DevicePointTreeNodeKind.Device)
                editor.DeleteSelectedDevice();
        }, owner);
    }

    private async void OnMovePointClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DevicePointManagementViewModel viewModel
            && viewModel.CanMoveSelectedPoint)
            await viewModel.MoveSelectedPointAsync();
    }

    private async void OnSignalBindingsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DevicePointManagementViewModel viewModel
            || !viewModel.CanManageSignalBindings
            || GetOwner() is not { } owner)
            return;

        var dialog = new SignalBindingsDialogWindow
        {
            DataContext = viewModel.CreateSignalBindingsDialog()
        };
        var result = await ShowDialogAsync<DeviceConfigurationApplyResult>(owner, dialog);
        if (result?.Ok is true)
            await viewModel.ApplySignalBindingsResultAsync(result);
    }

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
        if (string.IsNullOrWhiteSpace(path))
            return;

        var result = await viewModel.ValidateImportAsync(path);
        if (!result.IsValid || GetOwner() is not { } owner)
            return;

        var dialog = new DevicePointImportPreviewWindow
        {
            DataContext = new DevicePointImportPreviewViewModel(
                result.Points,
                viewModel.CurrentEntries.Count,
                viewModel.CurrentEntries,
                viewModel.CurrentGroups)
        };
        if (await ShowDialogAsync<bool>(owner, dialog) is true)
            await viewModel.ApplyImportedPointsAsync(result);
        else
            viewModel.CancelImportPreview();
    }

    private static async Task ShowDeviceConfigurationAsync(
        DevicePointManagementViewModel viewModel,
        Action<DeviceConfigurationEditorViewModel> configure,
        Window owner)
    {
        var editor = viewModel.CreateDeviceConfigurationDialog();
        configure(editor);
        var dialog = new DeviceConfigurationDialogWindow { DataContext = editor };
        var result = await ShowDialogAsync<DeviceConfigurationApplyResult>(owner, dialog);
        if (result?.Ok is true)
            await viewModel.ApplyDeviceConfigurationResultAsync(result);
    }

    private Window? GetOwner() => TopLevel.GetTopLevel(this) as Window;

    private static async Task<TResult?> ShowDialogAsync<TResult>(Window owner, Window dialog)
    {
        if (owner is not MainWindow mainWindow)
            throw new InvalidOperationException("DevicePointManagementView 必须由 MainWindow 承载，才能显示弹窗遮罩。");

        return await mainWindow.ShowDialogWithOverlayAsync<TResult>(dialog);
    }
}
