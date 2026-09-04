namespace XXX.TestBench.Core.Domain.Identity;

/// <summary>
/// 第一版权限代码。UI 隐藏/禁用不是安全边界，用例与仓储层需再次检查。
/// ManageTestPoints 沿用原 ManageTasks 的数值 2，保证历史数据库 role_permissions 数值语义不漂移。
/// </summary>
public enum PermissionCode
{
    /// <summary>
    /// 查看总览。
    /// </summary>
    ViewOverview = 1,
    /// <summary>
    /// 管理试验项点。
    /// </summary>
    ManageTestPoints = 2,
    /// <summary>
    /// 执行试验。
    /// </summary>
    ExecuteTests = 3,
    /// <summary>
    /// 手动控制。
    /// </summary>
    ManualControl = 4,
    /// <summary>
    /// 管理产品类型与型号。
    /// </summary>
    ManageProducts = 5,
    /// <summary>
    /// 管理试验定义。
    /// </summary>
    ManageTestDefinitions = 6,
    /// <summary>
    /// 管理配方。
    /// </summary>
    ManageRecipes = 7,
    /// <summary>
    /// 查看试验记录。
    /// </summary>
    ViewRecords = 8,
    /// <summary>
    /// 生成报表。
    /// </summary>
    GenerateReports = 9,
    /// <summary>
    /// 管理设备。
    /// </summary>
    ManageDevices = 10,
    /// <summary>
    /// 设备校准。
    /// </summary>
    CalibrateDevices = 11,
    /// <summary>
    /// 管理用户。
    /// </summary>
    ManageUsers = 12,
    /// <summary>
    /// 查看日志。
    /// </summary>
    ViewLogs = 13
}
