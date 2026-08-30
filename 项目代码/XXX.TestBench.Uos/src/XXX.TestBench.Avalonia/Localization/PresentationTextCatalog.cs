using System.Globalization;

namespace XXX.TestBench.Avalonia.Localization;

/// <summary>
/// Presentation 层文本边界。当前只交付中文资源，后续增加语言时无需修改 Core/Gateway。
/// </summary>
/// <summary>
/// 集中保存界面文案键，给后续资源文件/多语言切换保留稳定边界；业务状态不放入此目录。
/// </summary>
public sealed class PresentationTextCatalog
{
    private static readonly IReadOnlyDictionary<string, string> ZhCn = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["App.Title"] = "XXX 试验台",
        ["Nav.Overview"] = "运行总览",
        ["Nav.Overview.Description"] = "连接、安全联锁与 P2 五点只读状态",
        ["Nav.Test"] = "试验作业",
        ["Nav.Test.Description"] = "产品信息、项点选择、进度与结果入口",
        ["Nav.Process"] = "工艺与手动",
        ["Nav.Process.Description"] = "气路、电气量和只读执行元件状态",
        ["Nav.Management"] = "参数管理",
        ["Nav.Management.Description"] = "用户、权限、车型、型号、项点和报表参数",
        ["Nav.Reports"] = "数据查询与报表",
        ["Nav.Reports.Description"] = "组合条件查询、记录选择和报表浏览入口",
        ["Nav.Calibration"] = "硬件校准",
        ["Nav.Calibration.Description"] = "Zero/Gain 只读核对和本地计算",
        ["Nav.Diagnostics"] = "日志与维护",
        ["Nav.Diagnostics.Description"] = "运行日志及项目级停用维护功能的迁移状态",
        ["Nav.Instrument"] = "绝缘耐压",
        ["Nav.Instrument.Description"] = "仪器参数、连接状态和试验请求边界"
    };

    public PresentationTextCatalog(CultureInfo? culture = null)
    {
        Culture = culture ?? CultureInfo.GetCultureInfo("zh-CN");
    }

    public CultureInfo Culture { get; }

    public string Get(string key) => ZhCn.TryGetValue(key, out var value) ? value : $"[{key}]";
}
