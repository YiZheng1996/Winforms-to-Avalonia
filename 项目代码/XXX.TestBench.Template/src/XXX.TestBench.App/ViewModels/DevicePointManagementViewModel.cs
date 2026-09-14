using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XXX.TestBench.App.Composition;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Common;
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
    public string DisplayName => IsAll ? "全部设备" : string.IsNullOrWhiteSpace(Name) ? Code : Name;
    public string SummaryText => IsAll ? $"{PointCount} 个点位" : $"{StatusText} · {PointCount} 个点位";
}

/// <summary>
/// 设备点位模板的下载范围。范围只影响模板提示、下拉选项和示例，不改变设备配置。
/// </summary>
public enum DevicePointTemplateScopeKind
{
    CurrentRange,
    CurrentDevice,
    CurrentProtocol
}

/// <summary>
/// 设备点位工作区模式。实时监视只展示运行时缓存，不直接发起硬件读取。
/// </summary>
public enum DevicePointWorkspaceMode
{
    Configuration,
    LiveMonitor
}

/// <summary>
/// 模板范围的客户可读选项。
/// </summary>
public sealed record DevicePointTemplateScopeChoice(
    string Id,
    string DisplayName,
    string Description,
    DevicePointTemplateScopeKind Kind,
    string? DeviceId = null,
    DevicePointProtocol? Protocol = null)
{
    public override string ToString() => DisplayName;
}

/// <summary>
/// 设备点位表格行：页面只展示配置副本，不直接修改运行时点位。
/// </summary>
public sealed partial class DevicePointRow : ObservableObject
{
    /// <summary>
    /// 点位配置副本。
    /// </summary>
    public required PointsConfig.PointEntry Entry { get; set; }
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
    /// 简化清单的点位名称；没有名称时回退到稳定编码。
    /// </summary>
    public string PointName => string.IsNullOrWhiteSpace(Name) ? Code : Name;
    /// <summary>
    /// 客户清单和简化导入模板使用的“分组.点位”标签。
    /// </summary>
    public string PointTag => DevicePointTag.Format(
        GroupCode,
        string.IsNullOrWhiteSpace(Name) ? Code : Name);
    /// <summary>
    /// 所属设备编码，用于当前多设备配置的定位。
    /// </summary>
    public string DeviceCode { get; set; } = string.Empty;
    /// <summary>
    /// 所属设备名称，优先作为清单中的主显示文字。
    /// </summary>
    public string DeviceName { get; set; } = string.Empty;
    public string DeviceText => string.IsNullOrWhiteSpace(DeviceName) ? DeviceCode : DeviceName;
    public string DeviceCodeText => string.IsNullOrWhiteSpace(DeviceCode) ? string.Empty : $"设备编码：{DeviceCode}";
    /// <summary>
    /// 所属分组编码。
    /// </summary>
    public string GroupCode { get; set; } = string.Empty;
    /// <summary>
    /// 所属分组名称。
    /// </summary>
    public string GroupName { get; set; } = string.Empty;
    /// <summary>
    /// 面向客户显示的分组。
    /// </summary>
    public string GroupText => string.IsNullOrWhiteSpace(GroupCode)
        || string.Equals(GroupCode, "DEFAULT", StringComparison.OrdinalIgnoreCase)
        ? string.Empty
        : string.IsNullOrWhiteSpace(GroupName) ? GroupCode : GroupName;
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
    /// 设备点位用途说明，对应参考模板中的“说明”字段。
    /// </summary>
    public string Description => Entry.Description;
    /// <summary>
    /// 采集周期来自所属设备配置，不在点位层重复维护。
    /// </summary>
    public string PollIntervalText { get; set; } = "未配置";
    /// <summary>
    /// 实时监视的工程值、原始值、质量和更新时间。它们来自运行时缓存。
    /// </summary>
    [ObservableProperty]
    private string _currentValueText = "—";
    [ObservableProperty]
    private string _rawValueText = "—";
    [ObservableProperty]
    private string _qualityText = "未读取";
    [ObservableProperty]
    private string _timestampText = "—";
    [ObservableProperty]
    private string _qualityBackground = "#F2F4F7";
    [ObservableProperty]
    private string _qualityForeground = "#667085";
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
    /// 访问权限显示文字。客户只需要区分只读和读写，风险等级仍由运行时安全链单独校验。
    /// </summary>
    public string WritePolicyText => !Entry.IsWritable
        ? "只读"
        : "读写";
    public string GroupDisplayText => string.IsNullOrWhiteSpace(GroupText) ? "未分组" : GroupText;
    public string WritePolicyBackground => !Entry.IsWritable ? "#F2F4F7" : "#EAF2FF";
    public string WritePolicyForeground => !Entry.IsWritable ? "#667085" : "#0758D7";

    public void UpdateCachedValue(PointValue? value)
    {
        if (value is null)
        {
            CurrentValueText = "—";
            RawValueText = "—";
            QualityText = "未读取";
            TimestampText = "—";
            QualityBackground = "#F2F4F7";
            QualityForeground = "#667085";
            return;
        }

        CurrentValueText = FormatValue(value.Value);
        RawValueText = FormatValue(value.RawValue ?? value.Value);
        QualityText = value.Quality switch
        {
            PointQuality.Good => "正常",
            PointQuality.Stale => "陈旧",
            PointQuality.Bad => "无效",
            _ => "未读取"
        };
        TimestampText = value.TimestampUtc == default
            ? "—"
            : value.TimestampUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        (QualityBackground, QualityForeground) = value.Quality switch
        {
            PointQuality.Good => ("#E8F7EF", "#147A45"),
            PointQuality.Stale => ("#FFF4D8", "#946200"),
            PointQuality.Bad => ("#FFF0EE", "#C9362B"),
            _ => ("#F2F4F7", "#667085")
        };
    }

    private static string FormatValue(object? value)
        => value switch
        {
            null => "—",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? "—",
            _ => value.ToString() ?? "—"
        };

    /// <summary>
    /// 用相同 PointId 更新一行，保留表格选择和滚动容器可以复用的行实例。
    /// </summary>
    public void UpdateFrom(
        PointsConfig.PointEntry entry,
        string deviceCode,
        string deviceName,
        string groupCode,
        string groupName,
        string pollIntervalText)
    {
        Entry = entry;
        DeviceCode = deviceCode;
        DeviceName = deviceName;
        GroupCode = groupCode;
        GroupName = groupName;
        PollIntervalText = pollIntervalText;
        OnPropertyChanged(string.Empty);
    }
}

/// <summary>
/// 设备点位页的可恢复 UI 状态。配置应用只更新数据，不应把用户的导航状态归零。
/// </summary>
public sealed record DevicePointUiState(
    string? SelectedNodeKey,
    IReadOnlySet<string> ExpandedNodeKeys,
    string? SelectedPointId,
    string? TopVisiblePointId,
    string TreeSearchText,
    string PointSearchText,
    string? SortField = null,
    bool SortAscending = true);

/// <summary>
/// 设备点位管理：支持模板下载、手工新增/编辑/删除，以及按固定中文格式校验后的 Excel/CSV 整体导入。
/// </summary>
public sealed partial class DevicePointManagementViewModel : PageViewModel
{
    private sealed record UiRestoreIntent(string? NodeKey, string? PointId);

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
    private readonly Dictionary<string, DevicePointTreeNodeViewModel> _treeNodeCache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DevicePointRow> _pointRowCache =
        new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _expandedNodeKeysBeforeSearch = new(StringComparer.OrdinalIgnoreCase);
    private string? _selectedNodeKeyBeforeSearch;
    private bool _treeSearchWasActive;

    /// <summary>
    /// 创建设备点位管理视图模型。
    /// </summary>
    public DevicePointManagementViewModel(ShellServices services, UserContext actor)
    {
        _services = services;
        _actor = actor;
        EventLog = new DeviceEventLogViewModel(services.DeviceEvents);
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
    /// 页面底部的结构化设备事件日志。
    /// </summary>
    public DeviceEventLogViewModel EventLog { get; }
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
    /// 设备筛选摘要集合，当前页面主要使用下面的“通道 → 设备 → 分组”树。
    /// </summary>
    public ObservableCollection<DevicePointDeviceFilter> DeviceFilters { get; } = new();
    /// <summary>
    /// 面向客户的设备树：全部设备 → 通道 → 设备 → 已配置分组。
    /// 未分组点位不生成树节点，点位只在右侧清单显示。
    /// </summary>
    public ObservableCollection<DevicePointTreeNodeViewModel> TreeNodes { get; } = new();
    /// <summary>
    /// 点位移动操作可选择的当前设备分组。
    /// </summary>
    public ObservableCollection<DevicePointGroupChoice> MoveGroupOptions { get; } = new();
    /// <summary>
    /// 模板下载范围选项。选项依据当前左侧树节点动态生成。
    /// </summary>
    public ObservableCollection<DevicePointTemplateScopeChoice> TemplateScopeOptions { get; } = new();
    /// <summary>
    /// 只筛选右侧点位清单；不会改变左侧树的层级或筛选文本。
    /// </summary>
    [ObservableProperty]
    private string _pointSearchText = string.Empty;
    /// <summary>
    /// 当前用户是否有设备管理权限。
    /// </summary>
    public bool CanEdit => _actor.HasPermission(PermissionCode.ManageDevices);
    /// <summary>
    /// 是否已有点位数据。
    /// </summary>
    public bool HasPoints => Points.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfigurationMode))]
    [NotifyPropertyChangedFor(nameof(IsLiveMonitorMode))]
    private DevicePointWorkspaceMode _workspaceMode = DevicePointWorkspaceMode.Configuration;

    public bool IsConfigurationMode => WorkspaceMode == DevicePointWorkspaceMode.Configuration;
    public bool IsLiveMonitorMode => WorkspaceMode == DevicePointWorkspaceMode.LiveMonitor;

    [RelayCommand]
    public void ShowConfiguration() => WorkspaceMode = DevicePointWorkspaceMode.Configuration;

    [RelayCommand]
    public void ShowLiveMonitor()
    {
        WorkspaceMode = DevicePointWorkspaceMode.LiveMonitor;
        RefreshCachedValues();
    }

    /// <summary>
    /// 从运行时缓存更新当前清单，绝不调用 ReadAsync 或 ReadFreshAsync。
    /// </summary>
    [RelayCommand]
    public void RefreshCachedValues()
    {
        var runtime = _services.DeviceModes.Runtime;
        foreach (var row in Points)
        {
            if (runtime is not null
                && runtime.TryGetCachedValue(row.PointId, out var value))
                row.UpdateCachedValue(value);
            else
                row.UpdateCachedValue(null);
        }
    }

    /// <summary>
    /// 当前是否正在筛选右侧点位清单。
    /// </summary>
    public bool HasPointSearch => !string.IsNullOrWhiteSpace(PointSearchText);

    /// <summary>
    /// 右侧清单的空状态标题。搜索无结果和范围本身为空必须分开表达。
    /// </summary>
    public string EmptyPointsTitle => HasPointSearch
        ? "没有符合条件的点位"
        : "当前范围暂无点位";

    /// <summary>
    /// 右侧清单的空状态操作提示。
    /// </summary>
    public string EmptyPointsHint => HasPointSearch
        ? "可清除搜索条件，或切换左侧范围继续查找。"
        : "可切换左侧范围，或使用顶部“导入/导出”导入模板。";

    /// <summary>
    /// 点位来源说明文字。
    /// </summary>
    public string SourceText => "数据来源：当前生效的设备配置；导入模板使用固定中文格式";

    /// <summary>
    /// 当前设备运行时所使用的完整配置版本。
    /// </summary>
    public string ActiveRevisionText => _services.DeviceModes.Runtime switch
    {
        null => "配置状态：未加载",
        { ActiveRevision.Length: > 0 } => "配置状态：已生效",
        _ => "配置状态：未生成生效版本"
    };

    public string RuntimeStatusText
    {
        get
        {
            var runtime = _services.DeviceModes.Runtime;
            if (runtime is null)
                return "运行：未加载";
            if (runtime.IsSimulation)
                return "运行：仿真";
            var device = ResolveSelectedDevice();
            var status = device is null
                ? runtime.Status.ConnectionState
                : runtime.GetDeviceStatus(device.Id).ConnectionState;
            return $"运行：{status switch
            {
                DeviceConnectionState.Connecting => "连接中",
                DeviceConnectionState.Online => "在线",
                DeviceConnectionState.Degraded => "降级",
                DeviceConnectionState.Offline => "离线",
                DeviceConnectionState.Faulted => "故障",
                _ => "未读取"
            }}";
        }
    }

    /// <summary>
    /// 当前模板下载范围的客户可读提示。
    /// </summary>
    public string TemplateScopeHint =>
        $"模板范围跟随左侧当前选择：{GetCurrentTemplateScopeName()}";

    /// <summary>
    /// 模板范围有效且页面未执行其他操作时才允许下载。
    /// </summary>
    public bool CanDownloadTemplate => !IsBusy
        && IsGroupedConfiguration
        && GetCurrentTemplateDevices().Count > 0;

    public bool CanTestSelectedConnection
        => !IsBusy
           && _services.DeviceConnectionTester is not null
           && ResolveSelectedDevice() is { DeviceMode: DeviceMode.Hardware }
           && ResolveSelectedChannel() is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConnectionTestResult))]
    private string _connectionTestResultText = string.Empty;

    public bool HasConnectionTestResult => !string.IsNullOrWhiteSpace(ConnectionTestResultText);

    /// <summary>
    /// 测试当前设备连接。该操作只针对当前配置候选做连接握手，不保存配置。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanTestSelectedConnection))]
    public async Task TestSelectedConnectionAsync(CancellationToken ct = default)
    {
        var device = ResolveSelectedDevice();
        var channel = ResolveSelectedChannel();
        if (_services.DeviceConnectionTester is null || device is null || channel is null)
        {
            ConnectionTestResultText = "请选择一个硬件设备";
            return;
        }
        if (device.DeviceMode != DeviceMode.Hardware)
        {
            ConnectionTestResultText = "仿真设备不需要测试 PLC 连接";
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _services.DeviceConnectionTester.TestAsync(device, channel, ct);
            ConnectionTestResultText = DeviceConnectionTestResultFormatter.Format(result);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            ConnectionTestResultText = DeviceConnectionTestResultFormatter.FormatTimeout();
        }
        catch (Exception ex)
        {
            ConnectionTestResultText = DeviceConnectionTestResultFormatter.FormatException(ex);
        }
        finally
        {
            IsBusy = false;
            TestSelectedConnectionCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>
    /// 读取当前树范围的一次新鲜样本；这是显式诊断动作，实时监视本身不调用它。
    /// </summary>
    [RelayCommand]
    public async Task ReadCurrentScopeAsync(CancellationToken ct = default)
    {
        var runtime = _services.DeviceModes.Runtime;
        var pointIds = GetVisibleEntries(SelectedTreeNode)
            .Select(point => point.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (runtime is null)
        {
            StatusMessage = "设备运行时未初始化";
            return;
        }
        if (pointIds.Length == 0)
        {
            StatusMessage = "当前范围没有可读取点位";
            return;
        }

        IsBusy = true;
        try
        {
            await runtime.ReadManyFreshAsync(pointIds, ct);
            RefreshCachedValues();
            StatusMessage = $"已读取当前范围 {pointIds.Length} 个点位";
        }
        catch (Exception ex)
        {
            StatusMessage = "读取当前范围失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 当前完整设备配置是否可以编辑项目级业务信号绑定。
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
    /// 当前完整设备配置是否可以编辑通道和设备。
    /// </summary>
    public bool CanManageDeviceConfiguration
        => CanEdit
           && _services.DeviceConfigurations is not null
           && IsGroupedConfiguration;

    /// <summary>
    /// 导入预览判定 S7 硬件点位时使用的设备只读快照。
    /// </summary>
    public IReadOnlyList<DeviceConfig.DeviceEntry> CurrentDeviceEntries
        => (_services.DeviceConfig.Devices ?? new List<DeviceConfig.DeviceEntry>())
            .Where(device => device is not null)
            .ToList();

    /// <summary>
    /// 供当前点位编辑上下文使用的设备选项。
    /// </summary>
    public IReadOnlyList<DevicePointDeviceChoice> DialogDeviceOptions
        => (_services.DeviceConfig.Devices ?? new List<DeviceConfig.DeviceEntry>())
            .Where(device => !string.IsNullOrWhiteSpace(device.Id)
                && !string.IsNullOrWhiteSpace(device.Code)
                && !string.IsNullOrWhiteSpace(device.DriverKey))
            .Select(CreateDeviceChoice)
            .ToList();

    /// <summary>
    /// 从当前树/表格上下文创建简化点位编辑器上下文。
    /// 设备和分组在此处锁定，弹窗不显示协议、内部 Id 或客户自定义编码字段。
    /// </summary>
    public DevicePointEditContext CreatePointDialogContext(
        bool isEdit,
        DevicePointRow? row = null)
    {
        var deviceId = row is not null
            ? ResolveDeviceId(row.Entry)
            : SelectedPointDialogDeviceId;
        var device = FindConfiguredDevice(deviceId);
        if (device is null)
            throw new Core.Common.DomainException("当前范围没有可用的已配置设备");

        var deviceChoice = CreateDeviceChoice(device);
        var groups = _allGroups
            .Where(group => string.Equals(group.DeviceId, deviceChoice.Id, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(group.Code, "DEFAULT", StringComparison.OrdinalIgnoreCase))
            .OrderBy(group => group.SortOrder)
            .ThenBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => new DevicePointGroupChoice(
                group.Id, group.DeviceId, group.Code, group.Name, group.SortOrder))
            .ToList();

        var currentGroupId = row?.Entry.GroupId ?? SelectedPointDialogGroupId;
        var currentGroup = groups.FirstOrDefault(group =>
            string.Equals(group.Id, currentGroupId, StringComparison.OrdinalIgnoreCase))
            ?? groups.FirstOrDefault(group =>
                string.Equals(group.Code, row?.Entry.GroupCode, StringComparison.OrdinalIgnoreCase));
        var groupLocked = isEdit || SelectedTreeNode?.Kind == DevicePointTreeNodeKind.Group;
        var channel = ResolveConfiguredChannel(
            device,
            (_services.DeviceConfig.Channels ?? new List<ChannelEntry>()).Where(item => item is not null).ToList());
        var descriptor = (_services.DriverDescriptors ?? Array.Empty<IDeviceDriverDescriptor>())
            .FirstOrDefault(item => string.Equals(
                item.DriverKey, device.DriverKey, StringComparison.OrdinalIgnoreCase));
        return new DevicePointEditContext(
            channel is null
                ? "未关联通道"
                : string.IsNullOrWhiteSpace(channel.Name) ? channel.Code : channel.Name.Trim(),
            deviceChoice,
            descriptor?.SupportedDataTypes ?? new HashSet<DevicePointDataType>(),
            groups,
            currentGroup,
            groupLocked)
        {
            PollIntervalText = device.PollIntervalMs > 0
                ? $"设备配置：{device.PollIntervalMs} ms"
                : _services.DeviceConfig.PollIntervalMs > 0
                    ? $"全局配置：{_services.DeviceConfig.PollIntervalMs} ms"
                    : "未配置"
        };
    }

    /// <summary>
    /// 从当前树节点推导点位弹窗的默认设备，不改变点位实际归属。
    /// </summary>
    public string? SelectedPointDialogDeviceId
        => SelectedTreeNode is { IsOrphan: false, OwnerDeviceId: { Length: > 0 } ownerDeviceId }
            ? ownerDeviceId
            : SelectedPoint is { } row ? ResolveDeviceId(row.Entry) : null;

    /// <summary>
    /// 从当前树节点推导点位弹窗的默认分组。
    /// </summary>
    public string? SelectedPointDialogGroupId
        => SelectedTreeNode?.Kind == DevicePointTreeNodeKind.Group
            ? SelectedTreeNode.Id
            : null;

    /// <summary>
    /// 当前点位表是否处于设备筛选状态。
    /// </summary>
    public string ScopeText => SelectedScopeText;

    /// <summary>
    /// 当前树节点对应的清单标题。
    /// </summary>
    public string SelectedScopeTitle => SelectedTreeNode?.Kind switch
    {
        DevicePointTreeNodeKind.Root => "全部点位",
        DevicePointTreeNodeKind.Channel => $"{SelectedTreeNode.DisplayText} 点位",
        DevicePointTreeNodeKind.Device => $"{SelectedTreeNode.DisplayText} 点位",
        DevicePointTreeNodeKind.Group => $"{SelectedTreeNode.DisplayText} 点位",
        _ => "设备点位"
    };

    /// <summary>
    /// 当前树节点对应的筛选说明。
    /// </summary>
    public string SelectedScopeText => SelectedTreeNode is null || SelectedTreeNode.Kind == DevicePointTreeNodeKind.Root
        ? "显示全部设备点位"
        : $"显示“{SelectedTreeNode.DisplayText}”下的点位";

    /// <summary>
    /// 右侧范围详情卡片使用的客户可读信息。它只读当前配置，不额外创建运行时连接。
    /// </summary>
    public string ScopeDetailsName => SelectedTreeNode?.DisplayText ?? "全部设备";

    public string ScopeDetailsTypeText => SelectedTreeNode?.Kind switch
    {
        DevicePointTreeNodeKind.Channel => "通信通道",
        DevicePointTreeNodeKind.Device => "设备",
        DevicePointTreeNodeKind.Group => "点位分组",
        _ => "配置范围"
    };

    public string ScopeDetailsStatusText
    {
        get
        {
            var node = SelectedTreeNode;
            if (node is null || node.Kind == DevicePointTreeNodeKind.Root)
                return _services.DeviceModes.Runtime is null ? "未加载" : "已生效";
            if (node.IsOrphan || node.IsVirtual)
                return "需修复";

            var device = ResolveSelectedDevice();
            if (device is not null)
                return device.DeviceMode == DeviceMode.Hardware && IsUnimplementedDriver(device.DriverKey)
                    ? "不可应用"
                    : GetRuntimeStatusText(device.Id);

            var channel = ResolveSelectedChannel();
            return channel is null ? "未读取" : channel.Enabled ? "已配置" : "已停用";
        }
    }

    public string ScopeDetailsStatusBackground => ScopeDetailsStatusText switch
    {
        "在线" or "已配置" or "已生效" => "#E8F7EF",
        "连接中" or "降级" => "#FFF4D8",
        "故障" or "离线" or "不可应用" or "需修复" => "#FFF0EE",
        _ => "#F2F4F7"
    };

    public string ScopeDetailsStatusForeground => ScopeDetailsStatusText switch
    {
        "在线" or "已配置" or "已生效" => "#147A45",
        "连接中" or "降级" => "#946200",
        "故障" or "离线" or "不可应用" or "需修复" => "#C9362B",
        _ => "#667085"
    };

    public string ScopeDetailsTransportText
    {
        get
        {
            var channel = ResolveSelectedChannel();
            if (channel is null)
                return SelectedTreeNode?.Kind == DevicePointTreeNodeKind.Root ? "按设备关联" : "未关联通道";
            return channel.TransportKind switch
            {
                ChannelTransportKind.Tcp => "TCP",
                ChannelTransportKind.Serial => "串口",
                ChannelTransportKind.Simulation => "仿真",
                _ => "未识别"
            };
        }
    }

    public string ScopeDetailsDriverText
    {
        get
        {
            var device = ResolveSelectedDevice();
            if (device is null)
                return SelectedTreeNode?.Kind == DevicePointTreeNodeKind.Channel ? "多个设备" : "—";
            var descriptor = (_services.DriverDescriptors ?? Array.Empty<IDeviceDriverDescriptor>())
                .FirstOrDefault(item => string.Equals(
                    item.DriverKey, device.DriverKey, StringComparison.OrdinalIgnoreCase));
            return string.IsNullOrWhiteSpace(descriptor?.DisplayName)
                ? string.IsNullOrWhiteSpace(device.DriverKey) ? "未登记" : device.DriverKey.Trim()
                : descriptor.DisplayName.Trim();
        }
    }

    public string ScopeDetailsModeText
    {
        get
        {
            var device = ResolveSelectedDevice();
            return device is null
                ? SelectedTreeNode?.Kind == DevicePointTreeNodeKind.Channel ? "按设备配置" : "—"
                : device.DeviceMode == DeviceMode.Simulation ? "仿真模式" : "硬件模式";
        }
    }

    public string ScopeDetailsPointCountText
        => $"{GetVisibleEntries(SelectedTreeNode).Count} 个点位";

    public string ScopeDetailsPollIntervalText
    {
        get
        {
            var device = ResolveSelectedDevice();
            if (device is not null)
                return device.PollIntervalMs > 0 ? $"{device.PollIntervalMs} ms" : "未配置";
            return SelectedTreeNode?.Kind == DevicePointTreeNodeKind.Channel ? "按设备配置" : "—";
        }
    }

    public string ScopeDetailsEndpointText
    {
        get
        {
            var channel = ResolveSelectedChannel();
            if (channel is null)
                return "—";
            return channel.TransportKind switch
            {
                ChannelTransportKind.Tcp when channel.Tcp is { } tcp
                    && !string.IsNullOrWhiteSpace(tcp.Host) => $"{tcp.Host}:{tcp.Port}",
                ChannelTransportKind.Serial when channel.Serial is { } serial
                    && !string.IsNullOrWhiteSpace(serial.PortName) => $"{serial.PortName} · {serial.BaudRate} bps",
                ChannelTransportKind.Simulation when channel.Simulation is { } simulation
                    && !string.IsNullOrWhiteSpace(simulation.InstanceKey) => simulation.InstanceKey,
                _ => "未配置"
            };
        }
    }

    public string ScopeDetailsDescription
    {
        get
        {
            var node = SelectedTreeNode;
            if (node is null || node.Kind == DevicePointTreeNodeKind.Root)
                return "从左侧选择通道、设备或分组，右侧清单会只显示当前范围的点位。";
            if (node.IsOrphan || node.IsVirtual)
                return "该范围暂不能新增点位。请先在设备编辑中修复通道关联，再继续配置。";
            var device = ResolveSelectedDevice();
            if (device is not null)
            {
                var model = device.Model?.Trim() ?? string.Empty;
                return string.IsNullOrWhiteSpace(model)
                    ? "点位继承设备的采集周期；保存后仍需经过配置应用安全检查。"
                    : model;
            }
            return node.Kind == DevicePointTreeNodeKind.Channel
                ? "通道下的设备共用该通信配置；点位参数在设备范围内继续编辑。"
                : "分组只负责组织点位，地址、类型和访问权限仍以点位配置为准。";
        }
    }

    /// <summary>
    /// 当前范围是否存在需要确认“优化的块访问”的 S7 硬件 DB 绝对地址。
    /// </summary>
    public bool HasS7OptimizedBlockAccessScopeNotice
        => GetS7OptimizedBlockAccessScopeNotice() is not null;

    /// <summary>
    /// 右侧范围详情中的短提示，详细步骤在点位编辑或导入预览中展开。
    /// </summary>
    public string S7OptimizedBlockAccessScopeNotice
        => GetS7OptimizedBlockAccessScopeNotice()?.ShortMessage ?? string.Empty;

    public bool CanAddChannel => CanManageDeviceConfiguration && SelectedTreeNode?.Kind == DevicePointTreeNodeKind.Root;
    public bool CanAddDevice => CanManageDeviceConfiguration
        && SelectedTreeNode?.Kind == DevicePointTreeNodeKind.Channel
        && SelectedTreeNode is not { IsVirtual: true }
        && HasConfiguredChannels;
    public bool CanAddGroup => IsGroupedConfiguration
        && CanEdit
        && SelectedTreeNode?.Kind == DevicePointTreeNodeKind.Device
        && SelectedTreeNode is not { IsVirtual: true, IsOrphan: true };
    public bool CanAddPoint => IsGroupedConfiguration
        && CanEdit
        && SelectedTreeNode?.Kind is DevicePointTreeNodeKind.Device or DevicePointTreeNodeKind.Group
        && SelectedTreeNode is not { IsVirtual: true, IsOrphan: true }
        && IsSelectedDeviceAvailable();
    public bool CanShowAddAction => CanAddChannel || CanAddDevice || CanAddGroup || CanAddPoint;
    public string AddActionText => CanAddChannel
        ? "新增通道"
        : CanAddDevice
            ? "新增设备"
            : CanAddGroup
                ? "新增分组"
                : "新增点位";
    public bool CanEditSelectedPoint => IsGroupedConfiguration && CanEdit && SelectedPoint is not null;
    public bool CanEditSelectedNode => IsGroupedConfiguration
        && CanEdit
        && SelectedTreeNode is not null
        && SelectedTreeNode.Kind != DevicePointTreeNodeKind.Root
        && (SelectedTreeNode.Kind == DevicePointTreeNodeKind.Device
            || !SelectedTreeNode.IsVirtual);
    public bool CanDeleteSelectedNode => IsGroupedConfiguration
        && CanEdit
        && (SelectedTreeNode?.Kind is DevicePointTreeNodeKind.Channel
            or DevicePointTreeNodeKind.Device
            or DevicePointTreeNodeKind.Group)
        && SelectedTreeNode is not { IsVirtual: true };
    public bool CanMoveSelectedPoint => IsGroupedConfiguration
        && CanEdit
        && SelectedPoint is not null
        && MoveGroupOptions.Count > 0;
    public bool CanApplyMoveSelectedPoint => CanMoveSelectedPoint
        && SelectedMoveGroup is not null
        && !string.Equals(SelectedPoint?.Entry.GroupId, SelectedMoveGroup.Id, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 当前生效配置中是否存在可供新建设备引用的通道。
    /// </summary>
    public bool HasConfiguredChannels
        => (_services.DeviceConfig.Channels ?? new List<ChannelEntry>())
            .Any(channel => channel is not null && !string.IsNullOrWhiteSpace(channel.Id));

    /// <summary>
    /// 没有解析到有效通道的设备数。孤立设备仍保留在树中，供用户修复关联。
    /// </summary>
    public int OrphanDeviceCount
    {
        get
        {
            var channels = (_services.DeviceConfig.Channels ?? new List<ChannelEntry>())
                .Where(channel => channel is not null)
                .ToList();
            return (_services.DeviceConfig.Devices ?? new List<DeviceConfig.DeviceEntry>())
                .Count(device => device is not null && ResolveConfiguredChannel(device, channels) is null);
        }
    }

    public bool HasOrphanDevices => OrphanDeviceCount > 0;

    public string OrphanHint => HasOrphanDevices
        ? $"有 {OrphanDeviceCount} 个设备未关联有效通道，请在“未关联通道（需修复）”下编辑设备。"
        : string.Empty;

    /// <summary>
    /// 返回通道当前引用的设备数量，供删除确认说明使用。
    /// </summary>
    public int GetChannelDeviceCount(string? channelId, string? channelCode)
    {
        var id = channelId?.Trim() ?? string.Empty;
        var code = channelCode?.Trim() ?? string.Empty;
        return (_services.DeviceConfig.Devices ?? new List<DeviceConfig.DeviceEntry>())
            .Count(device =>
            {
                var reference = device.ChannelId?.Trim() ?? string.Empty;
                return (!string.IsNullOrWhiteSpace(id)
                        && string.Equals(reference, id, StringComparison.OrdinalIgnoreCase))
                    || (!string.IsNullOrWhiteSpace(code)
                        && string.Equals(reference, code, StringComparison.OrdinalIgnoreCase));
            });
    }

    /// <summary>
    /// 返回当前树节点的右键动作。这里只返回当前上下文真正有意义的动作，
    /// 因此菜单不需要靠一组灰色按钮解释“什么时候可以点”。
    /// </summary>
    public IReadOnlyList<DevicePointTreeAction> GetTreeNodeActions(DevicePointTreeNodeViewModel node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (!IsGroupedConfiguration)
            return Array.Empty<DevicePointTreeAction>();

        var actions = new List<DevicePointTreeAction>();
        switch (node.Kind)
        {
            case DevicePointTreeNodeKind.Root:
                if (CanManageDeviceConfiguration)
                    actions.Add(new(DevicePointTreeActionKind.AddChannel, "新增通道"));
                actions.Add(new(DevicePointTreeActionKind.DiagnoseScope, "诊断全部范围", IsSeparatorBefore: actions.Count > 0));
                break;

            case DevicePointTreeNodeKind.Channel when !node.IsVirtual:
                if (CanManageDeviceConfiguration)
                {
                    actions.Add(new(DevicePointTreeActionKind.AddDevice, "新增设备"));
                    actions.Add(new(DevicePointTreeActionKind.EditChannel, "编辑通道", IsSeparatorBefore: true));
                    actions.Add(new(DevicePointTreeActionKind.DeleteChannel, "删除通道", IsDestructive: true));
                }
                actions.Add(new(DevicePointTreeActionKind.DiagnoseScope, "诊断通道范围", IsSeparatorBefore: true));
                break;

            case DevicePointTreeNodeKind.Device when node.IsOrphan:
                if (CanManageDeviceConfiguration)
                    actions.Add(new(DevicePointTreeActionKind.EditDevice, "编辑设备"));
                break;

            case DevicePointTreeNodeKind.Device:
                if (CanAddPoint)
                    actions.Add(new(DevicePointTreeActionKind.AddPoint, "新增点位"));
                if (CanManageDeviceConfiguration)
                    actions.Add(new(DevicePointTreeActionKind.AddGroup, "新增分组"));
                if (CanPastePoint)
                    actions.Add(new(DevicePointTreeActionKind.PastePoint, "粘贴点位", IsSeparatorBefore: true));
                if (CanManageDeviceConfiguration)
                {
                    actions.Add(new(DevicePointTreeActionKind.EditDevice, "编辑设备", IsSeparatorBefore: true));
                    actions.Add(new(DevicePointTreeActionKind.DeleteDevice, "删除设备", IsDestructive: true));
                }
                actions.Add(new(DevicePointTreeActionKind.DiagnoseScope, "诊断设备范围", IsSeparatorBefore: true));
                break;

            case DevicePointTreeNodeKind.Group when !node.IsVirtual:
                if (CanAddPoint)
                    actions.Add(new(DevicePointTreeActionKind.AddPoint, "新增点位"));
                if (CanPastePoint)
                    actions.Add(new(DevicePointTreeActionKind.PastePoint, "粘贴点位", IsSeparatorBefore: true));
                if (!string.Equals(node.Code, "DEFAULT", StringComparison.OrdinalIgnoreCase))
                {
                    actions.Add(new(DevicePointTreeActionKind.EditGroup, "编辑分组", IsSeparatorBefore: true));
                    actions.Add(new(DevicePointTreeActionKind.DeleteGroup, "删除分组", IsDestructive: true));
                }
                actions.Add(new(DevicePointTreeActionKind.DiagnoseScope, "诊断分组范围", IsSeparatorBefore: true));
                break;
        }

        return actions;
    }

    /// <summary>
    /// 返回点位清单行的业务动作，供调用方按行上下文执行。
    /// 视图层的点位详情右键菜单独立维护，不复用左侧设备树菜单。
    /// </summary>
    public IReadOnlyList<DevicePointTreeAction> GetPointRowActions(DevicePointRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!IsGroupedConfiguration || !CanEdit)
            return Array.Empty<DevicePointTreeAction>();

        var actions = new List<DevicePointTreeAction>();
        if (CanCopySelectedPoint)
            actions.Add(new(DevicePointTreeActionKind.CopyPoint, "复制"));
        if (CanCutSelectedPoint)
            actions.Add(new(DevicePointTreeActionKind.CutPoint, "剪切"));
        if (CanPastePoint)
            actions.Add(new(DevicePointTreeActionKind.PastePoint, "粘贴"));
        actions.Add(new(DevicePointTreeActionKind.EditPoint, "编辑点位", IsSeparatorBefore: actions.Count > 0));
        if (_services.DeviceModes.Runtime?.GetPoint(row.PointId) is not null)
            actions.Add(new(DevicePointTreeActionKind.DiagnosePoint, "查看状态", IsSeparatorBefore: true));
        var deviceId = ResolveDeviceId(row.Entry);
        if (_allGroups.Any(group =>
                string.Equals(group.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(group.Id, row.Entry.GroupId, StringComparison.OrdinalIgnoreCase)))
        {
            actions.Add(new(DevicePointTreeActionKind.MovePoint, "移动到其他分组"));
        }
        actions.Add(new(DevicePointTreeActionKind.DeletePoint, "删除点位", IsDestructive: true));
        return actions;
    }

    public int GetPointBindingCount(DevicePointRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var pointId = row.PointId?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(pointId)
            ? 0
            : (_services.DeviceModes.Runtime?.SignalBindings.Bindings
                ?? new Dictionary<string, string>())
                .Count(binding => string.Equals(binding.Value, pointId, StringComparison.OrdinalIgnoreCase));
    }

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

    [ObservableProperty]
    private DevicePointTemplateScopeChoice? _selectedTemplateScope;

    partial void OnSelectedDeviceFilterChanged(DevicePointDeviceFilter? value)
    {
        RefreshVisiblePoints();
        OnPropertyChanged(nameof(ScopeText));
    }

    partial void OnSelectedTreeNodeChanged(DevicePointTreeNodeViewModel? value)
    {
        RefreshTreeSelectionVisual();
        RefreshVisiblePoints();
        RefreshMoveGroups();
        RebuildTemplateScopeOptions();
        NotifyTreeActions();
        OnPropertyChanged(nameof(SelectedPointDialogDeviceId));
        OnPropertyChanged(nameof(SelectedPointDialogGroupId));
        NotifyScopeDetails();
        UpdateEventScope();
        TestSelectedConnectionCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedPointChanged(DevicePointRow? value)
    {
        RefreshMoveGroups();
        OnPropertyChanged(nameof(CanMoveSelectedPoint));
        OnPropertyChanged(nameof(CanApplyMoveSelectedPoint));
        OnPropertyChanged(nameof(CanEditSelectedPoint));
        OnPropertyChanged(nameof(CanCopySelectedPoint));
        OnPropertyChanged(nameof(CanCutSelectedPoint));
        OnPropertyChanged(nameof(CanPastePoint));
    }

    partial void OnTreeSearchTextChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value) && !_treeSearchWasActive)
        {
            _expandedNodeKeysBeforeSearch = CaptureExpandedNodeKeys();
            _selectedNodeKeyBeforeSearch = SelectedTreeNode?.NodeKey;
        }
        _treeSearchWasActive = !string.IsNullOrWhiteSpace(value);
        RebuildTree();
        NotifyTreeActions();
    }

    partial void OnPointSearchTextChanged(string value)
    {
        RefreshVisiblePoints();
        OnPropertyChanged(nameof(HasPointSearch));
        OnPropertyChanged(nameof(EmptyPointsTitle));
        OnPropertyChanged(nameof(EmptyPointsHint));
    }

    /// <summary>
    /// 清除右侧点位搜索，不改变左侧树的选择和展开状态。
    /// </summary>
    public void ClearPointSearch()
    {
        if (!string.IsNullOrWhiteSpace(PointSearchText))
            PointSearchText = string.Empty;
    }

    partial void OnSelectedMoveGroupChanged(DevicePointGroupChoice? value)
    {
        OnPropertyChanged(nameof(CanMoveSelectedPoint));
        OnPropertyChanged(nameof(CanApplyMoveSelectedPoint));
    }

    partial void OnSelectedTemplateScopeChanged(DevicePointTemplateScopeChoice? value)
    {
        OnPropertyChanged(nameof(TemplateScopeHint));
        OnPropertyChanged(nameof(CanDownloadTemplate));
    }

    /// <summary>
    /// 加载点位目录并刷新列表。
    /// </summary>
    public override async Task LoadAsync(CancellationToken ct = default)
    {
        var state = CaptureUiState();
        StatusMessage = string.Empty;
        IsBusy = true;
        OnPropertyChanged(nameof(CanDownloadTemplate));
        try
        {
            if (_services.DeviceConfigurations is not null)
            {
                var snapshot = await _services.DeviceConfigurations.LoadActiveAsync(ct);
                AdoptDeviceConfig(snapshot.Device);
                _services.DevicePoints.AdoptApplied(snapshot.Points);
                AdoptPointSnapshot(snapshot.Points, snapshot.Points.Groups, state, null);
            }
            else
            {
                _allEntries.Clear();
                _allEntries.AddRange(_services.DevicePoints.List());
                _allGroups.Clear();
                _allGroups.AddRange(_services.DevicePoints.ListGroups());
                RebuildDeviceFilters();
                RebuildTree();
                RefreshVisiblePoints();
                RefreshMoveGroups();
                RebuildTemplateScopeOptions();
                NotifyTreeActions();
            }

            OnPropertyChanged(nameof(HasPoints));
            OnPropertyChanged(nameof(ActiveRevisionText));
            RefreshCachedValues();
            NotifyTreeActions();
        }
        catch (Exception ex)
        {
            // 已生效配置不回滚；页面保留当前对象并明确提示刷新失败。
            StatusMessage = "刷新设备点位失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanDownloadTemplate));
            NotifyTreeActions();
        }
    }

    /// <summary>
    /// 保存新增的点位。
    /// </summary>
    public Task<OperationFeedback> CreateFromDialogAsync(
        DevicePointDialogResult result,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        return RunMutationAsync(async token =>
        {
            var entries = _allEntries.Select(ClonePoint).ToList();
            entries.Add(result.ToEntry());
            return await SaveEntriesCoreAsync(
                entries,
                "设备点位已新增",
                _allGroups,
                new UiRestoreIntent(SelectedTreeNode?.NodeKey, result.PointId),
                token,
                result.S7OptimizedBlockAccessConfirmed);
        }, ct);
    }

    /// <summary>
    /// 保存对选中点位的修改。
    /// </summary>
    public Task<OperationFeedback> UpdateFromDialogAsync(
        DevicePointRow row,
        DevicePointDialogResult result,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(result);
        return RunMutationAsync(async token =>
        {
            var entries = _allEntries.Select(ClonePoint).ToList();
            var index = entries.FindIndex(item =>
                ReferenceEquals(item, row.Entry)
                || (!string.IsNullOrWhiteSpace(item.Id)
                    && string.Equals(item.Id, row.Entry.Id, StringComparison.OrdinalIgnoreCase)));
            if (index < 0)
                throw new DomainException("选中的点位已不存在");
            entries[index] = result.ToEntry();
            return await SaveEntriesCoreAsync(
                entries,
                "设备点位已更新",
                _allGroups,
                new UiRestoreIntent(SelectedTreeNode?.NodeKey, result.PointId),
                token,
                result.S7OptimizedBlockAccessConfirmed);
        }, ct);
    }

    /// <summary>
    /// 导入点位文件并整体保存。保留该入口供非界面调用；界面入口会先显示预览再调用 ApplyImportedPointsAsync。
    /// </summary>
    public async Task ImportAsync(string filePath)
    {
        var plan = await CreateImportPlanAsync(filePath);
        if (plan is not null)
            await ApplyImportedPointsAsync(plan);
    }

    /// <summary>
    /// 只读取并校验导入文件，不改动当前点位目录。
    /// </summary>
    public async Task<DevicePointImportResult> ValidateImportAsync(
        string filePath,
        CancellationToken ct = default)
    {
        StatusMessage = string.Empty;
        IsBusy = true;
        try
        {
            var result = await _services.DevicePointImporter.ImportAsync(filePath, ct);
            if (result.IsValid && !IsGroupedConfiguration)
            {
                result = new DevicePointImportResult(
                    Array.Empty<PointsConfig.PointEntry>(),
                    new[] { new DevicePointImportIssue(0, "当前设备点位配置不是完整配置版本，不能导入；请先生成当前生效配置") });
            }
            else if (result.IsValid)
            {
                var prepared = PrepareImportedPoints(result.Points);
                result = ResolveImportedGroups(new DevicePointImportResult(
                    prepared,
                    Array.Empty<DevicePointImportIssue>(),
                    result.RowsWithNumbers));
                if (result.IsValid)
                    result = ValidateImportedDriverAddresses(result);
            }
            StatusMessage = result.IsValid
                ? $"文件校验通过：共 {result.Points.Count} 个设备点位，请检查导入预览"
                : FormatImportIssues(result);
            return result;
        }
        catch (OperationCanceledException)
        {
            return new DevicePointImportResult(
                Array.Empty<PointsConfig.PointEntry>(),
                new[] { new DevicePointImportIssue(0, "导入校验已取消") });
        }
        catch (Exception ex)
        {
            var result = new DevicePointImportResult(
                Array.Empty<PointsConfig.PointEntry>(),
                new[] { new DevicePointImportIssue(0, ex.Message) });
            StatusMessage = FormatImportIssues(result);
            return result;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 创建当前页面选中的模板范围，供界面导出和行为测试共用。
    /// </summary>
    public DevicePointTemplateExportRequest? CreateTemplateExportRequest()
    {
        if (!IsGroupedConfiguration)
            return null;

        var devices = GetCurrentTemplateDevices();
        var targets = devices.Select(CreateTemplateTarget).ToList();
        if (targets.Count == 0)
            return null;

        var targetIds = targets
            .Select(target => target.DeviceId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var groupCodes = _allGroups
            .Where(group => targetIds.Contains(group.DeviceId))
            .Select(group => group.Code?.Trim() ?? string.Empty)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var defaultGroupCode = SelectedTreeNode?.Kind == DevicePointTreeNodeKind.Group
            && targets.Count == 1
            && !string.Equals(SelectedTreeNode.Code, "DEFAULT", StringComparison.OrdinalIgnoreCase)
            ? SelectedTreeNode.Code
            : null;

        return new DevicePointTemplateExportRequest(
            $"当前范围：{GetCurrentTemplateScopeName()}",
            $"按左侧当前选择导出，共包含 {targets.Count} 个设备；点位行使用设备和分组身份进行校验。",
            targets,
            groupCodes,
            defaultGroupCode);
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
            _actor,
            _services.DeviceConnectionTester);
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
        var deviceId = SelectedTreeNode is { OwnerDeviceId: { Length: > 0 } ownerDeviceId }
            ? ownerDeviceId
            : SelectedPoint is { } point ? ResolveDeviceId(point.Entry) : string.Empty;
        if (string.IsNullOrWhiteSpace(deviceId))
            throw new Core.Common.DomainException("请先选择设备节点");
        var current = isEdit && SelectedTreeNode?.Kind == DevicePointTreeNodeKind.Group
            ? _allGroups.FirstOrDefault(group => string.Equals(group.Id, SelectedTreeNode.Id, StringComparison.OrdinalIgnoreCase))
            : null;
        var deviceName = FindConfiguredDevice(deviceId)?.Name;
        var nextSortOrder = _allGroups
            .Where(group => string.Equals(group.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
            .Select(group => group.SortOrder)
            .DefaultIfEmpty(0)
            .Max() + 10;
        return new PointGroupDialogViewModel(
            isEdit,
            current,
            deviceId,
            defaultCode: null,
            defaultSortOrder: nextSortOrder,
            deviceName: deviceName);
    }

    /// <summary>
    /// 应用分组新增或修改候选。
    /// </summary>
    public Task<OperationFeedback> ApplyPointGroupAsync(
        PointGroupDialogResult result,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (string.Equals(result.Code, "DEFAULT", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(SetFeedback(false, "系统默认分组不能编辑或新增"));

        return RunMutationAsync(async token =>
        {
            var groups = _allGroups.Select(CloneGroup).ToList();
            var index = groups.FindIndex(group =>
                string.Equals(group.Id, result.GroupId, StringComparison.OrdinalIgnoreCase));
            var candidate = result.ToEntry();
            var duplicate = groups.FirstOrDefault(group =>
                !string.Equals(group.Id, candidate.Id, StringComparison.OrdinalIgnoreCase)
                && string.Equals(group.DeviceId, candidate.DeviceId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(group.Name?.Trim(), candidate.Name?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (duplicate is not null)
                throw new DomainException($"设备内已存在同名分组：{duplicate.Name}");
            if (index >= 0)
                groups[index] = candidate;
            else
                groups.Add(candidate);

            var nodeKey = DevicePointTreeNodeViewModel.CreateNodeKey(
                DevicePointTreeNodeKind.Group,
                candidate.Id,
                candidate.DeviceId);
            return await SaveEntriesCoreAsync(
                _allEntries,
                index >= 0 ? "点位分组已更新" : "点位分组已新增",
                groups,
                new UiRestoreIntent(nodeKey, SelectedPoint?.PointId),
                token);
        }, ct);
    }

    /// <summary>
    /// 删除当前选中的树节点。通道和设备沿用同一个候选编辑器，点位分组使用引用安全校验。
    /// </summary>
    public Task<OperationFeedback> DeleteSelectedTreeNodeAsync(CancellationToken ct = default)
    {
        if (SelectedTreeNode is null)
            return Task.FromResult(SetFeedback(false, "请先选择要删除的节点"));
        if (SelectedTreeNode.Kind != DevicePointTreeNodeKind.Group)
            return Task.FromResult(SetFeedback(false, "请在通道/设备编辑器中选择该节点并确认删除；系统会先检查引用关系"));

        var node = SelectedTreeNode;
        if (string.Equals(node.Code, "DEFAULT", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(SetFeedback(false, "系统默认分组不能删除"));
        if (_services.DeviceConfigurations is null)
            return Task.FromResult(SetFeedback(false, "完整设备配置服务未连接，不能删除点位分组"));

        return RunMutationAsync(async token =>
        {
            var state = CaptureUiState();
            var deviceId = node.OwnerDeviceId;
            var defaultGroup = _allGroups.FirstOrDefault(group =>
                string.Equals(group.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(group.Code, "DEFAULT", StringComparison.OrdinalIgnoreCase));
            var result = await _services.DeviceConfigurations.DeletePointGroupAsync(_actor, node.Id, token);
            if (!result.Ok)
                throw new DomainException(result.Error ?? "点位分组删除失败");
            var snapshot = await ResolveAppliedSnapshotAsync(result);
            _services.DevicePoints.AdoptApplied(snapshot.Points);
            var fallbackKey = defaultGroup is null
                ? DevicePointTreeNodeViewModel.CreateNodeKey(DevicePointTreeNodeKind.Device, deviceId)
                : DevicePointTreeNodeViewModel.CreateNodeKey(DevicePointTreeNodeKind.Group, defaultGroup.Id, deviceId);
            AdoptPointSnapshot(
                snapshot.Points,
                snapshot.Points.Groups,
                state,
                new UiRestoreIntent(fallbackKey, state.SelectedPointId));
            OnPropertyChanged(nameof(ActiveRevisionText));
            return "点位分组已删除，组内点位已迁移到默认分组并应用";
        }, ct);
    }

    /// <summary>
    /// 把选中点位移动到已存在的同设备分组，只改变 GroupId。
    /// </summary>
    public Task<OperationFeedback> MoveSelectedPointAsync(CancellationToken ct = default)
    {
        if (SelectedMoveGroup is null)
            return Task.FromResult(SetFeedback(false, "请选择点位和目标分组"));
        return MoveSelectedPointToGroupAsync(SelectedMoveGroup, ct);
    }

    /// <summary>
    /// 从树节点菜单把点位直接移动到指定分组，避免再显示无关的点位状态按钮。
    /// </summary>
    public Task<OperationFeedback> MoveSelectedPointToGroupAsync(
        DevicePointGroupChoice targetGroup,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(targetGroup);
        if (SelectedPoint is null)
            return Task.FromResult(SetFeedback(false, "请选择点位和目标分组"));
        if (string.Equals(SelectedPoint.Entry.GroupId, targetGroup.Id, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(SetFeedback(false, "目标分组与当前分组相同，无需调整"));
        if (!string.Equals(ResolveDeviceId(SelectedPoint.Entry), targetGroup.DeviceId, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(SetFeedback(false, "目标分组必须属于当前点位的设备"));

        var selectedPointId = SelectedPoint.PointId;
        return RunMutationAsync(async token =>
        {
            var entries = _allEntries.Select(ClonePoint).ToList();
            var index = entries.FindIndex(point =>
                string.Equals(point.Id, selectedPointId, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                throw new DomainException("选中的点位已不存在");
            entries[index].GroupId = targetGroup.Id;
            entries[index].GroupCode = targetGroup.Code;
            var targetNodeKey = DevicePointTreeNodeViewModel.CreateNodeKey(
                DevicePointTreeNodeKind.Group,
                targetGroup.Id,
                targetGroup.DeviceId);
            return await SaveEntriesCoreAsync(
                entries,
                "点位分组已调整",
                _allGroups,
                new UiRestoreIntent(targetNodeKey, selectedPointId),
                token);
        }, ct);
    }

    /// <summary>
    /// 设备/通道配置应用成功后更新共享配置对象和点位页。
    /// </summary>
    public Task<OperationFeedback> ApplyDeviceConfigurationResultAsync(
        DeviceConfigurationApplyResult result,
        CancellationToken ct = default)
    {
        if (!result.Ok)
            return Task.FromResult(SetFeedback(false, result.Error ?? "设备配置应用失败"));
        return RunMutationAsync(async token =>
        {
            var state = CaptureUiState();
            var snapshot = await ResolveAppliedSnapshotAsync(result);
            AdoptDeviceConfig(snapshot.Device);
            _services.DevicePoints.AdoptApplied(snapshot.Points);
            AdoptPointSnapshot(snapshot.Points, snapshot.Points.Groups, state, null);
            OnPropertyChanged(nameof(ActiveRevisionText));
            return "设备与通道配置已应用，运行状态已刷新";
        }, ct);
    }

    /// <summary>
    /// 信号绑定应用成功后同步页面目录并显示新的生效版本。
    /// </summary>
    public Task<OperationFeedback> ApplySignalBindingsResultAsync(
        DeviceConfigurationApplyResult result,
        CancellationToken ct = default)
    {
        if (!result.Ok)
            return Task.FromResult(SetFeedback(false, result.Error ?? "信号绑定应用失败"));
        return RunMutationAsync(async token =>
        {
            var state = CaptureUiState();
            var snapshot = await ResolveAppliedSnapshotAsync(result);
            _services.DevicePoints.AdoptApplied(snapshot.Points);
            AdoptPointSnapshot(snapshot.Points, snapshot.Points.Groups, state, null);
            OnPropertyChanged(nameof(ActiveRevisionText));
            return "信号绑定已应用，运行状态已刷新";
        }, ct);
    }

    private async Task<DeviceConfigurationSnapshot> ResolveAppliedSnapshotAsync(
        DeviceConfigurationApplyResult result)
    {
        if (result.Snapshot is not null)
            return result.Snapshot;
        if (_services.DeviceConfigurations is null)
            throw new Core.Common.DomainException("当前设备配置服务未连接，无法读取已应用快照");
        return await _services.DeviceConfigurations.LoadActiveAsync();
    }

    /// <summary>
    /// 删除当前选中的点位。
    /// </summary>
    [RelayCommand]
    public Task<OperationFeedback> DeletePointAsync(CancellationToken ct = default)
    {
        if (SelectedPoint is null)
            return Task.FromResult(SetFeedback(false, "请先选择设备点位"));
        var selectedPoint = SelectedPoint;
        var pointId = selectedPoint.Entry.Id?.Trim() ?? string.Empty;
        var bindingCount = GetPointBindingCount(selectedPoint);
        if (bindingCount > 0)
            return Task.FromResult(SetFeedback(
                false,
                $"点位“{selectedPoint.Code}”仍被 {bindingCount} 个信号绑定引用，请先解除引用；系统不会静默删除。"));
        var fallbackPointId = GetDeleteFallbackPointId(selectedPoint);
        var selectedRowKey = GetPointRowKey(selectedPoint.Entry);
        return RunMutationAsync(async token =>
        {
            var entries = _allEntries
                .Where(entry => !string.Equals(
                    GetPointRowKey(entry), selectedRowKey, StringComparison.OrdinalIgnoreCase))
                .Select(ClonePoint)
                .ToList();
            return await SaveEntriesCoreAsync(
                entries,
                "设备点位已删除",
                _allGroups,
                new UiRestoreIntent(SelectedTreeNode?.NodeKey, fallbackPointId),
                token);
        }, ct);
    }

    /// <summary>
    /// 保存点位列表并重载设备运行时。
    /// </summary>
    private async Task<string> SaveEntriesCoreAsync(
        IReadOnlyList<PointsConfig.PointEntry> entries,
        string successText,
        IReadOnlyList<PointsConfig.PointGroupEntry>? groups = null,
        UiRestoreIntent? restoreIntent = null,
        CancellationToken ct = default,
        bool s7OptimizedBlockAccessConfirmed = false)
    {
        StatusMessage = string.Empty;
        var state = CaptureUiState();
        if (await _services.RecordRepository.GetActiveRunningRecordAsync() is not null)
            throw new DomainException("存在活动试验，禁止修改设备点位；请先结束试验");
        var candidateEntries = entries.Select(ClonePoint).ToList();
        var candidateGroups = (groups ?? _allGroups).Select(CloneGroup).ToList();
        if (!IsGroupedConfiguration || _services.DeviceConfigurations is null)
            throw new DomainException("当前设备点位配置不是完整配置版本，不能保存");
        var applied = await _services.DeviceConfigurations.ApplyPointsAsync(
            _actor,
            candidateEntries,
            candidateGroups,
            ct,
            s7OptimizedBlockAccessConfirmed);
        if (!applied.Ok)
            throw new DomainException(applied.Error ?? "设备点位配置应用失败");
        var snapshot = await ResolveAppliedSnapshotAsync(applied);
        _services.DevicePoints.AdoptApplied(snapshot.Points);
        AdoptPointSnapshot(snapshot.Points, snapshot.Points.Groups, state, restoreIntent);
        OnPropertyChanged(nameof(ActiveRevisionText));
        return $"{successText}，完整设备配置已应用";
    }

    /// <summary>
    /// 捕获树、清单筛选和选择状态，供配置应用后恢复。
    /// </summary>
    public DevicePointUiState CaptureUiState()
        => new(
            SelectedTreeNode?.NodeKey,
            CaptureExpandedNodeKeys(),
            SelectedPoint?.PointId,
            Points.FirstOrDefault()?.PointId,
            TreeSearchText,
            PointSearchText);

    private void AdoptPointSnapshot(
        PointsConfig points,
        IReadOnlyList<PointsConfig.PointGroupEntry> groups,
        DevicePointUiState state,
        UiRestoreIntent? restoreIntent)
    {
        _allEntries.Clear();
        _allEntries.AddRange(points.Points.Select(ClonePoint));
        _allGroups.Clear();
        _allGroups.AddRange(groups.Select(CloneGroup));
        _expandedNodeKeysBeforeSearch = state.ExpandedNodeKeys
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        RebuildDeviceFilters();
        RebuildTree();

        var wantedNodeKey = restoreIntent?.NodeKey ?? state.SelectedNodeKey;
        var root = TreeNodes.FirstOrDefault();
        var wantedNode = root is null ? null : FindVisibleTreeNode(root, wantedNodeKey);
        if (wantedNode is not null && !ReferenceEquals(SelectedTreeNode, wantedNode))
            SelectedTreeNode = wantedNode;
        RefreshVisiblePoints();

        var wantedPointId = restoreIntent?.PointId ?? state.SelectedPointId;
        SelectedPoint = string.IsNullOrWhiteSpace(wantedPointId)
            ? null
            : Points.FirstOrDefault(row =>
                string.Equals(row.PointId, wantedPointId, StringComparison.OrdinalIgnoreCase));
        RefreshMoveGroups();
        NotifyTreeActions();
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

            if (existingIndex < 0 && !string.IsNullOrWhiteSpace(incoming.DeviceCode))
            {
                var incomingTag = GetPointTag(incoming);
                existingIndex = merged.FindIndex(point =>
                    string.Equals(ResolveDeviceCode(point), incoming.DeviceCode, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(GetPointTag(point), incomingTag, StringComparison.OrdinalIgnoreCase));
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

        var devices = SelectedTemplateScope is not null
            ? GetTemplateScopeDevices(SelectedTemplateScope)
            : DialogDeviceOptions
                .Select(choice => (_services.DeviceConfig.Devices ?? new List<DeviceConfig.DeviceEntry>())
                    .FirstOrDefault(device => string.Equals(device.Id, choice.Id, StringComparison.OrdinalIgnoreCase)))
                .Where(device => device is not null)
                .Cast<DeviceConfig.DeviceEntry>()
                .ToList();
        if (devices.Count != 1)
            throw new Core.Common.DomainException("当前模板未包含设备前缀；请选择单个设备范围下载的模板。多设备模板请填写“设备编码/分组.点位”。");
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
        foreach (var parsedRow in result.RowsWithNumbers)
        {
            var point = parsedRow.Entry;
            if (!devices.TryGetValue(point.DeviceCode.Trim(), out var device))
            {
                issues.Add(new DevicePointImportIssue(parsedRow.RowNumber,
                    $"点位 {point.Code} 引用的设备不存在：{point.DeviceCode}"));
                continue;
            }

            var groupCode = string.IsNullOrWhiteSpace(point.GroupCode) ? "DEFAULT" : point.GroupCode.Trim();
            var group = _allGroups.FirstOrDefault(item =>
                string.Equals(item.DeviceId, device.Id, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.Code, groupCode, StringComparison.OrdinalIgnoreCase));
            if (group is null)
            {
                issues.Add(new DevicePointImportIssue(parsedRow.RowNumber,
                    $"点位 {point.Code} 引用的分组不存在：设备 {device.Code} / {groupCode}"));
                continue;
            }

            point.DeviceId = device.Id;
            point.DeviceCode = device.Code;
            point.GroupId = group.Id;
            point.GroupCode = group.Code;
        }
        return new DevicePointImportResult(result.Points, issues, result.RowsWithNumbers);
    }

    /// <summary>
    /// 在设备和分组已经解析后按设备驱动校验原始地址。
    /// </summary>
    private DevicePointImportResult ValidateImportedDriverAddresses(DevicePointImportResult result)
    {
        var issues = DevicePointImportValidator.Validate(
            _services.DeviceConfig,
            result.Points,
            _services.DriverDescriptors);
        if (issues.Count == 0)
            return result;

        return new DevicePointImportResult(
            result.Points,
            result.Issues.Concat(issues.Select(issue => new DevicePointImportIssue(0, issue.ToString()))).ToList(),
            result.RowsWithNumbers);
    }

    private sealed record DesiredTreeNode(
        DevicePointTreeNodeKind Kind,
        string Id,
        string? ParentId,
        string Code,
        string Name,
        string SummaryText,
        string? OwnerDeviceId,
        bool IsVirtual,
        bool IsOrphan,
        IReadOnlyList<DesiredTreeNode> Children,
        string? NodeKey = null);

    private void RebuildTree()
    {
        var selectedKey = _selectedNodeKeyBeforeSearch
            ?? SelectedTreeNode?.NodeKey;
        var root = ReconcileNodeCollection(TreeNodes, new[] { BuildDesiredTree() })[0];
        if (!string.IsNullOrWhiteSpace(TreeSearchText))
            ApplyTreeFilter(root, TreeSearchText.Trim());

        var selected = FindVisibleTreeNode(root, selectedKey) ?? root;
        if (!ReferenceEquals(SelectedTreeNode, selected))
            SelectedTreeNode = selected;
        RefreshTreeSelectionVisual();

        if (string.IsNullOrWhiteSpace(TreeSearchText))
        {
            if (_expandedNodeKeysBeforeSearch.Count > 0)
                RestoreExpandedNodeKeys(_expandedNodeKeysBeforeSearch);
            _expandedNodeKeysBeforeSearch = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _selectedNodeKeyBeforeSearch = null;
        }
        NotifyTreeActions();
    }

    private DesiredTreeNode BuildDesiredTree()
    {
        var devices = (_services.DeviceConfig.Devices ?? new List<DeviceConfig.DeviceEntry>())
            .Where(device => device is not null)
            .GroupBy(GetDeviceIdentity, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        var channels = (_services.DeviceConfig.Channels ?? new List<ChannelEntry>())
            .Where(channel => channel is not null)
            .GroupBy(GetChannelIdentity, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        var channelNodes = channels
            .OrderBy(channel => string.IsNullOrWhiteSpace(channel.Name) ? channel.Code : channel.Name,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(channel => channel.Code, StringComparer.OrdinalIgnoreCase)
            .Select(channel =>
            {
                var channelId = GetChannelIdentity(channel);
                var channelDevices = devices
                    .Where(device => ResolveConfiguredChannel(device, channels) is { } resolved
                        && string.Equals(GetChannelIdentity(resolved), channelId, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(device => string.IsNullOrWhiteSpace(device.Name) ? device.Code : device.Name,
                        StringComparer.OrdinalIgnoreCase)
                    .ThenBy(device => device.Code, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                return BuildDesiredChannel(channel, "root", channelDevices);
            })
            .ToList();

        var orphanDevices = devices
            .Where(device => ResolveConfiguredChannel(device, channels) is null)
            .OrderBy(device => string.IsNullOrWhiteSpace(device.Name) ? device.Code : device.Name,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(device => device.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (orphanDevices.Count > 0)
        {
            var orphanChildren = orphanDevices
                .Select(device => BuildDesiredDevice(device, "orphan-devices", isOrphan: true))
                .ToList();
            channelNodes.Add(new DesiredTreeNode(
                DevicePointTreeNodeKind.Channel,
                "orphan-devices",
                "root",
                "ORPHAN_DEVICES",
                "未关联通道（需修复）",
                $"{orphanDevices.Count} 个设备需要修复通道关联",
                null,
                true,
                true,
                orphanChildren,
                "virtual:orphan-devices"));
        }

        return new DesiredTreeNode(
            DevicePointTreeNodeKind.Root,
            "root",
            null,
            "root",
            "全部设备",
            $"{devices.Count} 个设备 · {_allEntries.Count} 个点位",
            null,
            false,
            false,
            channelNodes);
    }

    private DesiredTreeNode BuildDesiredChannel(
        ChannelEntry channel,
        string parentId,
        IReadOnlyList<DeviceConfig.DeviceEntry> channelDevices)
    {
        var channelCode = string.IsNullOrWhiteSpace(channel.Code) ? "未命名通道" : channel.Code.Trim();
        var channelId = GetChannelIdentity(channel);
        return new DesiredTreeNode(
            DevicePointTreeNodeKind.Channel,
            channelId,
            parentId,
            channelCode,
            string.IsNullOrWhiteSpace(channel.Name) ? channelCode : channel.Name.Trim(),
            BuildChannelSummary(channel, channelDevices, isVirtual: false),
            null,
            false,
            false,
            channelDevices.Select(device => BuildDesiredDevice(device, channelId, isOrphan: false)).ToList());
    }

    private DesiredTreeNode BuildDesiredDevice(
        DeviceConfig.DeviceEntry device,
        string parentId,
        bool isOrphan)
    {
        var deviceCode = string.IsNullOrWhiteSpace(device.Code) ? device.Name : device.Code.Trim();
        var deviceId = GetDeviceIdentity(device);
        var devicePoints = _allEntries
            .Where(point => string.Equals(ResolveDeviceId(point), deviceId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var groups = _allGroups
            .Where(group => string.Equals(group.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(group.Code, "DEFAULT", StringComparison.OrdinalIgnoreCase))
            .GroupBy(group => group.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        var groupNodes = groups
            .OrderBy(group => group.SortOrder)
            .ThenBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Code, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var count = devicePoints.Count(point => IsPointInGroup(point, group));
                return new DesiredTreeNode(
                    DevicePointTreeNodeKind.Group,
                    string.IsNullOrWhiteSpace(group.Id) ? group.Code : group.Id.Trim(),
                    deviceId,
                    string.IsNullOrWhiteSpace(group.Code) ? "DEFAULT" : group.Code.Trim(),
                    string.IsNullOrWhiteSpace(group.Name) ? group.Code.Trim() : group.Name.Trim(),
                    $"{count} 个点位",
                    deviceId,
                    false,
                    isOrphan,
                    Array.Empty<DesiredTreeNode>());
            })
            .ToList();

        return new DesiredTreeNode(
            DevicePointTreeNodeKind.Device,
            deviceId,
            parentId,
            string.IsNullOrWhiteSpace(deviceCode) ? "未命名设备" : deviceCode,
            string.IsNullOrWhiteSpace(device.Name) ? deviceCode : device.Name.Trim(),
            BuildDeviceSummary(device, devicePoints.Count),
            deviceId,
            false,
            isOrphan,
            groupNodes);
    }

    private List<DevicePointTreeNodeViewModel> ReconcileNodeCollection(
        ObservableCollection<DevicePointTreeNodeViewModel> actual,
        IReadOnlyList<DesiredTreeNode> desired)
    {
        var result = new List<DevicePointTreeNodeViewModel>(desired.Count);
        for (var index = 0; index < desired.Count; index++)
        {
            var specification = desired[index];
            var node = GetOrCreateTreeNode(specification);
            if (index < actual.Count)
            {
                if (!ReferenceEquals(actual[index], node))
                {
                    var existingIndex = actual.IndexOf(node);
                    if (existingIndex >= 0)
                        actual.Move(existingIndex, index);
                    else
                        actual.Insert(index, node);
                }
            }
            else
            {
                actual.Add(node);
            }
            ReconcileNodeCollection(node.Children, specification.Children);
            result.Add(node);
        }
        while (actual.Count > desired.Count)
            actual.RemoveAt(actual.Count - 1);
        return result;
    }

    private DevicePointTreeNodeViewModel GetOrCreateTreeNode(DesiredTreeNode specification)
    {
        if (!_treeNodeCache.TryGetValue(
                specification.NodeKey
                    ?? DevicePointTreeNodeViewModel.CreateNodeKey(
                        specification.Kind, specification.Id, specification.OwnerDeviceId),
                out var node))
        {
            node = new DevicePointTreeNodeViewModel(
                specification.Kind,
                specification.Id,
                specification.ParentId,
                specification.Code,
                specification.Name,
                specification.SummaryText,
                specification.OwnerDeviceId,
                specification.IsVirtual,
                specification.IsOrphan,
                specification.NodeKey);
            node.IsExpanded = specification.Kind != DevicePointTreeNodeKind.Group;
            _treeNodeCache[node.NodeKey] = node;
        }
        else
        {
            node.Update(
                specification.ParentId,
                specification.Code,
                specification.Name,
                specification.SummaryText,
                specification.OwnerDeviceId,
                specification.IsVirtual,
                specification.IsOrphan);
        }
        return node;
    }

    private static bool IsPointInGroup(
        PointsConfig.PointEntry point,
        PointsConfig.PointGroupEntry group)
        => string.Equals(point.GroupId, group.Id, StringComparison.OrdinalIgnoreCase)
           || (string.Equals(group.Code, "DEFAULT", StringComparison.OrdinalIgnoreCase)
               && (string.IsNullOrWhiteSpace(point.GroupId)
                   || string.Equals(point.GroupCode, "DEFAULT", StringComparison.OrdinalIgnoreCase)));

    private static ChannelEntry? ResolveConfiguredChannel(
        DeviceConfig.DeviceEntry device,
        IReadOnlyList<ChannelEntry> channels)
    {
        var reference = device.ChannelId?.Trim() ?? string.Empty;
        var match = channels.FirstOrDefault(channel =>
            string.Equals(channel.Id, reference, StringComparison.OrdinalIgnoreCase)
            || string.Equals(channel.Code, reference, StringComparison.OrdinalIgnoreCase));
        // 空或无效的 ChannelId 必须进入“未关联通道”修复范围，不能猜测唯一通道。
        return match;
    }

    private static string GetChannelIdentity(ChannelEntry channel)
        => !string.IsNullOrWhiteSpace(channel.Id) ? channel.Id.Trim() : channel.Code.Trim();

    private string BuildChannelSummary(
        ChannelEntry channel,
        IReadOnlyList<DeviceConfig.DeviceEntry> devices,
        bool isVirtual)
    {
        if (isVirtual)
            return "未关联 · 请在设备编辑中选择通信通道";
        var disabled = !channel.Enabled;
        var unimplemented = devices.Any(device =>
            device.DeviceMode == DeviceMode.Hardware && IsUnimplementedDriver(device.DriverKey));
        var state = disabled ? "已停用" : unimplemented ? "包含硬件未实现驱动" : "已配置";
        var transport = channel.TransportKind switch
        {
            ChannelTransportKind.Tcp => "TCP",
            ChannelTransportKind.Serial => "串口",
            ChannelTransportKind.Simulation => "仿真",
            _ => "传输方式未识别"
        };
        var endpoint = channel.TransportKind switch
        {
            ChannelTransportKind.Tcp when channel.Tcp is { } tcp
                && !string.IsNullOrWhiteSpace(tcp.Host) => $"{tcp.Host}:{tcp.Port}",
            ChannelTransportKind.Serial when channel.Serial is { } serial
                && !string.IsNullOrWhiteSpace(serial.PortName) => $"{serial.PortName} · {serial.BaudRate} bps",
            ChannelTransportKind.Simulation when channel.Simulation is { } simulation
                && !string.IsNullOrWhiteSpace(simulation.InstanceKey) => simulation.InstanceKey,
            _ => string.Empty
        };
        var sharing = devices.Count > 1 ? "共享通道" : "独立通道";
        var endpointText = string.IsNullOrWhiteSpace(endpoint) ? string.Empty : $" · {endpoint}";
        return $"{state} · {transport}{endpointText} · {sharing} · {devices.Count} 个设备";
    }

    private string BuildDeviceSummary(DeviceConfig.DeviceEntry device, int pointCount)
    {
        var mode = device.DeviceMode == DeviceMode.Simulation ? "仿真模式" : "硬件模式";
        var state = device.DeviceMode == DeviceMode.Hardware && IsUnimplementedDriver(device.DriverKey)
            ? "硬件驱动未实现 · 不可应用"
            : GetRuntimeStatusText(device.Id);
        var driverName = _services.DriverDescriptors?
            .FirstOrDefault(descriptor => string.Equals(
                descriptor.DriverKey,
                device.DriverKey,
                StringComparison.OrdinalIgnoreCase))?.DisplayName;
        var driverText = string.IsNullOrWhiteSpace(driverName)
            ? device.DriverKey
            : driverName;
        var driverSummary = string.IsNullOrWhiteSpace(driverText)
            ? string.Empty
            : $" · 驱动 {driverText.Trim()}";
        return $"{mode} · {state}{driverSummary} · {pointCount} 个点位";
    }

    private bool IsUnimplementedDriver(string? driverKey)
        => !string.IsNullOrWhiteSpace(driverKey)
               && _services.DriverDescriptors?.FirstOrDefault(descriptor =>
               string.Equals(descriptor.DriverKey, driverKey, StringComparison.OrdinalIgnoreCase)) is { IsImplemented: false };

    private bool IsSelectedDeviceAvailable()
    {
        var device = FindConfiguredDevice(SelectedTreeNode?.OwnerDeviceId);
        return device is not null
            && (device.DeviceMode == DeviceMode.Simulation || !IsUnimplementedDriver(device.DriverKey));
    }

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

    private static bool ApplyTreeFilter(DevicePointTreeNodeViewModel node, string query)
    {
        var keptChild = false;
        foreach (var child in node.Children.ToList())
        {
            if (ApplyTreeFilter(child, query))
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

    private HashSet<string> CaptureExpandedNodeKeys()
        => _treeNodeCache.Values
            .Where(node => node.IsExpanded)
            .Select(node => node.NodeKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private void RestoreExpandedNodeKeys(IReadOnlySet<string> expandedKeys)
    {
        foreach (var node in _treeNodeCache.Values)
            node.IsExpanded = string.Equals(node.NodeKey, "root", StringComparison.OrdinalIgnoreCase)
                || expandedKeys.Contains(node.NodeKey);
    }

    private static DevicePointTreeNodeViewModel? FindVisibleTreeNode(
        DevicePointTreeNodeViewModel node,
        string? nodeKey)
    {
        if (string.IsNullOrWhiteSpace(nodeKey))
            return null;
        if (string.Equals(node.NodeKey, nodeKey, StringComparison.OrdinalIgnoreCase))
            return node;
        foreach (var child in node.Children)
        {
            var found = FindVisibleTreeNode(child, nodeKey);
            if (found is not null)
                return found;
        }
        return null;
    }

    /// <summary>
    /// 根据左侧树节点重建模板范围选项，避免模板永远使用一套无关的通用示例。
    /// </summary>
    private void RebuildTemplateScopeOptions()
    {
        var devices = GetCurrentTemplateDevices();
        var targets = devices.Select(CreateTemplateTarget).ToList();

        TemplateScopeOptions.Clear();
        if (targets.Count == 0)
        {
            SelectedTemplateScope = null;
            OnPropertyChanged(nameof(TemplateScopeHint));
            OnPropertyChanged(nameof(CanDownloadTemplate));
            return;
        }

        var currentRangeName = GetCurrentTemplateScopeName();
        TemplateScopeOptions.Add(new DevicePointTemplateScopeChoice(
            "current-range",
            $"当前范围：{currentRangeName}",
            $"包含 {targets.Count} 个设备；模板范围由左侧树选择决定",
            DevicePointTemplateScopeKind.CurrentRange));
        SelectedTemplateScope = TemplateScopeOptions[0];
        OnPropertyChanged(nameof(TemplateScopeHint));
        OnPropertyChanged(nameof(CanDownloadTemplate));
    }

    /// <summary>
    /// 返回当前树节点覆盖的设备，不把设备名称或编码写回配置。
    /// </summary>
    private IReadOnlyList<DeviceConfig.DeviceEntry> GetCurrentTemplateDevices()
    {
        var devices = (_services.DeviceConfig.Devices ?? new List<DeviceConfig.DeviceEntry>())
            .Where(device => device is not null
                && (!string.IsNullOrWhiteSpace(device.Id)
                    || !string.IsNullOrWhiteSpace(device.Code)
                    || !string.IsNullOrWhiteSpace(device.Name)))
            .ToList();
        var node = SelectedTreeNode;

        if (node is { IsVirtual: true } or { IsOrphan: true })
            return Array.Empty<DeviceConfig.DeviceEntry>();

        IEnumerable<DeviceConfig.DeviceEntry> scoped = node?.Kind switch
        {
            DevicePointTreeNodeKind.Device
                or DevicePointTreeNodeKind.Group
                when !string.IsNullOrWhiteSpace(node.OwnerDeviceId)
                => devices.Where(device => string.Equals(
                    GetDeviceIdentity(device), node.OwnerDeviceId, StringComparison.OrdinalIgnoreCase)),
            DevicePointTreeNodeKind.Channel => devices.Where(device =>
                string.Equals(device.ChannelId, node.Id, StringComparison.OrdinalIgnoreCase)
                || string.Equals(device.ChannelId, node.Code, StringComparison.OrdinalIgnoreCase)),
            _ => devices
        };

        return scoped
            .GroupBy(GetDeviceIdentity, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    /// <summary>
    /// 根据模板范围从当前树覆盖设备中筛选导出目标。
    /// </summary>
    private IReadOnlyList<DeviceConfig.DeviceEntry> GetTemplateScopeDevices(
        DevicePointTemplateScopeChoice scope)
    {
        var current = GetCurrentTemplateDevices();
        return scope.Kind switch
        {
            DevicePointTemplateScopeKind.CurrentDevice => current
                .Where(device => string.Equals(GetDeviceIdentity(device), scope.DeviceId, StringComparison.OrdinalIgnoreCase))
                .ToList(),
            DevicePointTemplateScopeKind.CurrentProtocol => current
                .Where(device => scope.Protocol.HasValue
                    && CreateTemplateTarget(device).Protocol == scope.Protocol.Value)
                .ToList(),
            _ => current
        };
    }

    private DevicePointTemplateTarget CreateTemplateTarget(DeviceConfig.DeviceEntry device)
    {
        var descriptor = (_services.DriverDescriptors ?? Array.Empty<IDeviceDriverDescriptor>())
            .FirstOrDefault(item => string.Equals(item.DriverKey, device.DriverKey, StringComparison.OrdinalIgnoreCase));
        var protocol = DevicePointTypeCatalog.TryParseDriverKey(device.DriverKey, out var driverProtocol)
            ? driverProtocol
            : DevicePointProtocol.Unknown;
        var code = string.IsNullOrWhiteSpace(device.Code)
            ? device.Name.Trim()
            : device.Code.Trim();
        var name = string.IsNullOrWhiteSpace(device.Name) ? code : device.Name.Trim();
        var protocolText = DevicePointTypeCatalog.ToDisplayName(
            protocol,
            descriptor?.DisplayName ?? "未识别通信方式");
        return new DevicePointTemplateTarget(
            GetDeviceIdentity(device),
            code,
            name,
            protocol,
            protocolText,
            descriptor?.SupportedDataTypes ?? new HashSet<DevicePointDataType>(),
            device.Model?.Trim() ?? string.Empty);
    }

    private DevicePointDeviceChoice CreateDeviceChoice(DeviceConfig.DeviceEntry device)
    {
        var descriptor = (_services.DriverDescriptors ?? Array.Empty<IDeviceDriverDescriptor>())
            .FirstOrDefault(item => string.Equals(item.DriverKey, device.DriverKey, StringComparison.OrdinalIgnoreCase));
        var model = descriptor?.DeviceModels.FirstOrDefault(item =>
            string.Equals(item.Key, device.Model, StringComparison.OrdinalIgnoreCase));
        var code = string.IsNullOrWhiteSpace(device.Code) ? device.Name.Trim() : device.Code.Trim();
        return new DevicePointDeviceChoice(
            GetDeviceIdentity(device),
            code,
            string.IsNullOrWhiteSpace(device.Name) ? code : device.Name.Trim(),
            device.DriverKey?.Trim() ?? string.Empty,
            device.DeviceMode,
            device.Model?.Trim() ?? string.Empty,
            model?.AddressWatermark ?? string.Empty,
            model?.AddressHint ?? string.Empty);
    }

    private DeviceConfig.DeviceEntry? ResolveSelectedDevice()
    {
        var node = SelectedTreeNode;
        if (node is null || node.Kind == DevicePointTreeNodeKind.Root)
            return null;

        var deviceId = node.OwnerDeviceId;
        if (string.IsNullOrWhiteSpace(deviceId) && node.Kind == DevicePointTreeNodeKind.Channel)
        {
            var channelDevices = (_services.DeviceConfig.Devices ?? new List<DeviceConfig.DeviceEntry>())
                .Where(device => string.Equals(device.ChannelId, node.Id, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(device.ChannelId, node.Code, StringComparison.OrdinalIgnoreCase))
                .ToList();
            deviceId = channelDevices.Count == 1 ? GetDeviceIdentity(channelDevices[0]) : string.Empty;
        }
        return FindConfiguredDevice(deviceId);
    }

    private ChannelEntry? ResolveSelectedChannel()
    {
        var node = SelectedTreeNode;
        if (node is null || node.Kind == DevicePointTreeNodeKind.Root)
            return null;

        var reference = node.Kind == DevicePointTreeNodeKind.Channel
            ? node.Id
            : ResolveSelectedDevice()?.ChannelId?.Trim();
        if (string.IsNullOrWhiteSpace(reference))
            return null;

        return (_services.DeviceConfig.Channels ?? new List<ChannelEntry>())
            .FirstOrDefault(channel =>
                string.Equals(channel.Id, reference, StringComparison.OrdinalIgnoreCase)
                || string.Equals(channel.Code, reference, StringComparison.OrdinalIgnoreCase));
    }

    private void UpdateEventScope()
    {
        var device = ResolveSelectedDevice();
        var channel = ResolveSelectedChannel();
        EventLog.SetScope(channel?.Id, device?.Id);
    }

    private DeviceConfig.DeviceEntry? FindConfiguredDevice(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return null;
        return (_services.DeviceConfig.Devices ?? new List<DeviceConfig.DeviceEntry>())
            .FirstOrDefault(device =>
                string.Equals(GetDeviceIdentity(device), deviceId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(device.Code, deviceId, StringComparison.OrdinalIgnoreCase));
    }

    private string GetCurrentTemplateScopeName()
    {
        var node = SelectedTreeNode;
        if (node is null || node.Kind == DevicePointTreeNodeKind.Root)
            return "全部点位";
        var name = string.IsNullOrWhiteSpace(node.Name) ? node.Code : node.Name;
        return node.Kind switch
        {
            DevicePointTreeNodeKind.Channel => $"通道“{name}”",
            DevicePointTreeNodeKind.Device => $"设备“{name}”",
            DevicePointTreeNodeKind.Group => $"分组“{name}”",
            _ => "全部点位"
        };
    }

    private static string GetDeviceIdentity(DeviceConfig.DeviceEntry device)
        => !string.IsNullOrWhiteSpace(device.Id)
            ? device.Id.Trim()
            : !string.IsNullOrWhiteSpace(device.Code)
                ? device.Code.Trim()
                : device.Name.Trim();

    private static DevicePointTreeNodeViewModel? FindTreeNode(
        DevicePointTreeNodeViewModel node,
        string? id,
        string? ownerDeviceId)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        if (string.Equals(node.Id, id, StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrWhiteSpace(ownerDeviceId)
                || string.Equals(node.OwnerDeviceId, ownerDeviceId, StringComparison.OrdinalIgnoreCase)))
            return node;
        foreach (var child in node.Children)
        {
            var found = FindTreeNode(child, id, ownerDeviceId);
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
        OnPropertyChanged(nameof(CanShowAddAction));
        OnPropertyChanged(nameof(AddActionText));
        OnPropertyChanged(nameof(CanEditSelectedNode));
        OnPropertyChanged(nameof(CanDeleteSelectedNode));
        OnPropertyChanged(nameof(CanMoveSelectedPoint));
        OnPropertyChanged(nameof(CanApplyMoveSelectedPoint));
        OnPropertyChanged(nameof(CanEditSelectedPoint));
        OnPropertyChanged(nameof(CanCopySelectedPoint));
        OnPropertyChanged(nameof(CanCutSelectedPoint));
        OnPropertyChanged(nameof(CanPastePoint));
        OnPropertyChanged(nameof(CanOpenDiagnostics));
        OnPropertyChanged(nameof(CanExportCurrentRange));
        OnPropertyChanged(nameof(CanStartPageOperation));
        OnPropertyChanged(nameof(OrphanDeviceCount));
        OnPropertyChanged(nameof(HasOrphanDevices));
        OnPropertyChanged(nameof(OrphanHint));
        OnPropertyChanged(nameof(TemplateScopeHint));
        OnPropertyChanged(nameof(CanDownloadTemplate));
        OnPropertyChanged(nameof(ActiveRevisionText));
        NotifyScopeDetails();
    }

    private void RefreshTreeSelectionVisual()
    {
        foreach (var node in _treeNodeCache.Values)
            node.IsSelected = ReferenceEquals(node, SelectedTreeNode);
    }

    private void NotifyScopeDetails()
    {
        OnPropertyChanged(nameof(ScopeDetailsName));
        OnPropertyChanged(nameof(ScopeDetailsTypeText));
        OnPropertyChanged(nameof(ScopeDetailsStatusText));
        OnPropertyChanged(nameof(ScopeDetailsStatusBackground));
        OnPropertyChanged(nameof(ScopeDetailsStatusForeground));
        OnPropertyChanged(nameof(ScopeDetailsTransportText));
        OnPropertyChanged(nameof(ScopeDetailsDriverText));
        OnPropertyChanged(nameof(ScopeDetailsPointCountText));
        OnPropertyChanged(nameof(ScopeDetailsPollIntervalText));
        OnPropertyChanged(nameof(ScopeDetailsEndpointText));
        OnPropertyChanged(nameof(ScopeDetailsDescription));
        OnPropertyChanged(nameof(HasS7OptimizedBlockAccessScopeNotice));
        OnPropertyChanged(nameof(S7OptimizedBlockAccessScopeNotice));
    }

    private SiemensS7OptimizedBlockAccessNotice? GetS7OptimizedBlockAccessScopeNotice()
        => SiemensS7OptimizedBlockAccessAdvisor.Inspect(
            GetVisibleEntries(SelectedTreeNode),
            _services.DeviceConfig.Devices,
            requireHardwareMode: true);

    private void RefreshMoveGroups()
    {
        var deviceId = SelectedPoint is not null
            ? ResolveDeviceId(SelectedPoint.Entry)
            : SelectedTreeNode?.OwnerDeviceId ?? string.Empty;
        MoveGroupOptions.Clear();
        foreach (var group in _allGroups
                     .Where(group => string.Equals(group.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase)
                         && !string.Equals(group.Code, "DEFAULT", StringComparison.OrdinalIgnoreCase))
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
                StatusText = device.DeviceMode == DeviceMode.Simulation ? "仿真模式" : "硬件模式"
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
        _services.DeviceConfig.PollIntervalMs = source.PollIntervalMs;
        _services.DeviceConfig.TimeoutMs = source.TimeoutMs;
        _services.DeviceConfig.Channels = source.Channels;
        _services.DeviceConfig.Devices = source.Devices;
    }

    private void RefreshVisiblePoints()
    {
        var selectedPointId = SelectedPoint?.PointId;
        var selected = SelectedTreeNode;
        var visible = GetVisibleEntries(selected)
            .Where(point => MatchesPointSearch(point, PointSearchText))
            .OrderBy(point => string.IsNullOrWhiteSpace(point.Name) ? point.Code : point.Name,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(point => point.Address, StringComparer.OrdinalIgnoreCase)
            .ThenBy(point => point.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var desiredRows = visible.Select(CreatePointRow).ToList();
        ReconcilePointRows(desiredRows);
        SelectedPoint = string.IsNullOrWhiteSpace(selectedPointId)
            ? null
            : Points.FirstOrDefault(row =>
                string.Equals(row.PointId, selectedPointId, StringComparison.OrdinalIgnoreCase));
        OnPropertyChanged(nameof(HasPoints));
        OnPropertyChanged(nameof(SelectedScopeTitle));
        OnPropertyChanged(nameof(SelectedScopeText));
        OnPropertyChanged(nameof(ScopeText));
        OnPropertyChanged(nameof(CanMoveSelectedPoint));
        OnPropertyChanged(nameof(HasS7OptimizedBlockAccessScopeNotice));
        OnPropertyChanged(nameof(S7OptimizedBlockAccessScopeNotice));
    }

    private IReadOnlyList<PointsConfig.PointEntry> GetVisibleEntries(
        DevicePointTreeNodeViewModel? selected)
    {
        if (selected is null || selected.Kind == DevicePointTreeNodeKind.Root)
            return _allEntries.ToList();
        if (selected.IsOrphan || selected.IsVirtual)
            return Array.Empty<PointsConfig.PointEntry>();

        return selected.Kind switch
        {
            DevicePointTreeNodeKind.Channel => PointsForChannelId(selected.Id, selected.Code),
            DevicePointTreeNodeKind.Device => _allEntries
                .Where(point => string.Equals(
                    ResolveDeviceId(point), selected.Id, StringComparison.OrdinalIgnoreCase))
                .ToList(),
            DevicePointTreeNodeKind.Group => _allEntries
                .Where(point => IsPointInGroup(point, new PointsConfig.PointGroupEntry
                {
                    Id = selected.Id,
                    Code = selected.Code
                }) && string.Equals(
                    ResolveDeviceId(point), selected.OwnerDeviceId, StringComparison.OrdinalIgnoreCase))
                .ToList(),
            _ => _allEntries.ToList()
        };
    }

    private static bool MatchesPointSearch(PointsConfig.PointEntry point, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return true;
        var text = query.Trim();
        return (string.IsNullOrWhiteSpace(point.Name) ? point.Code : point.Name)
            .Contains(text, StringComparison.OrdinalIgnoreCase)
            || point.Code.Contains(text, StringComparison.OrdinalIgnoreCase)
            || point.Address.Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    private DevicePointRow CreatePointRow(PointsConfig.PointEntry point)
    {
        var group = _allGroups.FirstOrDefault(item =>
            string.Equals(item.Id, point.GroupId, StringComparison.OrdinalIgnoreCase)
            || (string.Equals(item.Code, point.GroupCode, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.DeviceId, ResolveDeviceId(point), StringComparison.OrdinalIgnoreCase)));
        var deviceCode = ResolveDeviceCode(point);
        var deviceName = ResolveDeviceName(point);
        var isDefaultGroup = group is null
            || string.Equals(group.Code, "DEFAULT", StringComparison.OrdinalIgnoreCase)
            || string.Equals(point.GroupCode, "DEFAULT", StringComparison.OrdinalIgnoreCase);
        var groupCode = isDefaultGroup ? string.Empty : group?.Code ?? point.GroupCode;
        var groupName = isDefaultGroup ? string.Empty : group?.Name ?? string.Empty;
        var pollInterval = ResolvePollIntervalText(point);
        var rowKey = GetPointRowKey(point);
        if (_pointRowCache.TryGetValue(rowKey, out var existing))
        {
            existing.UpdateFrom(point, deviceCode, deviceName, groupCode, groupName, pollInterval);
            return existing;
        }

        var created = new DevicePointRow
        {
            Entry = point,
            DeviceCode = deviceCode,
            DeviceName = deviceName,
            GroupCode = groupCode,
            GroupName = groupName,
            PollIntervalText = pollInterval
        };
        _pointRowCache[rowKey] = created;
        return created;
    }

    private void ReconcilePointRows(IReadOnlyList<DevicePointRow> desired)
    {
        for (var index = 0; index < desired.Count; index++)
        {
            var wanted = desired[index];
            var existing = Points.FirstOrDefault(row =>
                string.Equals(
                    GetPointRowKey(row.Entry),
                    GetPointRowKey(wanted.Entry),
                    StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                Points.Insert(index, wanted);
                continue;
            }

            existing.UpdateFrom(
                wanted.Entry,
                wanted.DeviceCode,
                wanted.DeviceName,
                wanted.GroupCode,
                wanted.GroupName,
                wanted.PollIntervalText);
            var existingIndex = Points.IndexOf(existing);
            if (existingIndex != index)
                Points.Move(existingIndex, index);
        }
        while (Points.Count > desired.Count)
            Points.RemoveAt(Points.Count - 1);
    }

    private static string GetPointRowKey(PointsConfig.PointEntry point)
        => !string.IsNullOrWhiteSpace(point.Id)
            ? "id:" + point.Id.Trim()
            : $"legacy:{point.Code.Trim()}\u001f{point.Address.Trim()}";

    private string? GetDeleteFallbackPointId(DevicePointRow selected)
    {
        var index = Points.IndexOf(selected);
        if (index < 0)
            return null;

        return Points.Skip(index + 1).FirstOrDefault()?.PointId
            ?? Points.Take(index).LastOrDefault()?.PointId;
    }

    private List<PointsConfig.PointEntry> PointsForDeviceIds(ISet<string> deviceIds)
        => _allEntries.Where(point => deviceIds.Contains(ResolveDeviceId(point))).ToList();

    private List<PointsConfig.PointEntry> PointsForChannelId(string channelId, string channelCode)
    {
        var deviceIds = (_services.DeviceConfig.Devices ?? new List<DeviceConfig.DeviceEntry>())
            .Where(device => string.Equals(device.ChannelId, channelId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(device.ChannelId, channelCode, StringComparison.OrdinalIgnoreCase))
            .Select(GetDeviceIdentity)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return PointsForDeviceIds(deviceIds);
    }

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

    private string ResolveDeviceName(PointsConfig.PointEntry point)
    {
        var device = FindConfiguredDevice(ResolveDeviceId(point));
        return device is null || string.IsNullOrWhiteSpace(device.Name)
            ? ResolveDeviceCode(point)
            : device.Name.Trim();
    }

    private string GetPointTag(PointsConfig.PointEntry point)
    {
        var groupCode = point.GroupCode;
        if (string.IsNullOrWhiteSpace(groupCode))
        {
            groupCode = _allGroups.FirstOrDefault(group =>
                string.Equals(group.Id, point.GroupId, StringComparison.OrdinalIgnoreCase))?.Code;
        }

        return DevicePointTag.Format(
            groupCode,
            string.IsNullOrWhiteSpace(point.Name) ? point.Code : point.Name);
    }

    private string ResolvePollIntervalText(PointsConfig.PointEntry point)
    {
        var device = FindConfiguredDevice(ResolveDeviceId(point));
        var interval = device?.PollIntervalMs > 0
            ? device.PollIntervalMs
            : _services.DeviceConfig.PollIntervalMs;
        return interval > 0 ? $"设备配置：{interval} ms" : "未配置";
    }

    private static PointsConfig.PointEntry ClonePoint(PointsConfig.PointEntry source)
        => new()
        {
            Id = source.Id,
            Code = source.Code,
            Name = source.Name,
            Protocol = source.Protocol,
            DeviceId = source.DeviceId,
            DeviceCode = source.DeviceCode,
            GroupId = source.GroupId,
            GroupCode = source.GroupCode,
            Address = source.Address,
            AddressDefinition = source.AddressDefinition is null ? null : new PointAddressDefinition
            {
                Area = source.AddressDefinition.Area,
                Offset = source.AddressDefinition.Offset,
                BitIndex = source.AddressDefinition.BitIndex,
                DbNumber = source.AddressDefinition.DbNumber,
                ByteOffset = source.AddressDefinition.ByteOffset,
                BitOffset = source.AddressDefinition.BitOffset,
                LogicalAddress = source.AddressDefinition.LogicalAddress
            },
            DataType = source.DataType,
            RawDataType = source.RawDataType,
            DecodeOptions = source.DecodeOptions is null ? new DecodeOptions() : new DecodeOptions
            {
                ByteOrder = source.DecodeOptions.ByteOrder,
                WordOrder = source.DecodeOptions.WordOrder
            },
            WritePolicy = source.WritePolicy,
            IsWritable = source.IsWritable,
            RiskLevel = source.RiskLevel,
            Scale = source.Scale is null ? null : new PointsConfig.ScaleValues
            {
                RawMin = source.Scale.RawMin,
                RawMax = source.Scale.RawMax,
                EngMin = source.Scale.EngMin,
                EngMax = source.Scale.EngMax
            },
            RawMin = source.RawMin,
            RawMax = source.RawMax,
            EngMin = source.EngMin,
            EngMax = source.EngMax,
            Description = source.Description
        };

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
