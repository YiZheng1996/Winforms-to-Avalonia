using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XXX.TestBench.App.Composition;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Execution;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.App.ViewModels;

/// <summary>
/// 左侧设备树中的筛选节点。筛选只影响显示，不改变配置集合。
/// </summary>
public sealed class DevicePointDeviceFilter
{
    public required string Id { get; init; }
    public required string Code { get; init; }
    public required string Name { get; init; }
    public bool IsAll { get; init; }
    public int PointCount { get; set; }
    public string StatusText { get; set; } = "未读取";
    public string DisplayName => IsAll ? "全部设备" : $"{Code}  {Name}";
    public string SummaryText => IsAll ? $"{PointCount} 个点位" : $"{StatusText} · {PointCount} 个点位";
}

/// <summary>
/// 设备点位表格行：页面只展示配置副本，不直接修改运行时点位。
/// </summary>
public sealed class DevicePointRow
{
    /// <summary>
    /// 点位配置副本。
    /// </summary>
    public required PointsConfig.PointEntry Entry { get; init; }
    /// <summary>
    /// 点位编码。
    /// </summary>
    public string Code => Entry.Code;
    /// <summary>
    /// 稳定点位身份。
    /// </summary>
    public string PointId => Entry.Id;
    /// <summary>
    /// 点位名称。
    /// </summary>
    public string Name => Entry.Name;
    /// <summary>
    /// 所属设备编码；旧版单设备配置没有该字段时显示兼容名称。
    /// </summary>
    public string DeviceCode { get; init; } = string.Empty;
    /// <summary>
    /// 所属分组编码。
    /// </summary>
    public string GroupCode { get; init; } = string.Empty;
    /// <summary>
    /// 所属分组名称。
    /// </summary>
    public string GroupName { get; init; } = string.Empty;
    /// <summary>
    /// 面向客户显示的分组。
    /// </summary>
    public string GroupText => string.IsNullOrWhiteSpace(GroupCode)
        ? "DEFAULT  未分组"
        : $"{GroupCode}  {GroupName}";
    /// <summary>
    /// 通信方式内部值。
    /// </summary>
    public string Protocol => Entry.Protocol;
    /// <summary>
    /// 面向客户显示的通信方式。
    /// </summary>
    public string ProtocolText => DevicePointTypeCatalog.ToDisplayName(Entry.ProtocolKind, Entry.Protocol);
    /// <summary>
    /// 地址。
    /// </summary>
    public string Address => Entry.Address;
    /// <summary>
    /// 数据类型内部值。
    /// </summary>
    public string DataType => Entry.DataType;
    /// <summary>
    /// 面向客户显示的数据类型。
    /// </summary>
    public string DataTypeText => DevicePointTypeCatalog.ToDisplayName(Entry.DataTypeKind, Entry.DataType);
    /// <summary>
    /// 单位。
    /// </summary>
    public string Unit => Entry.Unit;
    /// <summary>
    /// 量程显示文字。
    /// </summary>
    public string ScaleText
    {
        get
        {
            var values = new[] { Entry.EffectiveRawMin, Entry.EffectiveRawMax, Entry.EffectiveEngMin, Entry.EffectiveEngMax };
            return values.Any(value => value.HasValue)
                ? $"{values[0]}~{values[1]} → {values[2]}~{values[3]}"
                : "未配置";
        }
    }
    /// <summary>
    /// 写入权限显示文字，避免把“可写”和“风险”拆成容易误读的两列。
    /// </summary>
    public string WritePolicyText => !Entry.IsWritable
        ? "只读"
        : Entry.RiskLevel == WriteRiskLevel.HighRisk ? "可写（高风险）" : "可写（普通）";
    /// <summary>
    /// 启用状态显示文字。
    /// </summary>
    public string EnabledText => Entry.IsEnabled ? "启用" : "停用";
}

/// <summary>
/// 设备点位管理：支持模板下载、手工新增/编辑/删除，以及按固定中文格式校验后的 Excel/CSV 整体导入。
/// </summary>
public sealed partial class DevicePointManagementViewModel : PageViewModel
{
    /// <summary>
    /// 组合根注入的服务。
    /// </summary>
    private readonly ShellServices _services;
    /// <summary>
    /// 当前操作者。
    /// </summary>
    private readonly UserContext _actor;
    private readonly List<PointsConfig.PointEntry> _allEntries = new();
    private readonly List<PointsConfig.PointGroupEntry> _allGroups = new();

    /// <summary>
    /// 创建设备点位管理视图模型。
    /// </summary>
    public DevicePointManagementViewModel(ShellServices services, UserContext actor)
    {
        _services = services;
        _actor = actor;
    }

    /// <summary>
    /// 页面标题。
    /// </summary>
    public override string Title => "设备点位";
    /// <summary>
    /// 页面展示的点位行集合。
    /// </summary>
    public ObservableCollection<DevicePointRow> Points { get; } = new();
    /// <summary>
    /// 当前完整点位集合副本，供导入预览计算更新/新增/冲突，不受左侧筛选影响。
    /// </summary>
    public IReadOnlyList<PointsConfig.PointEntry> CurrentEntries
        => _allEntries.ToList();
    /// <summary>
    /// 当前完整分组集合副本，供分组编辑和导入预览使用。
    /// </summary>
    public IReadOnlyList<PointsConfig.PointGroupEntry> CurrentGroups
        => _allGroups.Select(CloneGroup).ToList();
    /// <summary>
    /// 左侧通道/设备筛选树的扁平节点集合。通道资源仍由运行时管理器持有。
    /// </summary>
    public ObservableCollection<DevicePointDeviceFilter> DeviceFilters { get; } = new();
    /// <summary>
    /// 设备与通道树。根节点固定为“设备与通道”，不引入项目或 OPC 虚拟层级。
    /// </summary>
    public ObservableCollection<DevicePointTreeNodeViewModel> TreeNodes { get; } = new();
    /// <summary>
    /// 点位移动操作可选择的当前设备分组。
    /// </summary>
    public ObservableCollection<DevicePointGroupChoice> MoveGroupOptions { get; } = new();
    /// <summary>
    /// 当前用户是否有设备管理权限。
    /// </summary>
    public bool CanEdit => _actor.HasPermission(PermissionCode.ManageDevices);
    /// <summary>
    /// 是否已有点位数据。
    /// </summary>
    public bool HasPoints => Points.Count > 0;
    /// <summary>
    /// 点位来源说明文字。
    /// </summary>
    public string SourceText => $"来源：生效设备配置（设备→通道→分组→点位）；导入格式：v{DevicePointTemplateDefinition.CurrentTemplateVersion} · {DevicePointTemplateDefinition.DefaultFileName}";

    /// <summary>
    /// 当前设备运行时所使用的完整配置版本。
    /// </summary>
    public string ActiveRevisionText
        => $"当前生效版本：{(_services.DeviceModes.Runtime?.ActiveRevision is { Length: > 0 } revision ? revision : "旧版兼容运行时")}";

    /// <summary>
    /// 当前完整 v2 配置是否可以编辑项目级业务信号绑定。
    /// </summary>
    public bool IsGroupedConfiguration
        => _services.DeviceConfig.SchemaVersion == DeviceConfig.CurrentSchemaVersion
           && _services.DevicePoints.SchemaVersion == PointsConfig.CurrentSchemaVersion;

    public bool CanManageSignalBindings
        => CanEdit
           && _services.DeviceConfigurations is not null
           && IsGroupedConfiguration
           && GetRequiredSignals().Count > 0;

    /// <summary>
    /// 当前完整 v2 配置是否可以编辑通道和设备。
    /// </summary>
    public bool CanManageDeviceConfiguration
        => CanEdit
           && _services.DeviceConfigurations is not null
           && IsGroupedConfiguration;

    /// <summary>
    /// 供新增/编辑弹窗使用的设备选项；没有完整 v2 身份时返回空集合，保留旧版兼容编辑器。
    /// </summary>
    public IReadOnlyList<DevicePointDeviceChoice> DialogDeviceOptions
        => (_services.DeviceConfig.Devices ?? new List<DeviceConfig.DeviceEntry>())
            .Where(device => !string.IsNullOrWhiteSpace(device.Id)
                && !string.IsNullOrWhiteSpace(device.Code)
                && !string.IsNullOrWhiteSpace(device.DriverKey))
            .Select(device => new DevicePointDeviceChoice(
                device.Id.Trim(), device.Code.Trim(),
                string.IsNullOrWhiteSpace(device.Name) ? device.Code.Trim() : device.Name.Trim(),
                device.DriverKey.Trim(), device.Enabled))
            .ToList();

    /// <summary>
    /// 从当前树节点推导点位弹窗的默认设备，不改变点位实际归属。
    /// </summary>
    public string? SelectedPointDialogDeviceId => SelectedTreeNode?.Kind switch
    {
        DevicePointTreeNodeKind.Device => SelectedTreeNode.Id,
        DevicePointTreeNodeKind.Group => SelectedTreeNode.ParentId,
        _ => null
    };

    /// <summary>
    /// 从当前树节点推导点位弹窗的默认分组。
    /// </summary>
    public string? SelectedPointDialogGroupId
        => SelectedTreeNode?.Kind == DevicePointTreeNodeKind.Group ? SelectedTreeNode.Id : null;

    /// <summary>
    /// 当前点位表是否处于设备筛选状态。
    /// </summary>
    public string ScopeText => SelectedScopeText;

    /// <summary>
    /// 当前树节点对应的清单标题。
    /// </summary>
    public string SelectedScopeTitle => SelectedTreeNode?.Kind switch
    {
        DevicePointTreeNodeKind.Channel => "通道点位清单",
        DevicePointTreeNodeKind.Device => "设备点位清单",
        DevicePointTreeNodeKind.Group => "分组点位清单",
        DevicePointTreeNodeKind.Point => "点位详情",
        _ => "设备点位清单"
    };

    /// <summary>
    /// 当前树节点对应的筛选说明。
    /// </summary>
    public string SelectedScopeText => SelectedTreeNode is null || SelectedTreeNode.Kind == DevicePointTreeNodeKind.Root
        ? "当前显示全部设备和通道；筛选不会删除或修改其他配置"
        : $"当前范围：{SelectedTreeNode.DisplayText} · 其他设备点位不会被修改";

    public bool CanAddChannel => CanManageDeviceConfiguration && SelectedTreeNode?.Kind == DevicePointTreeNodeKind.Root;
    public bool CanAddDevice => CanManageDeviceConfiguration && SelectedTreeNode?.Kind == DevicePointTreeNodeKind.Channel;
    public bool CanAddGroup => IsGroupedConfiguration && CanEdit && SelectedTreeNode?.Kind == DevicePointTreeNodeKind.Device;
    public bool CanAddPoint => CanEdit && SelectedTreeNode?.Kind is DevicePointTreeNodeKind.Device or DevicePointTreeNodeKind.Group;
    public bool CanEditSelectedPoint => CanEdit && SelectedPoint is not null;
    public bool CanEditSelectedNode => CanEdit
        && SelectedTreeNode is not null
        && SelectedTreeNode.Kind != DevicePointTreeNodeKind.Root;
    public bool CanDeleteSelectedNode => CanEdit
        && (SelectedTreeNode?.Kind is DevicePointTreeNodeKind.Channel
            or DevicePointTreeNodeKind.Device
            or DevicePointTreeNodeKind.Group
            or DevicePointTreeNodeKind.Point);
    public bool CanMoveSelectedPoint => CanEdit && SelectedPoint is not null && MoveGroupOptions.Count > 0;
    public bool CanApplyMoveSelectedPoint => CanMoveSelectedPoint
        && SelectedMoveGroup is not null
        && !string.Equals(SelectedPoint?.Entry.GroupId, SelectedMoveGroup.Id, StringComparison.OrdinalIgnoreCase);

    [ObservableProperty]
    /// <summary>
    /// 当前选中的点位行。
    /// </summary>
    private DevicePointRow? _selectedPoint;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScopeText))]
    private DevicePointDeviceFilter? _selectedDeviceFilter;

    [ObservableProperty]
    private DevicePointTreeNodeViewModel? _selectedTreeNode;

    [ObservableProperty]
    private string _treeSearchText = string.Empty;

    [ObservableProperty]
    private DevicePointGroupChoice? _selectedMoveGroup;

    partial void OnSelectedDeviceFilterChanged(DevicePointDeviceFilter? value)
    {
        RefreshVisiblePoints();
        OnPropertyChanged(nameof(ScopeText));
    }

    partial void OnSelectedTreeNodeChanged(DevicePointTreeNodeViewModel? value)
    {
        RefreshVisiblePoints();
        RefreshMoveGroups();
        NotifyTreeActions();
        OnPropertyChanged(nameof(SelectedPointDialogDeviceId));
        OnPropertyChanged(nameof(SelectedPointDialogGroupId));
    }

    partial void OnSelectedPointChanged(DevicePointRow? value)
    {
        RefreshMoveGroups();
        OnPropertyChanged(nameof(CanMoveSelectedPoint));
        OnPropertyChanged(nameof(CanApplyMoveSelectedPoint));
        OnPropertyChanged(nameof(CanEditSelectedPoint));
    }

    partial void OnTreeSearchTextChanged(string value)
    {
        RebuildTree();
        NotifyTreeActions();
    }

    partial void OnSelectedMoveGroupChanged(DevicePointGroupChoice? value)
    {
        OnPropertyChanged(nameof(CanMoveSelectedPoint));
        OnPropertyChanged(nameof(CanApplyMoveSelectedPoint));
    }

    /// <summary>
    /// 加载点位目录并刷新列表。
    /// </summary>
    public override Task LoadAsync(CancellationToken ct = default)
    {
        IsBusy = true;
        try
        {
            _allEntries.Clear();
            _allEntries.AddRange(_services.DevicePoints.List());
            _allGroups.Clear();
            _allGroups.AddRange(_services.DevicePoints.ListGroups());
            RebuildDeviceFilters();
            RebuildTree();
            RefreshVisiblePoints();
            RefreshMoveGroups();
            OnPropertyChanged(nameof(HasPoints));
            NotifyTreeActions();
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
        finally { IsBusy = false; }
        return Task.CompletedTask;
    }

    /// <summary>
    /// 保存新增的点位。
    /// </summary>
    public async Task CreateFromDialogAsync(DevicePointDialogResult result)
    {
        var entries = _allEntries.ToList();
        entries.Add(result.ToEntry());
        await SaveEntriesAsync(entries, "设备点位已新增", _allGroups);
    }

    /// <summary>
    /// 保存对选中点位的修改。
    /// </summary>
    public async Task UpdateFromDialogAsync(DevicePointRow row, DevicePointDialogResult result)
    {
        var entries = _allEntries.ToList();
        var index = entries.FindIndex(item => ReferenceEquals(item, row.Entry)
            || (!string.IsNullOrWhiteSpace(item.Id) && string.Equals(item.Id, row.Entry.Id, StringComparison.OrdinalIgnoreCase)));
        if (index < 0) { StatusMessage = "选中的点位已不存在"; return; }
        entries[index] = result.ToEntry();
        await SaveEntriesAsync(entries, "设备点位已更新", _allGroups);
    }

    /// <summary>
    /// 导入点位文件并整体保存。保留该入口供非界面调用；界面入口会先显示预览再调用 ApplyImportedPointsAsync。
    /// </summary>
    public async Task ImportAsync(string filePath)
    {
        var result = await ValidateImportAsync(filePath);
        if (result.IsValid)
            await ApplyImportedPointsAsync(result);
    }

    /// <summary>
    /// 只读取并校验导入文件，不改动当前点位目录。
    /// </summary>
    public async Task<DevicePointImportResult> ValidateImportAsync(string filePath)
    {
        StatusMessage = string.Empty;
        IsBusy = true;
        try
        {
            var result = await _services.DevicePointImporter.ImportAsync(filePath);
            if (result.IsValid && IsGroupedConfiguration)
                result = ResolveImportedGroups(result);
            StatusMessage = result.IsValid
                ? $"文件校验通过：共 {result.Points.Count} 个设备点位，请检查导入预览"
                : FormatImportIssues(result);
            return result;
        }
        catch (Exception ex)
        {
            var result = new DevicePointImportResult(
                Array.Empty<PointsConfig.PointEntry>(),
                new[] { new DevicePointImportIssue(0, ex.Message) });
            StatusMessage = FormatImportIssues(result);
            return result;
        }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// 把已经通过校验并由用户确认的点位整体替换到目录中。
    /// </summary>
    public async Task ApplyImportedPointsAsync(DevicePointImportResult result)
    {
        if (!result.IsValid)
        {
            StatusMessage = FormatImportIssues(result);
            return;
        }

        IsBusy = true;
        try
        {
            var imported = PrepareImportedPoints(result.Points);
            if (IsGroupedConfiguration)
            {
                var resolved = ResolveImportedGroups(new DevicePointImportResult(imported, Array.Empty<DevicePointImportIssue>()));
                if (!resolved.IsValid)
                    throw new Core.Common.DomainException(FormatImportIssues(resolved));
                imported = resolved.Points;
            }
            var entries = IsGroupedConfiguration
                ? MergeImportedPoints(imported)
                : imported;
            var action = IsGroupedConfiguration
                ? $"已合并导入 {result.Points.Count} 个设备点位"
                : $"已导入 {result.Points.Count} 个设备点位";
            await SaveEntriesAsync(entries, action, _allGroups);
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// 用户关闭导入预览时明确提示当前目录没有改变。
    /// </summary>
    public void CancelImportPreview()
        => StatusMessage = "已取消导入，当前设备点位未改变";

    /// <summary>
    /// 下载设备点位导入模板。
    /// </summary>
    public async Task DownloadTemplateAsync(string filePath)
    {
        StatusMessage = string.Empty;
        IsBusy = true;
        try
        {
            await _services.DevicePointTemplateExporter.ExportAsync(filePath);
            StatusMessage = $"设备点位导入模板已保存：{filePath}";
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// 创建信号绑定编辑器；绑定键来自已注册执行器，当前选择来自生效运行时快照。
    /// </summary>
    public SignalBindingsDialogViewModel CreateSignalBindingsDialog()
    {
        if (_services.DeviceConfigurations is null)
            throw new Core.Common.DomainException("完整设备配置服务未连接，不能编辑信号绑定");

        var runtime = _services.DeviceModes.Runtime;
        return new SignalBindingsDialogViewModel(
            GetRequiredSignals(),
            _allEntries,
            _services.DeviceConfig.Devices ?? new List<DeviceConfig.DeviceEntry>(),
            runtime?.SignalBindings?.Bindings,
            runtime?.ActiveRevision ?? string.Empty,
            _services.DeviceConfigurations,
            _actor);
    }

    /// <summary>
    /// 创建通道/设备候选编辑器；编辑器不会直接修改当前生效对象。
    /// </summary>
    public DeviceConfigurationEditorViewModel CreateDeviceConfigurationDialog()
    {
        if (_services.DeviceConfigurations is null)
            throw new Core.Common.DomainException("完整设备配置服务未连接，不能编辑通道和设备");

        var editor = new DeviceConfigurationEditorViewModel(
            _services.DeviceConfig,
            _services.DevicePoints.List(),
            _services.DeviceModes.Runtime?.SignalBindings,
            _services.DriverDescriptors,
            _services.DeviceConfigurations,
            _actor);
        if (SelectedTreeNode?.Kind == DevicePointTreeNodeKind.Channel)
            editor.SelectChannel(SelectedTreeNode.Id);
        else if (SelectedTreeNode?.Kind == DevicePointTreeNodeKind.Device)
            editor.SelectDevice(SelectedTreeNode.Id);
        return editor;
    }

    /// <summary>
    /// 创建当前设备下的分组编辑器；分组不会进入独立持久化路径。
    /// </summary>
    public PointGroupDialogViewModel CreatePointGroupDialog(bool isEdit)
    {
        var deviceId = SelectedTreeNode?.Kind == DevicePointTreeNodeKind.Device
            ? SelectedTreeNode.Id
            : SelectedTreeNode?.Kind == DevicePointTreeNodeKind.Group
                ? SelectedTreeNode.ParentId
            : SelectedPoint?.Entry.DeviceId ?? string.Empty;
        if (string.IsNullOrWhiteSpace(deviceId))
            throw new Core.Common.DomainException("请先选择设备节点");
        var current = isEdit && SelectedTreeNode?.Kind == DevicePointTreeNodeKind.Group
            ? _allGroups.FirstOrDefault(group => string.Equals(group.Id, SelectedTreeNode.Id, StringComparison.OrdinalIgnoreCase))
            : null;
        var defaultCode = _allGroups
            .Where(group => string.Equals(group.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
            .Select(group => group.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var nextCode = Enumerable.Range(1, 1000)
            .Select(index => $"GROUP_{index}")
            .FirstOrDefault(code => !defaultCode.Contains(code)) ?? "GROUP_NEW";
        return new PointGroupDialogViewModel(isEdit, current, deviceId, nextCode);
    }

    /// <summary>
    /// 应用分组新增或修改候选。
    /// </summary>
    public async Task ApplyPointGroupAsync(PointGroupDialogResult result)
    {
        var groups = _allGroups.Select(CloneGroup).ToList();
        var index = groups.FindIndex(group => string.Equals(group.Id, result.GroupId, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) groups[index] = result.ToEntry();
        else groups.Add(result.ToEntry());
        await SaveEntriesAsync(_allEntries, index >= 0 ? "点位分组已更新" : "点位分组已新增", groups);
    }

    /// <summary>
    /// 删除当前选中的树节点。通道和设备沿用同一个候选编辑器，点位分组使用引用安全校验。
    /// </summary>
    public async Task<bool> DeleteSelectedTreeNodeAsync()
    {
        if (SelectedTreeNode is null)
        {
            StatusMessage = "请先选择要删除的节点";
            return false;
        }
        if (SelectedTreeNode.Kind == DevicePointTreeNodeKind.Point)
        {
            await DeletePointAsync();
            return true;
        }
        if (SelectedTreeNode.Kind == DevicePointTreeNodeKind.Group)
        {
            if (_services.DeviceConfigurations is null)
            {
                StatusMessage = "完整设备配置服务未连接，不能删除点位分组";
                return false;
            }
            var result = await _services.DeviceConfigurations.DeletePointGroupAsync(_actor, SelectedTreeNode.Id);
            if (!result.Ok)
            {
                StatusMessage = result.Error ?? "点位分组删除失败";
                return false;
            }
            if (result.Snapshot is not null)
                _services.DevicePoints.AdoptApplied(result.Snapshot.Points);
            await LoadAsync();
            StatusMessage = "点位分组已删除，完整设备配置已应用";
            return true;
        }
        if (SelectedTreeNode.Kind is DevicePointTreeNodeKind.Channel or DevicePointTreeNodeKind.Device)
        {
            StatusMessage = "请在通道/设备编辑器中选择该节点并确认删除；系统会先检查引用关系";
            return false;
        }
        StatusMessage = "根节点不能删除";
        return false;
    }

    /// <summary>
    /// 把选中点位移动到已存在的同设备分组，只改变 GroupId。
    /// </summary>
    public async Task MoveSelectedPointAsync()
    {
        if (SelectedPoint is null || SelectedMoveGroup is null)
        {
            StatusMessage = "请选择点位和目标分组";
            return;
        }
        if (string.Equals(SelectedPoint.Entry.GroupId, SelectedMoveGroup.Id, StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "目标分组与当前分组相同，无需调整";
            return;
        }
        if (!string.Equals(ResolveDeviceId(SelectedPoint.Entry), SelectedMoveGroup.DeviceId, StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "目标分组必须属于当前点位的设备";
            return;
        }
        var entries = _allEntries.ToList();
        var index = entries.FindIndex(point => string.Equals(point.Id, SelectedPoint.Entry.Id, StringComparison.OrdinalIgnoreCase));
        if (index < 0) { StatusMessage = "选中的点位已不存在"; return; }
        entries[index].GroupId = SelectedMoveGroup.Id;
        entries[index].GroupCode = SelectedMoveGroup.Code;
        await SaveEntriesAsync(entries, "点位分组已调整", _allGroups);
    }

    /// <summary>
    /// 设备/通道配置应用成功后更新共享配置对象和点位页。
    /// </summary>
    public async Task ApplyDeviceConfigurationResultAsync(DeviceConfigurationApplyResult result)
    {
        if (!result.Ok) return;
        if (result.Snapshot is not null)
        {
            AdoptDeviceConfig(result.Snapshot.Device);
            _services.DevicePoints.AdoptApplied(result.Snapshot.Points);
        }
        await LoadAsync();
        StatusMessage = $"设备与通道配置已应用，生效版本：{result.Revision}";
        OnPropertyChanged(nameof(ActiveRevisionText));
    }

    /// <summary>
    /// 信号绑定应用成功后同步页面目录并显示新的生效版本。
    /// </summary>
    public async Task ApplySignalBindingsResultAsync(DeviceConfigurationApplyResult result)
    {
        if (!result.Ok) return;
        if (result.Snapshot is not null)
            _services.DevicePoints.AdoptApplied(result.Snapshot.Points);
        await LoadAsync();
        StatusMessage = $"信号绑定已应用，生效版本：{result.Revision}";
        OnPropertyChanged(nameof(ActiveRevisionText));
    }

    /// <summary>
    /// 删除当前选中的点位。
    /// </summary>
    [RelayCommand]
    public async Task DeletePointAsync()
    {
        if (SelectedPoint is null) { StatusMessage = "请先选择设备点位"; return; }
        var selectedEntry = SelectedPoint.Entry;
        var pointId = selectedEntry.Id?.Trim() ?? string.Empty;
        var bindingCount = string.IsNullOrWhiteSpace(pointId)
            ? 0
            : (_services.DeviceModes.Runtime?.SignalBindings.Bindings ?? new Dictionary<string, string>())
                .Count(binding => string.Equals(binding.Value, pointId, StringComparison.OrdinalIgnoreCase));
        if (bindingCount > 0)
        {
            StatusMessage = $"点位“{selectedEntry.Code}”仍被 {bindingCount} 个信号绑定引用，请先解除引用；系统不会静默删除。";
            return;
        }
        var entries = _allEntries.Where(entry => !ReferenceEquals(entry, selectedEntry)).ToList();
        await SaveEntriesAsync(entries, "设备点位已删除", _allGroups);
    }

    /// <summary>
    /// 保存点位列表并重载设备运行时。
    /// </summary>
    private async Task SaveEntriesAsync(
        IReadOnlyList<PointsConfig.PointEntry> entries,
        string successText,
        IReadOnlyList<PointsConfig.PointGroupEntry>? groups = null)
    {
        StatusMessage = string.Empty;
        try
        {
            if (await _services.RecordRepository.GetActiveRunningRecordAsync() is not null)
                throw new Core.Common.DomainException("存在活动试验，禁止修改设备点位；请先结束试验");

            DeviceModeResult? reload = null;
            if (IsGroupedConfiguration)
            {
                if (_services.DeviceConfigurations is null)
                    throw new Core.Common.DomainException("完整设备配置服务未连接，不能单独保存设备点位");
                var applied = await _services.DeviceConfigurations.ApplyPointsAsync(
                    _actor, entries, groups ?? _allGroups);
                if (!applied.Ok)
                    throw new Core.Common.DomainException(applied.Error ?? "设备点位配置应用失败");
                if (applied.Snapshot is not null)
                    _services.DevicePoints.AdoptApplied(applied.Snapshot.Points);
            }
            else
            {
                await _services.DevicePoints.SaveAsync(_actor, entries);
                reload = await _services.DeviceModes.InitializeAsync(_services.DeviceModes.CurrentMode);
            }
            await LoadAsync();
            OnPropertyChanged(nameof(ActiveRevisionText));
            StatusMessage = reload is null || reload.Ok
                ? $"{successText}，完整设备配置已应用"
                : $"{successText}，但运行时重载失败：{reload.Error}";
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
    }

    /// <summary>
    /// 当前导入按 PointId 更新、按设备编码+点位编码补充更新，其余现有点位保持不变。
    /// </summary>
    private IReadOnlyList<PointsConfig.PointEntry> MergeImportedPoints(IReadOnlyList<PointsConfig.PointEntry> imported)
    {
        var merged = _allEntries.ToList();
        foreach (var incoming in imported)
        {
            var existingIndex = -1;
            if (Guid.TryParse(incoming.Id, out _))
            {
                existingIndex = merged.FindIndex(point =>
                    string.Equals(point.Id, incoming.Id, StringComparison.OrdinalIgnoreCase));
                if (existingIndex >= 0 && !string.IsNullOrWhiteSpace(incoming.DeviceCode))
                {
                    var existingDevice = ResolveDeviceCode(merged[existingIndex]);
                    if (!string.Equals(existingDevice, incoming.DeviceCode, StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(merged[existingIndex].Code, incoming.Code, StringComparison.OrdinalIgnoreCase))
                        throw new Core.Common.DomainException($"点位标识 {incoming.Id} 与设备编码/点位编码不一致");
        }
    }

            if (existingIndex < 0 && !string.IsNullOrWhiteSpace(incoming.DeviceCode))
            {
                existingIndex = merged.FindIndex(point =>
                    string.Equals(ResolveDeviceCode(point), incoming.DeviceCode, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(point.Code, incoming.Code, StringComparison.OrdinalIgnoreCase));
            }

            if (existingIndex >= 0)
            {
                var replacement = incoming;
                if (string.IsNullOrWhiteSpace(replacement.Id)) replacement.Id = merged[existingIndex].Id;
                merged[existingIndex] = replacement;
            }
            else
            {
                var codeConflict = merged.FirstOrDefault(point =>
                    string.Equals(point.Code, incoming.Code, StringComparison.OrdinalIgnoreCase));
                if (codeConflict is not null)
                    throw new Core.Common.DomainException($"点位编码已存在但设备不一致：{incoming.Code}");
                merged.Add(incoming);
            }
        }
        return merged;
    }

    private IReadOnlyList<PointsConfig.PointEntry> PrepareImportedPoints(
        IReadOnlyList<PointsConfig.PointEntry> imported)
    {
        if (!IsGroupedConfiguration
            || imported.All(point => !string.IsNullOrWhiteSpace(point.DeviceCode)))
            return imported.ToList();

        var devices = DialogDeviceOptions;
        if (devices.Count != 1)
            throw new Core.Common.DomainException("旧版点位模板缺少设备编码；多设备页面必须使用模板 v2 并逐行填写设备编码");
        var device = devices[0];
        foreach (var point in imported.Where(point => string.IsNullOrWhiteSpace(point.DeviceCode)))
        {
            point.DeviceCode = device.Code;
            point.DeviceId = device.Id;
        }
        return imported.ToList();
    }

    private DevicePointImportResult ResolveImportedGroups(DevicePointImportResult result)
    {
        var devices = DialogDeviceOptions.ToDictionary(device => device.Code, StringComparer.OrdinalIgnoreCase);
        var issues = result.Issues.ToList();
        foreach (var point in result.Points)
        {
            if (!devices.TryGetValue(point.DeviceCode.Trim(), out var device))
            {
                issues.Add(new DevicePointImportIssue(0,
                    $"点位 {point.Code} 引用的设备不存在：{point.DeviceCode}"));
                continue;
            }

            var groupCode = string.IsNullOrWhiteSpace(point.GroupCode) ? "DEFAULT" : point.GroupCode.Trim();
            var group = _allGroups.FirstOrDefault(item =>
                string.Equals(item.DeviceId, device.Id, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.Code, groupCode, StringComparison.OrdinalIgnoreCase));
            if (group is null)
            {
                issues.Add(new DevicePointImportIssue(0,
                    $"点位 {point.Code} 引用的分组不存在：设备 {device.Code} / {groupCode}"));
                continue;
            }

            point.DeviceId = device.Id;
            point.DeviceCode = device.Code;
            point.GroupId = group.Id;
            point.GroupCode = group.Code;
        }
        return new DevicePointImportResult(result.Points, issues);
    }

    private void RebuildTree()
    {
        var previousId = SelectedTreeNode?.Id;
        var root = new DevicePointTreeNodeViewModel(
            DevicePointTreeNodeKind.Root,
            string.Empty,
            null,
            "设备与通道",
            string.Empty,
            $"{_allEntries.Count} 个点位 · {_allGroups.Count} 个分组");
        root.IsExpanded = true;

        var devices = (_services.DeviceConfig.Devices ?? new List<DeviceConfig.DeviceEntry>())
            .Where(device => device is not null)
            .ToList();
        var channels = (_services.DeviceConfig.Channels ?? new List<ChannelEntry>())
            .Where(channel => channel is not null)
            .ToList();
        if (channels.Count == 0 && devices.Count > 0)
        {
            channels.Add(new ChannelEntry
            {
                Id = DeviceConfigurationMigrator.StableId("legacy-channel"),
                Code = "LEGACY",
                Name = "兼容通道",
                TransportKind = ChannelTransportKind.Simulation,
                Enabled = true
            });
        }

        foreach (var channel in channels.OrderBy(item => item.Code, StringComparer.OrdinalIgnoreCase))
        {
            var channelDevices = devices
                .Where(device => string.Equals(device.ChannelId, channel.Id, StringComparison.OrdinalIgnoreCase))
                .OrderBy(device => device.Code, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (channels.Count == 1 && string.IsNullOrWhiteSpace(channelDevices.FirstOrDefault()?.ChannelId))
                channelDevices = devices.OrderBy(device => device.Code, StringComparer.OrdinalIgnoreCase).ToList();

            var channelNode = new DevicePointTreeNodeViewModel(
                DevicePointTreeNodeKind.Channel,
                channel.Id,
                root.Id,
                string.IsNullOrWhiteSpace(channel.Code) ? "未命名通道" : channel.Code.Trim(),
                string.IsNullOrWhiteSpace(channel.Name) ? channel.Code.Trim() : channel.Name.Trim(),
                BuildChannelSummary(channel, channelDevices));
            channelNode.IsExpanded = string.IsNullOrWhiteSpace(TreeSearchText);
            foreach (var device in channelDevices)
                channelNode.Children.Add(BuildDeviceNode(device, channelNode.Id));
            root.Children.Add(channelNode);
        }

        // 对异常/旧配置保留可见性，避免树状页面把未能映射到通道的设备静默隐藏。
        var mappedDeviceIds = channels.SelectMany(channel =>
                devices.Where(device => string.Equals(device.ChannelId, channel.Id, StringComparison.OrdinalIgnoreCase)))
            .Select(device => device.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var device in devices.Where(device => !mappedDeviceIds.Contains(device.Id)))
            root.Children.Add(BuildDeviceNode(device, root.Id));

        var query = TreeSearchText.Trim();
        if (!string.IsNullOrWhiteSpace(query))
            FilterTree(root, query);

        TreeNodes.Clear();
        TreeNodes.Add(root);
        SelectedTreeNode = FindTreeNode(root, previousId) ?? root;
        NotifyTreeActions();
    }

    private DevicePointTreeNodeViewModel BuildDeviceNode(
        DeviceConfig.DeviceEntry device,
        string parentId)
    {
        var deviceCode = string.IsNullOrWhiteSpace(device.Code) ? device.Name : device.Code.Trim();
        var deviceId = string.IsNullOrWhiteSpace(device.Id) ? deviceCode : device.Id.Trim();
        var devicePoints = _allEntries.Where(point =>
                string.Equals(ResolveDeviceId(point), deviceId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var deviceGroups = _allGroups
            .Where(group => string.Equals(group.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(group => group.SortOrder)
            .ThenBy(group => group.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (deviceGroups.Count == 0)
        {
            deviceGroups.Add(new PointsConfig.PointGroupEntry
            {
                Id = DeviceConfigurationMigrator.StableId($"legacy-group|{deviceId}|DEFAULT"),
                DeviceId = deviceId,
                Code = "DEFAULT",
                Name = "未分组",
                Description = "旧版兼容显示分组"
            });
        }

        var node = new DevicePointTreeNodeViewModel(
            DevicePointTreeNodeKind.Device,
            deviceId,
            parentId,
            string.IsNullOrWhiteSpace(deviceCode) ? "未命名设备" : deviceCode,
            string.IsNullOrWhiteSpace(device.Name) ? deviceCode : device.Name.Trim(),
            BuildDeviceSummary(device, devicePoints.Count));
        node.IsExpanded = string.IsNullOrWhiteSpace(TreeSearchText);
        foreach (var group in deviceGroups)
        {
            var groupPoints = devicePoints.Where(point =>
                    string.Equals(point.GroupId, group.Id, StringComparison.OrdinalIgnoreCase)
                    || (string.IsNullOrWhiteSpace(point.GroupId) && group.Code == "DEFAULT"))
                .OrderBy(point => point.Code, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var groupNode = new DevicePointTreeNodeViewModel(
                DevicePointTreeNodeKind.Group,
                group.Id,
                node.Id,
                group.Code,
                group.Name,
                $"{groupPoints.Count} 个点位 · 仅用于组织和筛选");
            groupNode.IsExpanded = string.IsNullOrWhiteSpace(TreeSearchText);
            foreach (var point in groupPoints)
            {
                var pointId = string.IsNullOrWhiteSpace(point.Id)
                    ? DeviceConfigurationMigrator.StableId($"legacy-point|{deviceId}|{point.Code}|{point.Address}")
                    : point.Id;
                groupNode.Children.Add(new DevicePointTreeNodeViewModel(
                    DevicePointTreeNodeKind.Point,
                    pointId,
                    groupNode.Id,
                    point.Code,
                    point.Name,
                    $"{point.Address} · {(point.IsEnabled ? "启用" : "停用")}"));
            }
            node.Children.Add(groupNode);
        }
        return node;
    }

    private string BuildChannelSummary(ChannelEntry channel, IReadOnlyList<DeviceConfig.DeviceEntry> devices)
    {
        var disabled = !channel.Enabled;
        var unimplemented = devices.Any(device => IsUnimplementedDriver(device.DriverKey));
        var state = disabled ? "已停用" : unimplemented ? "包含未实现驱动" : "已配置";
        return $"{state} · {devices.Count} 个设备";
    }

    private string BuildDeviceSummary(DeviceConfig.DeviceEntry device, int pointCount)
    {
        var state = !device.Enabled
            ? "已停用"
            : IsUnimplementedDriver(device.DriverKey)
                ? "未实现驱动 · 不可应用"
                : GetRuntimeStatusText(device.Id);
        return $"{state} · {pointCount} 个点位";
    }

    private bool IsUnimplementedDriver(string? driverKey)
        => !string.IsNullOrWhiteSpace(driverKey)
           && _services.DriverDescriptors?.FirstOrDefault(descriptor =>
               string.Equals(descriptor.DriverKey, driverKey, StringComparison.OrdinalIgnoreCase)) is { IsImplemented: false };

    private string GetRuntimeStatusText(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return "未读取";
        try
        {
            var status = _services.DeviceModes.Runtime?.GetDeviceStatus(deviceId);
            if (status is null) return "未读取";
            return status.ConnectionState switch
            {
                DeviceConnectionState.Disabled => "已停用",
                DeviceConnectionState.Connecting => "连接中",
                DeviceConnectionState.Online => "在线",
                DeviceConnectionState.Degraded => "降级",
                DeviceConnectionState.Offline => "离线",
                DeviceConnectionState.Faulted => "故障",
                _ => "未读取"
            };
        }
        catch { return "未读取"; }
    }

    private static bool FilterTree(DevicePointTreeNodeViewModel node, string query)
    {
        var keptChild = false;
        foreach (var child in node.Children.ToList())
        {
            if (FilterTree(child, query))
                keptChild = true;
            else
                node.Children.Remove(child);
        }
        var selfMatches = node.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase);
        if (selfMatches && node.Kind != DevicePointTreeNodeKind.Root)
        {
            node.IsExpanded = true;
            return true;
        }
        return node.Kind == DevicePointTreeNodeKind.Root || keptChild;
    }

    private static DevicePointTreeNodeViewModel? FindTreeNode(
        DevicePointTreeNodeViewModel node,
        string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        if (string.Equals(node.Id, id, StringComparison.OrdinalIgnoreCase)) return node;
        foreach (var child in node.Children)
        {
            var found = FindTreeNode(child, id);
            if (found is not null) return found;
        }
        return null;
    }

    private void NotifyTreeActions()
    {
        OnPropertyChanged(nameof(SelectedScopeTitle));
        OnPropertyChanged(nameof(SelectedScopeText));
        OnPropertyChanged(nameof(ScopeText));
        OnPropertyChanged(nameof(CanAddChannel));
        OnPropertyChanged(nameof(CanAddDevice));
        OnPropertyChanged(nameof(CanAddGroup));
        OnPropertyChanged(nameof(CanAddPoint));
        OnPropertyChanged(nameof(CanEditSelectedNode));
        OnPropertyChanged(nameof(CanDeleteSelectedNode));
        OnPropertyChanged(nameof(CanMoveSelectedPoint));
        OnPropertyChanged(nameof(CanApplyMoveSelectedPoint));
        OnPropertyChanged(nameof(CanEditSelectedPoint));
    }

    private void RefreshMoveGroups()
    {
        var deviceId = SelectedPoint is not null
            ? ResolveDeviceId(SelectedPoint.Entry)
            : SelectedTreeNode?.Kind == DevicePointTreeNodeKind.Device
                ? SelectedTreeNode.Id
                : SelectedTreeNode?.ParentId ?? string.Empty;
        MoveGroupOptions.Clear();
        foreach (var group in _allGroups
                     .Where(group => string.Equals(group.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(group => group.SortOrder)
                     .ThenBy(group => group.Code, StringComparer.OrdinalIgnoreCase))
            MoveGroupOptions.Add(new DevicePointGroupChoice(group.Id, group.DeviceId, group.Code, group.Name, group.SortOrder));

        var currentGroupId = SelectedPoint?.Entry.GroupId;
        SelectedMoveGroup = MoveGroupOptions.FirstOrDefault(group =>
            string.Equals(group.Id, currentGroupId, StringComparison.OrdinalIgnoreCase));
        OnPropertyChanged(nameof(CanMoveSelectedPoint));
        OnPropertyChanged(nameof(CanApplyMoveSelectedPoint));
    }

    private string ResolveDeviceId(PointsConfig.PointEntry point)
    {
        if (!string.IsNullOrWhiteSpace(point.DeviceId)) return point.DeviceId.Trim();
        var code = point.DeviceCode?.Trim() ?? string.Empty;
        var device = (_services.DeviceConfig.Devices ?? new List<DeviceConfig.DeviceEntry>())
            .FirstOrDefault(item => !string.IsNullOrWhiteSpace(code)
                && string.Equals(item.Code, code, StringComparison.OrdinalIgnoreCase));
        if (device is not null) return device.Id.Trim();
        var only = (_services.DeviceConfig.Devices ?? new List<DeviceConfig.DeviceEntry>()).Count == 1
            ? (_services.DeviceConfig.Devices ?? new List<DeviceConfig.DeviceEntry>()).FirstOrDefault()
            : null;
        if (only is null) return string.Empty;
        return !string.IsNullOrWhiteSpace(only.Id)
            ? only.Id.Trim()
            : !string.IsNullOrWhiteSpace(only.Code) ? only.Code.Trim() : only.Name.Trim();
    }

    private void RebuildDeviceFilters()
    {
        var previousId = SelectedDeviceFilter?.Id;
        DeviceFilters.Clear();
        var all = new DevicePointDeviceFilter
        {
            Id = string.Empty,
            Code = "ALL",
            Name = "全部设备",
            IsAll = true,
            PointCount = _allEntries.Count,
            StatusText = "配置范围"
        };
        DeviceFilters.Add(all);

        foreach (var device in (_services.DeviceConfig.Devices ?? new List<DeviceConfig.DeviceEntry>())
                     .Where(device => !string.IsNullOrWhiteSpace(device.Id) || !string.IsNullOrWhiteSpace(device.Code)))
        {
            var code = string.IsNullOrWhiteSpace(device.Code) ? device.Name : device.Code.Trim();
            var filter = new DevicePointDeviceFilter
            {
                Id = string.IsNullOrWhiteSpace(device.Id) ? code : device.Id,
                Code = string.IsNullOrWhiteSpace(code) ? "未命名设备" : code,
                Name = string.IsNullOrWhiteSpace(device.Name) ? code : device.Name.Trim(),
                PointCount = _allEntries.Count(point =>
                    string.Equals(point.DeviceId, device.Id, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(point.DeviceCode, code, StringComparison.OrdinalIgnoreCase)),
                StatusText = device.Enabled ? "已启用" : "已停用"
            };
            DeviceFilters.Add(filter);
        }

        SelectedDeviceFilter = DeviceFilters.FirstOrDefault(filter => filter.Id == previousId)
            ?? DeviceFilters.FirstOrDefault();
    }

    private IReadOnlyList<RequiredSignal> GetRequiredSignals()
        => _services.Executors.Codes
            .SelectMany(code => _services.Executors.Get(code).RequiredSignals ?? Array.Empty<RequiredSignal>())
            .Where(signal => signal is not null && !string.IsNullOrWhiteSpace(signal.SignalKey))
            .GroupBy(signal => signal.SignalKey.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

    private void AdoptDeviceConfig(DeviceConfig source)
    {
        _services.DeviceConfig.SchemaVersion = source.SchemaVersion;
        _services.DeviceConfig.DeviceMode = source.DeviceMode;
        _services.DeviceConfig.PollIntervalMs = source.PollIntervalMs;
        _services.DeviceConfig.TimeoutMs = source.TimeoutMs;
        _services.DeviceConfig.Channels = source.Channels;
        _services.DeviceConfig.Devices = source.Devices;
    }

    private void RefreshVisiblePoints()
    {
        var selected = SelectedTreeNode;
        var visible = selected?.Kind switch
        {
            DevicePointTreeNodeKind.Channel => PointsForDeviceIds(
                selected.Children.Select(child => child.Id).ToHashSet(StringComparer.OrdinalIgnoreCase)),
            DevicePointTreeNodeKind.Device => _allEntries.Where(point =>
                string.Equals(ResolveDeviceId(point), selected.Id, StringComparison.OrdinalIgnoreCase)).ToList(),
            DevicePointTreeNodeKind.Group => _allEntries.Where(point =>
                string.Equals(point.GroupId, selected.Id, StringComparison.OrdinalIgnoreCase)
                || (string.IsNullOrWhiteSpace(point.GroupId)
                    && string.Equals(selected.Code, "DEFAULT", StringComparison.OrdinalIgnoreCase))).ToList(),
            DevicePointTreeNodeKind.Point => _allEntries.Where(point =>
                string.Equals(point.Id, selected.Id, StringComparison.OrdinalIgnoreCase)).ToList(),
            _ => _allEntries.ToList()
        };

        Points.Clear();
        foreach (var point in visible)
        {
            var group = _allGroups.FirstOrDefault(item =>
                string.Equals(item.Id, point.GroupId, StringComparison.OrdinalIgnoreCase));
            Points.Add(new DevicePointRow
            {
                Entry = point,
                DeviceCode = ResolveDeviceCode(point),
                GroupCode = group?.Code ?? (string.IsNullOrWhiteSpace(point.GroupCode) ? "DEFAULT" : point.GroupCode),
                GroupName = group?.Name ?? "未分组"
            });
        }
        if (selected?.Kind == DevicePointTreeNodeKind.Point)
            SelectedPoint = Points.FirstOrDefault();
        else if (SelectedPoint is not null && !Points.Contains(SelectedPoint))
            SelectedPoint = null;
        OnPropertyChanged(nameof(HasPoints));
        OnPropertyChanged(nameof(SelectedScopeTitle));
        OnPropertyChanged(nameof(SelectedScopeText));
        OnPropertyChanged(nameof(ScopeText));
        OnPropertyChanged(nameof(CanMoveSelectedPoint));
    }

    private List<PointsConfig.PointEntry> PointsForDeviceIds(ISet<string> deviceIds)
        => _allEntries.Where(point => deviceIds.Contains(ResolveDeviceId(point))).ToList();

    private string ResolveDeviceCode(PointsConfig.PointEntry point)
    {
        if (!string.IsNullOrWhiteSpace(point.DeviceCode)) return point.DeviceCode;
        var device = (_services.DeviceConfig.Devices ?? new List<DeviceConfig.DeviceEntry>())
            .FirstOrDefault(item => !string.IsNullOrWhiteSpace(point.DeviceId)
                && string.Equals(item.Id, point.DeviceId, StringComparison.OrdinalIgnoreCase));
        if (device is not null) return string.IsNullOrWhiteSpace(device.Code) ? device.Name : device.Code;
        var only = (_services.DeviceConfig.Devices ?? new List<DeviceConfig.DeviceEntry>()).Count == 1
            ? (_services.DeviceConfig.Devices ?? new List<DeviceConfig.DeviceEntry>()).FirstOrDefault()
            : null;
        return only is null ? "未分配" : string.IsNullOrWhiteSpace(only.Code) ? only.Name : only.Code;
    }

    private static PointsConfig.PointGroupEntry CloneGroup(PointsConfig.PointGroupEntry source)
        => new()
        {
            Id = source.Id,
            DeviceId = source.DeviceId,
            Code = source.Code,
            Name = source.Name,
            Description = source.Description,
            SortOrder = source.SortOrder
        };

    /// <summary>
    /// 把导入问题整理为提示文字。
    /// </summary>
    private static string FormatImportIssues(DevicePointImportResult result)
    {
        var displayed = result.Issues.Take(8).Select(issue => issue.RowNumber > 0
            ? $"第 {issue.RowNumber} 行：{issue.Message}"
            : issue.Message);
        var suffix = result.Issues.Count > 8 ? $"；另有 {result.Issues.Count - 8} 个错误" : string.Empty;
        return "导入未保存：" + string.Join("；", displayed) + suffix;
    }
}
