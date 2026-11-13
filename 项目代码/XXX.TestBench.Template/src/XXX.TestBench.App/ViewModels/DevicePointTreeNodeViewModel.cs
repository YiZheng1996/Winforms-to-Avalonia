using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace XXX.TestBench.App.ViewModels;

/// <summary>
/// 设备点位页面树节点类型。点位只在右侧清单显示，不再作为树节点。
/// </summary>
public enum DevicePointTreeNodeKind
{
    Root,
    Channel,
    Device,
    Group
}

/// <summary>
/// 设备点位树节点可以发起的管理动作。
/// </summary>
public enum DevicePointTreeActionKind
{
    AddChannel,
    AddDevice,
    AddGroup,
    AddPoint,
    MovePoint,
    EditChannel,
    EditDevice,
    EditGroup,
    EditPoint,
    DeleteChannel,
    DeleteDevice,
    DeleteGroup,
    DeletePoint,
    CopyPoint,
    CutPoint,
    PastePoint,
    DiagnosePoint,
    DiagnoseScope
}

/// <summary>
/// 设备点位树右键菜单项。
/// </summary>
public sealed record DevicePointTreeAction(
    DevicePointTreeActionKind Kind,
    string Header,
    bool IsDestructive = false,
    bool IsSeparatorBefore = false);

/// <summary>
/// 设备点位页左侧树节点。
///
/// NodeKey 是页面状态的稳定身份，不能使用显示名称、地址或当前父集合索引。
/// 节点的显示属性可在配置应用后更新，Children 集合和 IsExpanded 尽量复用。
/// </summary>
public sealed partial class DevicePointTreeNodeViewModel : ObservableObject
{
    public DevicePointTreeNodeViewModel(
        DevicePointTreeNodeKind kind,
        string id,
        string? parentId,
        string code,
        string name,
        string summaryText,
        string? ownerDeviceId = null,
        bool isVirtual = false,
        bool isOrphan = false,
        string? nodeKey = null)
    {
        Kind = kind;
        Id = NormalizeId(id, kind.ToString());
        NodeKey = string.IsNullOrWhiteSpace(nodeKey)
            ? CreateNodeKey(kind, Id, ownerDeviceId)
            : nodeKey.Trim();
        Update(parentId, code, name, summaryText, ownerDeviceId, isVirtual, isOrphan);
        Children.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasChildren));
    }

    public DevicePointTreeNodeKind Kind { get; }
    public string Id { get; }
    public string NodeKey { get; }

    [ObservableProperty]
    private string _parentId = string.Empty;

    [ObservableProperty]
    private string _code = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTreeSummaryText))]
    [NotifyPropertyChangedFor(nameof(IsTreeSummaryVisible))]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    private string _ownerDeviceId = string.Empty;

    [ObservableProperty]
    private bool _isVirtual;

    [ObservableProperty]
    private bool _isOrphan;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectionBackground))]
    [NotifyPropertyChangedFor(nameof(SelectionBorderBrush))]
    [NotifyPropertyChangedFor(nameof(SelectionForeground))]
    [NotifyPropertyChangedFor(nameof(NodeIconBackground))]
    [NotifyPropertyChangedFor(nameof(NodeIconForeground))]
    private bool _isSelected;

    public ObservableCollection<DevicePointTreeNodeViewModel> Children { get; } = new();
    public bool HasChildren => Children.Count > 0;
    public bool IsChannel => Kind == DevicePointTreeNodeKind.Channel;
    public bool IsDevice => Kind == DevicePointTreeNodeKind.Device;
    public bool IsGroup => Kind == DevicePointTreeNodeKind.Group;
    public bool IsVectorIcon => IsChannel || IsDevice || IsGroup;

    /// <summary>
    /// 配置应用后更新节点的可变显示部分，不改变 NodeKey 或 Children 实例。
    /// </summary>
    public void Update(
        string? parentId,
        string? code,
        string? name,
        string? summaryText,
        string? ownerDeviceId = null,
        bool isVirtual = false,
        bool isOrphan = false)
    {
        ParentId = parentId?.Trim() ?? string.Empty;
        Code = code?.Trim() ?? string.Empty;
        Name = name?.Trim() ?? string.Empty;
        SummaryText = summaryText?.Trim() ?? string.Empty;
        OwnerDeviceId = ownerDeviceId?.Trim()
            ?? (Kind == DevicePointTreeNodeKind.Device ? Id : string.Empty);
        IsVirtual = isVirtual;
        IsOrphan = isOrphan;
    }

    /// <summary>
    /// 树节点身份规则集中在此处，避免页面不同重建路径产生不一致的选择状态。
    /// </summary>
    public static string CreateNodeKey(
        DevicePointTreeNodeKind kind,
        string id,
        string? ownerDeviceId = null)
    {
        var normalizedId = NormalizeId(id, kind.ToString());
        return kind switch
        {
            DevicePointTreeNodeKind.Root => "root",
            DevicePointTreeNodeKind.Channel => $"channel:{normalizedId}",
            DevicePointTreeNodeKind.Device => $"device:{normalizedId}",
            DevicePointTreeNodeKind.Group =>
                $"group:{NormalizeId(ownerDeviceId, "unknown-device")}:{normalizedId}",
            _ => $"{kind.ToString().ToLowerInvariant()}:{normalizedId}"
        };
    }

    /// <summary>
    /// 树只显示业务名称；稳定编码保留在内部属性中，供搜索、关联和排障使用。
    /// </summary>
    public string DisplayText => string.IsNullOrWhiteSpace(Name) ? Code : Name;

    /// <summary>
    /// 摘要只显示导航需要的状态和数量；连接参数、地址等在右侧清单或编辑器查看。
    /// </summary>
    public string TreeSummaryText => SummaryText;

    public bool HasTreeSummaryText => !string.IsNullOrWhiteSpace(TreeSummaryText);

    /// <summary>
    /// 通道和设备节点只显示名称，避免树中重复堆叠连接、驱动等辅助信息。
    /// 根节点和分组仍保留数量摘要，便于快速判断当前范围。
    /// </summary>
    public bool IsTreeSummaryVisible =>
        Kind is DevicePointTreeNodeKind.Root or DevicePointTreeNodeKind.Group
        && HasTreeSummaryText;

    /// <summary>
    /// 页面树节点的选择反馈。颜色放在节点模型中，避免把业务树模板和页面代码绑定在一起。
    /// </summary>
    public string SelectionBackground => IsSelected ? "#EAF2FF" : "Transparent";
    public string SelectionBorderBrush => IsSelected ? "#CFE0FF" : "Transparent";
    public string SelectionForeground => IsSelected ? "#0758D7" : "#344054";

    public string NodeIconBackground => IsSelected
        ? "#DDEAFF"
        : Kind switch
        {
            DevicePointTreeNodeKind.Root => "#EAF2FF",
            DevicePointTreeNodeKind.Channel => "#EEF7FF",
            DevicePointTreeNodeKind.Device => "#EEF9F2",
            DevicePointTreeNodeKind.Group => "#FFF5E5",
            _ => "#F2F4F7"
        };

    public string NodeIconForeground => IsSelected
        ? "#0868F7"
        : Kind switch
        {
            DevicePointTreeNodeKind.Root => "#0868F7",
            DevicePointTreeNodeKind.Channel => "#1677C8",
            DevicePointTreeNodeKind.Device => "#168653",
            DevicePointTreeNodeKind.Group => "#B7791F",
            _ => "#667085"
        };

    public string IconGlyph => Kind switch
    {
        DevicePointTreeNodeKind.Root => "⌂",
        DevicePointTreeNodeKind.Channel => "▣",
        DevicePointTreeNodeKind.Device => "▰",
        DevicePointTreeNodeKind.Group => "▤",
        _ => "•"
    };

    /// <summary>
    /// 搜索使用的客户可读文本，不改变配置集合。
    /// </summary>
    public string SearchText => $"{Code}\n{Name}\n{SummaryText}";

    private static string NormalizeId(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
