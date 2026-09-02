namespace XXX.TestBench.Core.Domain.Identity;

/// <summary>
/// 第一版权限代码。UI 隐藏/禁用不是安全边界，用例与仓储层需再次检查。
/// </summary>
public enum PermissionCode
{
    ViewOverview = 1,
    ManageTasks = 2,
    ExecuteTests = 3,
    ManualControl = 4,
    ManageProducts = 5,
    ManageTestDefinitions = 6,
    ManageRecipes = 7,
    ViewRecords = 8,
    GenerateReports = 9,
    ManageDevices = 10,
    CalibrateDevices = 11,
    ManageUsers = 12,
    ViewLogs = 13
}
