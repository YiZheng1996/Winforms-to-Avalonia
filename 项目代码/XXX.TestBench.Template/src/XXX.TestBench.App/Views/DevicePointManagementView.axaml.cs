using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using XXX.TestBench.App.ViewModels;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Configuration;

namespace XXX.TestBench.App.Views;

/// <summary>
/// 设备点位管理页面视图。
/// </summary>
public partial class DevicePointManagementView : UserControl
{
    public DevicePointManagementView()
    {
        InitializeComponent();
        AddHandler(TreeViewItem.ExpandedEvent, OnTreeItemExpansionChanged, RoutingStrategies.Bubble);
        AddHandler(TreeViewItem.CollapsedEvent, OnTreeItemExpansionChanged, RoutingStrategies.Bubble);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is DevicePointManagementViewModel viewModel)
            viewModel.ActivateRuntimeObservation();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is DevicePointManagementViewModel viewModel)
            viewModel.DeactivateRuntimeObservation();
        base.OnDetachedFromVisualTree(e);
    }

    private static void OnTreeItemExpansionChanged(object? sender, RoutedEventArgs e)
    {
        if (e.Source is TreeViewItem { DataContext: DevicePointTreeNodeViewModel node } item
            && node.IsExpanded != item.IsExpanded)
            node.IsExpanded = item.IsExpanded;
    }

    private async void OnAddClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DevicePointManagementViewModel viewModel
            || GetOwner() is not { } owner)
            return;

        if (viewModel.CanAddChannel)
            await ShowChannelEditorAsync(viewModel, null, owner);
        else if (viewModel.CanAddDevice)
            await ShowDeviceEditorAsync(
                viewModel,
                null,
                viewModel.SelectedTreeNode?.Kind == DevicePointTreeNodeKind.Channel
                    ? viewModel.SelectedTreeNode.Id
                    : null,
                owner);
        else if (viewModel.CanAddGroup)
            await AddGroupAsync(viewModel, owner);
        else if (viewModel.CanAddPoint)
            await AddPointAsync(viewModel, owner);
    }

    private async void OnAddPointClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DevicePointManagementViewModel viewModel
            && viewModel.CanAddPoint
            && GetOwner() is { } owner)
            await AddPointAsync(viewModel, owner);
    }

    private void OnImportExportClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DevicePointManagementViewModel viewModel
            || sender is not Control target)
            return;

        var menu = new ContextMenu();
        menu.Classes.Add("tree-context-menu");
        AddAsyncMenuItem(menu, "导入点位", viewModel.CanEdit, ImportPointsAsync);
        AddAsyncMenuItem(menu, "导出当前范围", viewModel.CanExportCurrentRange, ExportCurrentRangeAsync);
        AddAsyncMenuItem(menu, "下载填写模板", viewModel.CanDownloadTemplate, DownloadTemplateAsync);
        if (menu.Items.Count > 0)
        {
            target.ContextMenu = menu;
            menu.Open(target);
        }
    }

    private void OnMoreClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DevicePointManagementViewModel viewModel
            || sender is not Control target)
            return;

        var menu = new ContextMenu();
        menu.Classes.Add("tree-context-menu");
        AddAsyncMenuItem(menu, "刷新设备点位", true, async () =>
        {
            if (GetOwner() is { } owner)
                await ShowFeedbackAsync(owner, await viewModel.RefreshAsync());
        });
        AddAsyncMenuItem(menu, "点位诊断", viewModel.CanOpenDiagnostics, OpenDiagnosticsAsync);
        if (viewModel.CanManageSignalBindings)
        {
            AddAsyncMenuItem(
                menu,
                "业务信号绑定",
                true,
                async () =>
                {
                    if (GetOwner() is { } owner)
                        await OpenSignalBindingsAsync(viewModel, owner);
                });
        }

        if (viewModel.SelectedPoint is { } row && viewModel.CanEditSelectedPoint)
        {
            AddAsyncMenuItem(
                menu,
                "编辑选中点位",
                true,
                async () =>
                {
                    if (GetOwner() is { } owner)
                        await EditPointAsync(viewModel, owner, row);
                });
            AddAsyncMenuItem(
                menu,
                "删除选中点位",
                true,
                async () =>
                {
                    await ExecutePointActionAsync(DevicePointTreeActionKind.DeletePoint, viewModel, row);
                },
                isDestructive: true);
        }

        if (viewModel.SelectedTreeNode is { } node && node.Kind != DevicePointTreeNodeKind.Root)
        {
            foreach (var action in viewModel.GetTreeNodeActions(node).Where(action =>
                         action.Kind is DevicePointTreeActionKind.EditChannel
                             or DevicePointTreeActionKind.EditDevice
                             or DevicePointTreeActionKind.EditGroup
                             or DevicePointTreeActionKind.DeleteChannel
                             or DevicePointTreeActionKind.DeleteDevice
                             or DevicePointTreeActionKind.DeleteGroup))
            {
                AddAsyncMenuItem(
                    menu,
                    action.Header,
                    true,
                    () => ExecuteTreeActionAsync(action.Kind, viewModel, node),
                    action.IsDestructive);
            }
        }

        if (menu.Items.Count > 0)
        {
            target.ContextMenu = menu;
            menu.Open(target);
        }
    }

    private static void AddAsyncMenuItem(
        ContextMenu menu,
        string header,
        bool isEnabled,
        Func<Task> action,
        bool isDestructive = false)
    {
        if (!isEnabled)
            return;
        var item = new MenuItem { Header = header };
        item.Classes.Add("tree-context-menu-item");
        if (isDestructive)
            item.Classes.Add("tree-context-menu-danger");
        item.Click += async (_, _) =>
        {
            menu.Close();
            await action();
        };
        menu.Items.Add(item);
    }

    private void OnClearPointSearchClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DevicePointManagementViewModel viewModel)
            viewModel.ClearPointSearch();
    }

    private async void OnDownloadTemplateClick(object? sender, RoutedEventArgs e)
        => await DownloadTemplateAsync();

    private async Task DownloadTemplateAsync()
    {
        if (DataContext is not DevicePointManagementViewModel viewModel
            || !viewModel.CanDownloadTemplate
            || TopLevel.GetTopLevel(this) is not { StorageProvider: { } storageProvider })
            return;

        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "下载设备点位模板",
            SuggestedFileName = DevicePointTemplateDefinition.DefaultFileName,
            DefaultExtension = "xlsx",
            ShowOverwritePrompt = true,
            FileTypeChoices =
            [
                new FilePickerFileType("Excel 设备点位模板")
                {
                    Patterns = ["*.xlsx"]
                }
            ]
        });
        var path = file?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
            await viewModel.DownloadTemplateAsync(path);
    }

    private void OnTreeContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (e.Handled
            || DataContext is not DevicePointManagementViewModel viewModel
            || (!viewModel.CanEdit && !viewModel.CanOpenDiagnostics)
            || sender is not Control target)
            return;

        var node = viewModel.SelectedTreeNode ?? viewModel.TreeNodes.FirstOrDefault();
        if (node is null)
            return;

        OpenNodeContextMenu(target, viewModel, node);
        e.Handled = true;
    }

    private void OnTreeNodeContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (e.Handled
            || sender is not Control target
            || target.DataContext is not DevicePointTreeNodeViewModel node
            || DataContext is not DevicePointManagementViewModel viewModel
            || (!viewModel.CanEdit && !viewModel.CanOpenDiagnostics))
            return;

        viewModel.SelectedTreeNode = node;
        OpenNodeContextMenu(target, viewModel, node);
        e.Handled = true;
    }

    private void OnPointListContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (e.Handled
            || sender is not Control target
            || DataContext is not DevicePointManagementViewModel viewModel
            || (!viewModel.CanEdit && !viewModel.CanOpenDiagnostics)
            || !viewModel.IsGroupedConfiguration)
            return;

        var row = FindPointRow(e.Source);
        if (row is not null)
        {
            viewModel.SelectedPoint = row;
            OpenPointDetailContextMenu(target, viewModel, row);
            e.Handled = true;
            return;
        }

        // 点位详情区域始终使用自己的菜单；空白处也不能回退到设备树菜单。
        OpenPointDetailContextMenu(target, viewModel, viewModel.SelectedPoint);
        e.Handled = true;
    }

    private async void OnPointListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Handled
            || DataContext is not DevicePointManagementViewModel viewModel
            || !viewModel.CanEdit
            || !viewModel.IsGroupedConfiguration
            || GetOwner() is not { } owner
            || FindPointRow(e.Source) is not { } row)
            return;

        viewModel.SelectedPoint = row;
        await EditPointAsync(viewModel, owner, row);
        e.Handled = true;
    }

    private async void OnPointListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled
            || DataContext is not DevicePointManagementViewModel viewModel
            || !viewModel.CanEdit
            || !viewModel.IsGroupedConfiguration)
            return;

        if (IsContextMenuKey(e))
        {
            if (sender is Control target)
            {
                if (viewModel.SelectedPoint is { } menuRow)
                    OpenPointDetailContextMenu(target, viewModel, menuRow);
                else
                    OpenPointDetailContextMenu(target, viewModel, null);
                e.Handled = true;
            }
            return;
        }

        var owner = GetOwner();
        if (owner is null)
            return;

        if ((e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            if (e.Key == Key.C && viewModel.SelectedPoint is { } copyRow)
            {
                viewModel.SelectedPoint = copyRow;
                await ShowFeedbackAsync(owner, viewModel.CopySelectedPoint());
                e.Handled = true;
                return;
            }
            if (e.Key == Key.X && viewModel.SelectedPoint is { } cutRow)
            {
                viewModel.SelectedPoint = cutRow;
                await ShowFeedbackAsync(owner, viewModel.CutSelectedPoint());
                e.Handled = true;
                return;
            }
            if (e.Key == Key.V)
            {
                await PastePointAsync(viewModel, owner);
                e.Handled = true;
                return;
            }
        }

        if (viewModel.SelectedPoint is not { } row)
            return;

        if (e.Key == Key.Delete)
        {
            await ExecutePointActionAsync(DevicePointTreeActionKind.DeletePoint, viewModel, row);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            await EditPointAsync(viewModel, owner, row);
            e.Handled = true;
        }
    }

    private void OnTreeKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled
            || !IsContextMenuKey(e)
            || sender is not Control target
            || DataContext is not DevicePointManagementViewModel viewModel
            || !viewModel.CanEdit
            || viewModel.SelectedTreeNode is not { } node)
            return;

        OpenNodeContextMenu(target, viewModel, node);
        e.Handled = true;
    }

    private static bool IsContextMenuKey(KeyEventArgs e)
        => e.Key == Key.Apps
           || (e.Key == Key.F10 && (e.KeyModifiers & KeyModifiers.Shift) != 0);

    private static DevicePointRow? FindPointRow(object? source)
    {
        var visual = source as Visual;
        while (visual is not null)
        {
            if (visual is DataGridRow { DataContext: DevicePointRow row })
                return row;
            visual = visual.GetVisualParent();
        }

        return null;
    }

    private void OpenNodeContextMenu(
        Control target,
        DevicePointManagementViewModel viewModel,
        DevicePointTreeNodeViewModel node)
    {
        var menu = new ContextMenu();
        menu.Classes.Add("tree-context-menu");
        foreach (var action in viewModel.GetTreeNodeActions(node))
        {
            if (action.IsSeparatorBefore && menu.Items.Count > 0)
                menu.Items.Add(new Separator());

            if (action.Kind == DevicePointTreeActionKind.MovePoint)
            {
                var moveMenu = new MenuItem { Header = action.Header };
                moveMenu.Classes.Add("tree-context-menu-item");
                foreach (var group in viewModel.MoveGroupOptions.Where(group =>
                             !string.Equals(group.Id, viewModel.SelectedPoint?.Entry.GroupId, StringComparison.OrdinalIgnoreCase)))
                {
                    var groupItem = new MenuItem { Header = group.DisplayName };
                    groupItem.Classes.Add("tree-context-menu-item");
                    groupItem.Click += async (_, _) =>
                    {
                        menu.Close();
                        var owner = GetOwner();
                        var feedback = await viewModel.MoveSelectedPointToGroupAsync(group);
                        if (owner is not null)
                            await ShowFeedbackAsync(owner, feedback);
                        ScrollSelectedPointIntoView(viewModel);
                    };
                    moveMenu.Items.Add(groupItem);
                }
                if (moveMenu.Items.Count > 0)
                    menu.Items.Add(moveMenu);
                continue;
            }

            var item = new MenuItem { Header = action.Header };
            item.Classes.Add("tree-context-menu-item");
            if (action.IsDestructive)
                item.Classes.Add("tree-context-menu-danger");
            item.Click += async (_, _) =>
            {
                menu.Close();
                await ExecuteTreeActionAsync(action.Kind, viewModel, node);
            };
            menu.Items.Add(item);
        }

        if (menu.Items.Count == 0)
            return;

        target.ContextMenu = menu;
        menu.Open(target);
    }

    private void OpenPointDetailContextMenu(
        Control target,
        DevicePointManagementViewModel viewModel,
        DevicePointRow? row)
    {
        var menu = new ContextMenu();
        menu.Classes.Add("point-detail-context-menu");
        var selectedRow = row ?? viewModel.SelectedPoint;
        var owner = GetOwner();
        var canAdd = owner is not null && viewModel.CanAddPoint;
        var canEdit = owner is not null && selectedRow is not null && viewModel.CanEditSelectedPoint;
        var canCopy = owner is not null && selectedRow is not null && viewModel.CanCopySelectedPoint;
        var canCut = owner is not null && selectedRow is not null && viewModel.CanCutSelectedPoint;
        var canPaste = owner is not null && viewModel.CanPastePoint;

        AddPointDetailMenuItem(menu, "新建标记", canAdd, action: canAdd ? () => AddPointAsync(viewModel, owner!) : null);
        AddPointDetailMenuItem(
            menu,
            "剪切(U)",
            canCut,
            new KeyGesture(Key.X, KeyModifiers.Control),
            canCut ? () => ExecutePointActionAsync(DevicePointTreeActionKind.CutPoint, viewModel, selectedRow!) : null);
        AddPointDetailMenuItem(
            menu,
            "复制(C)",
            canCopy,
            new KeyGesture(Key.C, KeyModifiers.Control),
            canCopy ? () => ExecutePointActionAsync(DevicePointTreeActionKind.CopyPoint, viewModel, selectedRow!) : null);
        AddPointDetailMenuItem(
            menu,
            "粘贴(V)",
            canPaste,
            new KeyGesture(Key.V, KeyModifiers.Control),
            canPaste ? () => PastePointAsync(viewModel, owner!) : null);

        var moveOptions = viewModel.MoveGroupOptions
            .Where(group => !string.Equals(
                group.Id,
                selectedRow?.Entry.GroupId,
                StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (moveOptions.Count > 0 && selectedRow is not null)
        {
            var moveMenu = new MenuItem { Header = "移动到其他分组" };
            moveMenu.Classes.Add("point-detail-context-menu-item");
            foreach (var group in moveOptions)
            {
                var groupItem = new MenuItem { Header = group.DisplayName };
                groupItem.Classes.Add("point-detail-context-menu-item");
                groupItem.Click += async (_, _) =>
                {
                    menu.Close();
                    viewModel.SelectedPoint = selectedRow;
                    await ShowFeedbackAsync(owner!, await viewModel.MoveSelectedPointToGroupAsync(group));
                    ScrollSelectedPointIntoView(viewModel);
                };
                moveMenu.Items.Add(groupItem);
            }
            menu.Items.Add(moveMenu);
        }

        menu.Items.Add(new Separator());
        AddPointDetailMenuItem(
            menu,
            "查看状态",
            owner is not null && selectedRow is not null && viewModel.CanOpenDiagnostics,
            action: owner is not null && selectedRow is not null && viewModel.CanOpenDiagnostics
                ? () => OpenPointDiagnosticsAsync(viewModel, owner, selectedRow)
                : null);
        menu.Items.Add(new Separator());
        AddPointDetailMenuItem(
            menu,
            "删除(D)",
            canEdit,
            new KeyGesture(Key.Delete, KeyModifiers.None),
            canEdit ? () => ExecutePointActionAsync(DevicePointTreeActionKind.DeletePoint, viewModel, selectedRow!) : null,
            isDestructive: true);
        AddPointDetailMenuItem(
            menu,
            "属性(O)...",
            canEdit,
            action: canEdit ? () => EditPointAsync(viewModel, owner!, selectedRow) : null);

        target.ContextMenu = menu;
        menu.Open(target);
    }

    private static void AddPointDetailMenuItem(
        ContextMenu menu,
        string header,
        bool isEnabled,
        KeyGesture? inputGesture = null,
        Func<Task>? action = null,
        bool isDestructive = false)
    {
        var item = new MenuItem
        {
            Header = header,
            IsEnabled = isEnabled,
            InputGesture = inputGesture
        };
        item.Classes.Add("point-detail-context-menu-item");
        if (isDestructive)
            item.Classes.Add("point-detail-context-menu-danger");
        if (action is not null)
        {
            item.Click += async (_, _) =>
            {
                menu.Close();
                await action();
            };
        }
        menu.Items.Add(item);
    }

    private async Task ExecutePointActionAsync(
        DevicePointTreeActionKind action,
        DevicePointManagementViewModel viewModel,
        DevicePointRow row)
    {
        viewModel.SelectedPoint = row;
        var owner = GetOwner();
        switch (action)
        {
            case DevicePointTreeActionKind.EditPoint when owner is not null:
                await EditPointAsync(viewModel, owner, row);
                break;
            case DevicePointTreeActionKind.CopyPoint when owner is not null:
                await ShowFeedbackAsync(owner, viewModel.CopySelectedPoint());
                break;
            case DevicePointTreeActionKind.CutPoint when owner is not null:
                await ShowFeedbackAsync(owner, viewModel.CutSelectedPoint());
                break;
            case DevicePointTreeActionKind.PastePoint when owner is not null:
                await PastePointAsync(viewModel, owner);
                break;
            case DevicePointTreeActionKind.DiagnosePoint when owner is not null:
                await OpenPointDiagnosticsAsync(viewModel, owner, row);
                break;
            case DevicePointTreeActionKind.DeletePoint:
                if (owner is null)
                    break;
                if (viewModel.GetPointBindingCount(row) > 0)
                {
                    await ShowFeedbackAsync(
                        owner,
                        new OperationFeedback(
                            false,
                            $"点位“{row.PointName}”仍被信号绑定引用，不能删除；请先解除绑定。"));
                    break;
                }
                if (await ConfirmAsync(
                        owner,
                        "删除设备点位",
                        $"确定删除点位“{row.PointName}”吗？\n设备：{row.DeviceText}\n地址：{row.Address}\n访问权限：{row.WritePolicyText}",
                        "确认删除") is true)
                {
                    await ShowFeedbackAsync(owner, await viewModel.DeletePointAsync());
                    ScrollSelectedPointIntoView(viewModel);
                }
                break;
        }
    }

    private async Task ExecuteTreeActionAsync(
        DevicePointTreeActionKind action,
        DevicePointManagementViewModel viewModel,
        DevicePointTreeNodeViewModel node)
    {
        viewModel.SelectedTreeNode = node;
        var owner = GetOwner();
        switch (action)
        {
            case DevicePointTreeActionKind.AddChannel when owner is not null:
                await ShowChannelEditorAsync(viewModel, null, owner);
                break;
            case DevicePointTreeActionKind.AddDevice when owner is not null:
                await ShowDeviceEditorAsync(viewModel, null, node.Id, owner);
                break;
            case DevicePointTreeActionKind.AddGroup when owner is not null:
                await AddGroupAsync(viewModel, owner);
                break;
            case DevicePointTreeActionKind.AddPoint when owner is not null:
                await AddPointAsync(viewModel, owner);
                break;
            case DevicePointTreeActionKind.EditChannel when owner is not null:
                await ShowChannelEditorAsync(viewModel, node.Id, owner);
                break;
            case DevicePointTreeActionKind.EditDevice when owner is not null:
                await ShowDeviceEditorAsync(viewModel, node.Id, null, owner);
                break;
            case DevicePointTreeActionKind.EditGroup when owner is not null:
                await EditGroupAsync(viewModel, owner);
                break;
            case DevicePointTreeActionKind.EditPoint when owner is not null:
                await EditPointAsync(viewModel, owner);
                break;
            case DevicePointTreeActionKind.PastePoint when owner is not null:
                await PastePointAsync(viewModel, owner);
                break;
            case DevicePointTreeActionKind.DiagnoseScope when owner is not null:
                await OpenDiagnosticsAsync();
                break;
            case DevicePointTreeActionKind.DeleteChannel when owner is not null:
                if (await ConfirmAsync(
                    owner,
                        "删除通信通道",
                        BuildChannelDeleteMessage(viewModel, node),
                        "确认删除") is true)
                    await ApplyDeviceConfigurationMutationAsync(
                        viewModel,
                        editor =>
                        {
                            editor.SelectChannel(node.Id);
                            return editor.DeleteSelectedChannel();
                        },
                        owner);
                break;
            case DevicePointTreeActionKind.DeleteDevice when owner is not null:
                if (await ConfirmAsync(
                        owner,
                        "删除设备",
                        BuildDeviceDeleteMessage(viewModel, node),
                        "确认删除") is true)
                    await ApplyDeviceConfigurationMutationAsync(
                        viewModel,
                        editor =>
                        {
                            editor.SelectDevice(node.Id);
                            return editor.DeleteSelectedDevice();
                        },
                        owner);
                break;
            case DevicePointTreeActionKind.DeleteGroup:
                if (owner is not null
                    && await ConfirmAsync(
                        owner,
                        "删除点位分组",
                        BuildGroupDeleteMessage(viewModel, node),
                        "确认删除") is true)
                {
                    await ShowFeedbackAsync(owner, await viewModel.DeleteSelectedTreeNodeAsync());
                    ScrollSelectedPointIntoView(viewModel);
                }
                break;
            case DevicePointTreeActionKind.DeletePoint:
                if (viewModel.SelectedPoint is { } point
                    && viewModel.GetPointBindingCount(point) == 0
                    && owner is not null
                    && await ConfirmAsync(
                        owner,
                        "删除设备点位",
                        $"确定删除点位“{point.PointName}”吗？\n设备：{point.DeviceText}\n地址：{point.Address}\n访问权限：{point.WritePolicyText}",
                        "确认删除") is true)
                {
                    await ShowFeedbackAsync(owner, await viewModel.DeletePointAsync());
                    ScrollSelectedPointIntoView(viewModel);
                }
                break;
        }
    }

    private static string BuildGroupDeleteMessage(
        DevicePointManagementViewModel viewModel,
        DevicePointTreeNodeViewModel node)
    {
        var count = viewModel.CurrentEntries.Count(entry =>
            string.Equals(entry.DeviceId, node.OwnerDeviceId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(entry.GroupId, node.Id, StringComparison.OrdinalIgnoreCase));
        return $"确定删除分组“{node.DisplayText}”吗？\n该分组包含 {count} 个点位，删除后仍可从设备节点查看。";
    }

    private static string BuildDeviceDeleteMessage(
        DevicePointManagementViewModel viewModel,
        DevicePointTreeNodeViewModel node)
    {
        var count = viewModel.CurrentEntries.Count(entry =>
            string.Equals(entry.DeviceId, node.OwnerDeviceId, StringComparison.OrdinalIgnoreCase));
        return $"确定删除设备“{node.DisplayText}”吗？\n当前有 {count} 个点位引用该设备；保存前还会检查信号绑定和运行状态。";
    }

    private static string BuildChannelDeleteMessage(
        DevicePointManagementViewModel viewModel,
        DevicePointTreeNodeViewModel node)
    {
        var devices = viewModel.GetChannelDeviceCount(node.Id, node.Code);
        return $"确定删除通信通道“{node.DisplayText}”吗？\n当前配置会先检查关联设备；如仍被设备引用，系统将拒绝应用。\n关联设备数：{devices}。";
    }

    private async Task ExportCurrentRangeAsync()
    {
        if (DataContext is not DevicePointManagementViewModel viewModel
            || !viewModel.CanExportCurrentRange
            || viewModel.CreateCatalogExportRequest() is null
            || TopLevel.GetTopLevel(this) is not { StorageProvider: { } storageProvider }
            || GetOwner() is not { } owner)
            return;

        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出当前范围设备点位",
            SuggestedFileName = viewModel.CreateCatalogExportFileName(),
            DefaultExtension = "xlsx",
            ShowOverwritePrompt = true,
            FileTypeChoices =
            [
                new FilePickerFileType("Excel 设备点位")
                {
                    Patterns = ["*.xlsx"]
                }
            ]
        });
        var path = file?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
            await ShowFeedbackAsync(owner, await viewModel.ExportCurrentRangeAsync(path));
    }

    private async Task PastePointAsync(DevicePointManagementViewModel viewModel, Window owner)
    {
        var draft = viewModel.CreatePasteDraft();
        if (draft is null)
        {
            if (!string.IsNullOrWhiteSpace(viewModel.StatusMessage))
                await ShowFeedbackAsync(owner, new OperationFeedback(false, viewModel.StatusMessage));
            return;
        }

        var context = viewModel.CreatePointDialogContext(false);
        var dialog = new DevicePointDialogWindow
        {
            DataContext = new DevicePointDialogViewModel(draft, context)
        };
        var result = await ShowDialogAsync<DevicePointDialogResult>(owner, dialog);
        if (result is null)
            return;
        await ShowFeedbackAsync(owner, await viewModel.ApplyPasteAsync(result));
        ScrollSelectedPointIntoView(viewModel);
    }

    private async Task OpenDiagnosticsAsync()
    {
        if (DataContext is not DevicePointManagementViewModel viewModel
            || GetOwner() is not { } owner)
            return;
        var diagnostics = viewModel.CreateDiagnosticsViewModel();
        if (diagnostics is null)
        {
            await ShowFeedbackAsync(owner, new OperationFeedback(false, "当前范围没有可诊断的运行时点位"));
            return;
        }
        await diagnostics.LoadAsync();
        await ShowDialogAsync<object?>(owner, new DevicePointDiagnosticsWindow { DataContext = diagnostics });
    }

    private static async Task OpenPointDiagnosticsAsync(
        DevicePointManagementViewModel viewModel,
        Window owner,
        DevicePointRow row)
    {
        var diagnostics = viewModel.CreateDiagnosticsViewModelForPoint(row);
        if (diagnostics is null)
        {
            await ShowFeedbackAsync(owner, new OperationFeedback(false, "该点位不在当前运行时中，不能读取状态"));
            return;
        }
        diagnostics.SelectedPoint = diagnostics.Points.FirstOrDefault();
        await diagnostics.LoadAsync();
        await ShowDialogAsync<object?>(owner, new DevicePointDiagnosticsWindow { DataContext = diagnostics });
    }

    private async Task AddPointAsync(
        DevicePointManagementViewModel viewModel,
        Window owner)
    {
        var context = viewModel.CreatePointDialogContext(false);
        var dialog = new DevicePointDialogWindow
        {
            DataContext = new DevicePointDialogViewModel(false, null, context)
        };
        var result = await ShowDialogAsync<DevicePointDialogResult>(owner, dialog);
        if (result is null)
            return;
        await ShowFeedbackAsync(owner, await viewModel.CreateFromDialogAsync(result));
        ScrollSelectedPointIntoView(viewModel);
    }

    private async Task EditPointAsync(
        DevicePointManagementViewModel viewModel,
        Window owner,
        DevicePointRow? row = null)
    {
        row ??= viewModel.SelectedPoint;
        if (row is null)
            return;
        var context = viewModel.CreatePointDialogContext(true, row);
        var dialog = new DevicePointDialogWindow
        {
            DataContext = new DevicePointDialogViewModel(true, row.Entry, context)
        };
        var result = await ShowDialogAsync<DevicePointDialogResult>(owner, dialog);
        if (result is null)
            return;
        await ShowFeedbackAsync(owner, await viewModel.UpdateFromDialogAsync(row, result));
        ScrollSelectedPointIntoView(viewModel);
    }

    private static async Task AddGroupAsync(
        DevicePointManagementViewModel viewModel,
        Window owner)
    {
        var dialog = new PointGroupDialogWindow
        {
            DataContext = viewModel.CreatePointGroupDialog(false)
        };
        var result = await ShowDialogAsync<PointGroupDialogResult>(owner, dialog);
        if (result is not null)
            await ShowFeedbackAsync(owner, await viewModel.ApplyPointGroupAsync(result));
    }

    private static async Task EditGroupAsync(
        DevicePointManagementViewModel viewModel,
        Window owner)
    {
        var dialog = new PointGroupDialogWindow
        {
            DataContext = viewModel.CreatePointGroupDialog(true)
        };
        var result = await ShowDialogAsync<PointGroupDialogResult>(owner, dialog);
        if (result is not null)
            await ShowFeedbackAsync(owner, await viewModel.ApplyPointGroupAsync(result));
    }

    private static async Task OpenSignalBindingsAsync(
        DevicePointManagementViewModel viewModel,
        Window owner)
    {
        var dialog = new SignalBindingsDialogWindow
        {
            DataContext = viewModel.CreateSignalBindingsDialog()
        };
        var result = await ShowDialogAsync<DeviceConfigurationApplyResult>(owner, dialog);
        if (result is not null)
            await ShowFeedbackAsync(owner, await viewModel.ApplySignalBindingsResultAsync(result));
    }

    private async void OnImportClick(object? sender, RoutedEventArgs e)
        => await ImportPointsAsync();

    private async Task ImportPointsAsync()
    {
        if (DataContext is not DevicePointManagementViewModel viewModel
            || !viewModel.CanEdit
            || !viewModel.IsGroupedConfiguration
            || TopLevel.GetTopLevel(this) is not { StorageProvider: { } storageProvider }
            || GetOwner() is not { } owner)
            return;

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "导入设备点位",
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

        var plan = await viewModel.CreateImportPlanAsync(path);
        if (plan is null)
        {
            if (!string.IsNullOrWhiteSpace(viewModel.StatusMessage))
                await ShowFeedbackAsync(owner, new OperationFeedback(false, viewModel.StatusMessage));
            return;
        }

        var dialog = new DevicePointImportPreviewWindow
        {
            DataContext = new DevicePointImportPreviewViewModel(
                plan,
                viewModel.CurrentEntries.Count,
                viewModel.CurrentGroups,
                viewModel.CurrentDeviceEntries)
        };
        if (await ShowDialogAsync<bool>(owner, dialog) is true)
            await ShowFeedbackAsync(owner, await viewModel.ApplyImportedPointsAsync(
                plan,
                s7OptimizedBlockAccessConfirmed: true));
        else
            viewModel.CancelImportPreview();
    }

    private static async Task ShowChannelEditorAsync(
        DevicePointManagementViewModel viewModel,
        string? channelId,
        Window owner)
    {
        var transaction = viewModel.CreateDeviceConfigurationDialog();
        var form = transaction.CreateChannelEditor(channelId);
        if (form is null)
        {
            await ShowFeedbackAsync(owner, new OperationFeedback(false, transaction.ValidationMessage));
            return;
        }

        var dialog = new ChannelEditorDialogWindow { DataContext = form };
        var result = await ShowDialogAsync<ChannelEditorDialogResult>(owner, dialog);
        if (result is null)
            return;
        if (!transaction.ApplyChannelEntry(result.Entry))
        {
            await ShowFeedbackAsync(owner, new OperationFeedback(false, transaction.ValidationMessage));
            return;
        }

        await SaveDeviceConfigurationAsync(viewModel, transaction, owner);
    }

    private static async Task ShowDeviceEditorAsync(
        DevicePointManagementViewModel viewModel,
        string? deviceId,
        string? defaultChannelId,
        Window owner)
    {
        var transaction = viewModel.CreateDeviceConfigurationDialog();
        var form = transaction.CreateDeviceEditor(deviceId, defaultChannelId);
        if (form is null)
        {
            await ShowFeedbackAsync(owner, new OperationFeedback(false, transaction.ValidationMessage));
            return;
        }

        var dialog = new DeviceEditorDialogWindow { DataContext = form };
        var result = await ShowDialogAsync<DeviceEditorDialogResult>(owner, dialog);
        if (result is null)
            return;
        if (!transaction.ApplyDeviceEntry(result.Entry))
        {
            await ShowFeedbackAsync(owner, new OperationFeedback(false, transaction.ValidationMessage));
            return;
        }

        await SaveDeviceConfigurationAsync(viewModel, transaction, owner);
    }

    private static async Task ApplyDeviceConfigurationMutationAsync(
        DevicePointManagementViewModel viewModel,
        Func<DeviceConfigurationEditorViewModel, bool> mutate,
        Window owner)
    {
        var editor = viewModel.CreateDeviceConfigurationDialog();
        if (!mutate(editor))
        {
            await ShowFeedbackAsync(owner, new OperationFeedback(false, editor.ValidationMessage));
            return;
        }

        await SaveDeviceConfigurationAsync(viewModel, editor, owner);
    }

    private static async Task SaveDeviceConfigurationAsync(
        DevicePointManagementViewModel viewModel,
        DeviceConfigurationEditorViewModel editor,
        Window owner)
    {
        var result = await editor.SaveAsync();
        if (!result.Ok
            && result.RequiresS7OptimizedBlockAccessConfirmation
            && result.S7OptimizedBlockAccessNotice is { } notice)
        {
            if (await ConfirmAsync(
                    owner,
                    notice.ConfirmationTitle,
                    notice.ConfirmationMessage,
                    notice.ConfirmButtonText) is not true)
            {
                await ShowFeedbackAsync(owner, new OperationFeedback(false, "已取消应用，当前设备配置未改变"));
                return;
            }

            result = await editor.SaveAsync(s7OptimizedBlockAccessConfirmed: true);
        }

        if (result.Ok)
        {
            await ShowFeedbackAsync(owner, await viewModel.ApplyDeviceConfigurationResultAsync(result));
            return;
        }

        await ShowFeedbackAsync(
            owner,
            new OperationFeedback(false, result.Error ?? editor.ValidationMessage ?? "设备配置应用失败"));
    }

    private Window? GetOwner() => TopLevel.GetTopLevel(this) as Window;

    private void ScrollSelectedPointIntoView(DevicePointManagementViewModel viewModel)
    {
        if (viewModel.SelectedPoint is null)
            return;

        Dispatcher.UIThread.Post(
            () => PointGrid.ScrollIntoView(viewModel.SelectedPoint, null),
            DispatcherPriority.Background);
    }

    private static async Task<bool?> ConfirmAsync(
        Window owner,
        string title,
        string message,
        string confirmText)
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
            throw new InvalidOperationException("DevicePointManagementView 必须由 MainWindow 承载，才能显示弹窗遮罩。");

        return await mainWindow.ShowDialogWithOverlayAsync<TResult>(dialog);
    }
}
