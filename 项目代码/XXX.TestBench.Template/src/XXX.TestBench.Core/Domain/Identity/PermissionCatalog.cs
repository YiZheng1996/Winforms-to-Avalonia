namespace XXX.TestBench.Core.Domain.Identity;

/// <summary>
/// 权限显示描述。权限代码仍是运行时授权的唯一标识，显示名称仅用于界面。
/// </summary>
public sealed record PermissionDescriptor(
    PermissionCode Code,
    string Group,
    string Name,
    string Description,
    IReadOnlyList<PermissionCode> RequiredPermissions);

/// <summary>
/// 当前产品线实际启用的权限目录。
/// ManageRecipes 保留用于历史数据库兼容，但当前没有对应业务入口，因此不在配置界面展示。
/// </summary>
public static class PermissionCatalog
{
    public static IReadOnlyList<PermissionDescriptor> Active { get; } = new[]
    {
        new PermissionDescriptor(PermissionCode.ViewOverview, "基础查看", "查看运行总览", "查看当前产品、设备状态和工艺总览。", Array.Empty<PermissionCode>()),
        new PermissionDescriptor(PermissionCode.ExecuteTests, "试验业务", "执行试验", "创建、启动、逐项执行和结束试验。", Array.Empty<PermissionCode>()),
        new PermissionDescriptor(PermissionCode.ManualControl, "试验业务", "手动控制", "执行工艺监控中的普通手动控制动作。", Array.Empty<PermissionCode>()),
        new PermissionDescriptor(PermissionCode.ViewRecords, "数据报表", "查看试验记录", "查询历史试验记录和结果。", Array.Empty<PermissionCode>()),
        new PermissionDescriptor(PermissionCode.GenerateReports, "数据报表", "生成报表", "根据已完成试验记录生成报表。", new[] { PermissionCode.ViewRecords }),
        new PermissionDescriptor(PermissionCode.ManageProducts, "产品参数", "管理产品类型与型号", "新增、编辑、启停产品类型和产品型号。", Array.Empty<PermissionCode>()),
        new PermissionDescriptor(PermissionCode.ManageTestPoints, "产品参数", "管理试验项点", "维护试验项点及产品型号项点配置。", Array.Empty<PermissionCode>()),
        new PermissionDescriptor(PermissionCode.ManageTestDefinitions, "产品参数", "管理试验参数", "维护项目级和产品类型与型号组合级试验参数。", Array.Empty<PermissionCode>()),
        new PermissionDescriptor(PermissionCode.ManageDevices, "设备控制", "管理设备与点位", "维护设备状态、设备点位和设备模式。", Array.Empty<PermissionCode>()),
        new PermissionDescriptor(PermissionCode.CalibrateDevices, "设备控制", "设备校准", "执行高风险设备校准写入。", new[] { PermissionCode.ManageDevices }),
        new PermissionDescriptor(PermissionCode.ViewLogs, "系统安全", "查看日志", "查看审计日志和诊断信息。", Array.Empty<PermissionCode>()),
        new PermissionDescriptor(PermissionCode.ManageUsers, "系统安全", "管理用户", "新增、编辑、停用用户并处理账号状态。", Array.Empty<PermissionCode>()),
        new PermissionDescriptor(PermissionCode.ManageRoles, "系统安全", "配置角色权限", "新增角色并配置角色拥有的权限。", Array.Empty<PermissionCode>())
    };

    public static IReadOnlySet<PermissionCode> ActiveCodes { get; } = Active.Select(item => item.Code).ToHashSet();

    public static IReadOnlyList<PermissionDescriptor> ForGroup(string group)
        => Active.Where(item => item.Group == group).ToArray();

    public static string GetName(PermissionCode code)
        => Active.FirstOrDefault(item => item.Code == code)?.Name ?? code.ToString();

    /// <summary>
    /// 应用权限依赖关系并过滤当前界面不可配置的保留权限。
    /// </summary>
    public static HashSet<PermissionCode> Normalize(IEnumerable<PermissionCode> permissions)
    {
        var result = permissions.Where(ActiveCodes.Contains).ToHashSet();
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var required in Active.Where(item => result.Contains(item.Code)).SelectMany(item => item.RequiredPermissions))
                changed |= result.Add(required);
        }
        return result;
    }
}
