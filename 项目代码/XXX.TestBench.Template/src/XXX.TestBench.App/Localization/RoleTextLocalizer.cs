using XXX.TestBench.Core.Domain.Identity;

namespace XXX.TestBench.App.Localization;

/// <summary>
/// 角色的界面显示名称转换器。
/// 内置角色的 SystemKey 和数据库名称保持稳定，仅在界面显示客户可读的中文名称。
/// </summary>
public static class RoleTextLocalizer
{
    private static readonly IReadOnlyDictionary<string, string> BuiltInNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Administrator"] = "系统管理员",
        ["Operator"] = "试验操作员",
        ["Maintenance"] = "设备维护员"
    };

    /// <summary>
    /// 获取角色的界面显示名称。
    /// 自定义角色沿用管理员创建时填写的名称。
    /// </summary>
    public static string DisplayName(Role role)
        => DisplayName(role.SystemKey, role.Name);

    /// <summary>
    /// 根据内置标识和角色名称获取界面显示名称。
    /// </summary>
    public static string DisplayName(string? systemKey, string? roleName)
    {
        if (!string.IsNullOrWhiteSpace(systemKey)
            && BuiltInNames.TryGetValue(systemKey.Trim(), out var displayName))
            return displayName;

        return string.IsNullOrWhiteSpace(roleName) ? "未命名角色" : roleName.Trim();
    }
}
