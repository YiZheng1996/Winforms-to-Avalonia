using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace XXX.TestBench.App.ViewModels;

/// <summary>
/// 设备点位页面树节点类型。树只负责组织和筛选，不承载通信或实时值。
/// </summary>
public enum DevicePointTreeNodeKind
{
    Root,
    Channel,
    Device,
    Group,
    Point
}

/// <summary>
/// 设备与点位的单层树节点。节点身份直接复用配置中的稳定 Id。
/// </summary>
public sealed partial class DevicePointTreeNodeViewModel : ObservableObject
{
    public DevicePointTreeNodeViewModel(
        DevicePointTreeNodeKind kind,
        string id,
        string? parentId,
        string code,
        string name,
        string summaryText)
    {
        Kind = kind;
        Id = id;
        ParentId = parentId ?? string.Empty;
        Code = code;
        Name = name;
        SummaryText = summaryText;
        Children.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasChildren));
    }

    public DevicePointTreeNodeKind Kind { get; }
    public string Id { get; }
    public string ParentId { get; }
    public string Code { get; }
    public string Name { get; }
    public string SummaryText { get; }
    public ObservableCollection<DevicePointTreeNodeViewModel> Children { get; } = new();
    public bool HasChildren => Children.Count > 0;
    public string DisplayText => string.IsNullOrWhiteSpace(Name) || string.Equals(Code, Name, StringComparison.OrdinalIgnoreCase)
        ? Code
        : $"{Code}  {Name}";
    public string IconGlyph => Kind switch
    {
        DevicePointTreeNodeKind.Root => "⌂",
        DevicePointTreeNodeKind.Channel => "▣",
        DevicePointTreeNodeKind.Device => "▰",
        DevicePointTreeNodeKind.Group => "▤",
        DevicePointTreeNodeKind.Point => "●",
        _ => "•"
    };

    /// <summary>
    /// 搜索使用的客户可读文本，不改变配置集合。
    /// </summary>
    public string SearchText => $"{Code}\n{Name}\n{SummaryText}";

    [ObservableProperty]
    private bool _isExpanded;
}
