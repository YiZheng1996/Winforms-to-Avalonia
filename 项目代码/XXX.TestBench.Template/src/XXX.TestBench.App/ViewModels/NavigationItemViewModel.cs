using CommunityToolkit.Mvvm.ComponentModel;

namespace XXX.TestBench.App.ViewModels;

/// <summary>
/// 左侧导航中的一个菜单项：绑定标题、图标与对应页面，并跟踪选中状态。
/// </summary>
public sealed partial class NavigationItemViewModel : ObservableObject
{
    public NavigationItemViewModel(string title, string displayTitle, string iconGlyph, PageViewModel page)
    {
        Title = title;
        DisplayTitle = displayTitle;
        IconGlyph = iconGlyph;
        Page = page;
    }

    /// <summary>
    /// 内部功能名，保持权限测试和页面路由兼容。
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// 参考界面中显示的短标题。
    /// </summary>
    public string DisplayTitle { get; }

    /// <summary>
    /// 导航项显示的图标字符。
    /// </summary>
    public string IconGlyph { get; }

    /// <summary>
    /// 该导航项对应的页面视图模型。
    /// </summary>
    public PageViewModel Page { get; }

    /// <summary>
    /// 当前导航项是否被选中，选中项在侧边栏高亮显示。
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;
}