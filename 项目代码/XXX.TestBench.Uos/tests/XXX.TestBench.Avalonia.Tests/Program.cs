using XXX.TestBench.Avalonia.ViewModels;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Gateway.Domain;
using XXX.TestBench.Gateway.Infrastructure;

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

var settings = new GatewaySettingsDocument();
await using var runtime = new ReadOnlyGatewayRuntime(
    settings.Gateway,
    new SimulatedS7ReadOnlyTransport(),
    new SimulatedModbusReadOnlyTransport(),
    isSimulated: true);
using var viewModel = new MainWindowViewModel(
    new TestBenchStateMachine(),
    runtime,
    log: null,
    configPath: "offline-test",
    transportMode: "OfflineSimulation");

await viewModel.StartAsync();
Assert(viewModel.StatusText == "待登录", "UI 启动后没有停在待登录状态。");
Assert(viewModel.Points.Count == 5, "UI 没有显示 P2 五点。");
Assert(viewModel.Points.Single(x => x.PointId == "SMART.PLC.AI.MAI00").DisplayValue == "42.5", "UI 没有显示 S7 AI00 仿真值。");
Assert(viewModel.ConnectionText.Contains("仿真", StringComparison.Ordinal), "UI 没有明确显示仿真来源。");
Assert(viewModel.WritesText.Contains("禁用", StringComparison.Ordinal), "UI 没有显示写入禁用状态。");

viewModel.LoginCommand.Execute(null);
Assert(viewModel.StatusText == "就绪", "仿真会话登录后没有进入 Ready。");
viewModel.ProductId = "B11-UI-Test";
viewModel.SelectProductCommand.Execute(null);
Assert(!viewModel.CanStartAutomaticTest, "Test00 未接入时 UI 错误开放自动试验。");
Assert(viewModel.SafetyText.Contains("Test00 尚未接入", StringComparison.Ordinal), "UI 没有解释控制按钮禁用原因。");

// 导航和各任务页面必须共享一个状态源，不能以“文件存在”代替可执行行为。
Assert(viewModel.NavigationItems.Count == 8, "Legacy 主要界面没有形成完整任务导航。");
Assert(viewModel.NavigationItems.Select(item => item.Key).Distinct(StringComparer.Ordinal).Count() == 8,
    "界面导航键存在重复。");
viewModel.NavigateCommand.Execute("test");
Assert(viewModel.IsTestOperationVisible && !viewModel.IsOverviewVisible, "试验作业导航没有切换可见状态。");

var operation = viewModel.TestOperation;
Assert(operation.TestItems.Count == 21
       && operation.TestItems.Select(item => item.LegacyClassName).Distinct(StringComparer.Ordinal).Count() == 21,
    "Legacy 21 个具体试验类没有唯一映射到项点状态。");
operation.ProductNumber = "B11-0001";
operation.VehicleNumber = "CAR-001";
operation.Remarks = "Avalonia 行为测试";
Assert(operation.CanSubmitProduct, "完整产品信息未使提交命令可用。");
operation.SubmitProductCommand.Execute(null);
Assert(viewModel.ProductId == "B11-0001", "试验作业没有通过同一 Core 产品合同提交。");
operation.SelectVisibleCommand.Execute(null);
Assert(operation.SelectedItemCount == 7, "B11 实际七个项点没有按车型选择。");
Assert(!operation.CanStartAutomaticTest, "Test00 未接入时项点页错误开放自动试验。");
Assert(!operation.StartAutomaticTestCommand.CanExecute(null), "项点页命令自身未执行 Test00/项点双重门禁。");
Assert(viewModel.ProcessOverview.Outputs.All(output => !output.IsEnabled), "工艺页存在可用的危险写控件。");

var management = viewModel.Management;
Assert(management.Categories.Count == 9
       && management.Categories.Select(category => category.Key).SequenceEqual(
       ["users", "roles", "permissions", "allocation", "vehicle-types", "models", "test-items", "item-config", "test-params"]),
    "AVM-MGMT 九类管理入口专属行为证据缺失。");

// 角色名称和描述均为 Legacy 必填字段；这些会话记录同时作为用户/权限分配的下拉候选。
management.SelectedCategory = management.Categories.Single(category => category.Key == "roles");
foreach (var (roleName, description) in new[]
         {
             ("操作员", "执行试验"),
             ("试验员", "执行试验但不维护权限")
         })
{
    management.DraftName = roleName;
    Assert(!management.CanAdd, "角色管理错误允许缺少角色描述。");
    management.DraftDescription = description;
    management.AddCommand.Execute(null);
    management.ResetCommand.Execute(null);
}
Assert(management.Records.Count == 2
       && management.Records.All(record => !string.IsNullOrWhiteSpace(record.Values["description"])),
    "角色名称/描述没有完整进入会话记录。");

// 用户管理只要求用户名唯一，多个用户可以引用同一个角色；密码留在尚未接入的认证边界。
management.SelectedCategory = management.Categories.Single(category => category.Key == "users");
Assert(management.NameLabel == "用户名" && management.CodeLabel == "角色" && management.CodeUsesSelection
       && management.CodeOptions.SequenceEqual(["操作员", "试验员"]),
    "用户管理没有通过 Legacy 角色下拉候选呈现用户名/角色字段。");
management.DraftName = "operator-a";
Assert(!management.CanAdd, "用户管理错误允许缺少角色的记录。");
management.DraftCode = "操作员";
management.AddCommand.Execute(null);
var firstUser = management.SelectedRecord!;
management.ResetCommand.Execute(null);
management.DraftName = "operator-b";
management.DraftCode = "操作员";
Assert(management.CanAdd, "用户管理错误地把可复用角色当作唯一代码。");
management.AddCommand.Execute(null);
var secondUser = management.SelectedRecord!;
management.DraftName = firstUser.Name;
Assert(!management.CanUpdate && !management.UpdateCommand.CanExecute(null),
    "用户管理修改错误允许了重复用户名。");
management.UpdateCommand.Execute(null);
Assert(secondUser.Name == "operator-b", "被拒绝的重复修改仍改变了用户记录。");

// 权限类型在 Legacy 中隐藏且未使用；迁移只保留名称、代码、控件名称和备注。
management.SelectedCategory = management.Categories.Single(category => category.Key == "permissions");
Assert(management.EditorFields.Count == 1 && management.EditorFields[0].Key == "control-name"
       && !management.CategoryFields.Contains("权限类型", StringComparison.Ordinal),
    "权限管理错误新增了 Legacy 未使用字段或遗漏控件名称。");
management.DraftName = "查看报表";
management.DraftCode = "REPORT_VIEW";
management.DraftDescription = "报表查询入口";
management.EditorFields[0].Value = "btnReport";
management.AddCommand.Execute(null);
var firstPermission = management.SelectedRecord!;
management.ResetCommand.Execute(null);
management.DraftName = "导出报表";
management.DraftCode = "REPORT_EXPORT";
management.EditorFields[0].Value = "btnExport";
management.AddCommand.Execute(null);
var secondPermission = management.SelectedRecord!;
management.EditorFields[0].Value = firstPermission.Values["control-name"];
Assert(!management.CanUpdate, "权限修改错误允许了重复控件名称。");
management.EditorFields[0].Value = "btnExport";
management.DraftCode = firstPermission.Code;
Assert(!management.CanUpdate, "权限修改错误允许了重复权限代码。");
management.UpdateCommand.Execute(null);
Assert(secondPermission.Code == "REPORT_EXPORT" && secondPermission.Values["control-name"] == "btnExport",
    "被拒绝的权限修改仍改变了会话记录。");

// Legacy 允许控件名称为空，只对非空控件名称执行唯一性校验。
var optionalControlManagement = new ManagementViewModel();
optionalControlManagement.SelectedCategory = optionalControlManagement.Categories.Single(category => category.Key == "permissions");
foreach (var (permissionName, permissionCode) in new[] { ("空控件权限一", "EMPTY_CONTROL_1"), ("空控件权限二", "EMPTY_CONTROL_2") })
{
    optionalControlManagement.DraftName = permissionName;
    optionalControlManagement.DraftCode = permissionCode;
    Assert(optionalControlManagement.CanAdd, "权限管理错误地把 Legacy 可空控件名称设为必填或互相判重。");
    optionalControlManagement.AddCommand.Execute(null);
    optionalControlManagement.ResetCommand.Execute(null);
}
Assert(optionalControlManagement.Records.Count == 2,
    "两个空控件名称的合法权限没有进入会话记录。");

// 权限分配沿用 Legacy 的“角色 + 权限勾选”，且真实授权能力必须明确锁定。
management.SelectedCategory = management.Categories.Single(category => category.Key == "allocation");
Assert(management.ShowPermissionChecklist
       && management.NameUsesSelection
       && management.NameOptions.SequenceEqual(["操作员", "试验员"])
       && management.CategoryBoundaryText.Contains("不能授予", StringComparison.Ordinal),
    "权限分配没有显示真实授权锁定边界。");
management.DraftName = "试验员";
Assert(!management.CanAdd, "权限分配错误允许未勾选任何权限。");
Assert(management.PermissionOptions.Select(option => option.Name).SequenceEqual(["查看报表", "导出报表"]),
    "权限分配候选没有从权限管理会话记录同步。");
management.PermissionOptions.Single(option => option.Name == "查看报表").IsSelected = true;
var selectedPermissionKey = management.PermissionOptions.Single(option => option.Name == "查看报表").Key;
management.AddCommand.Execute(null);
var allocationRecord = management.Records.Single();
Assert(allocationRecord.Selections.SequenceEqual([selectedPermissionKey])
       && allocationRecord.Description.Contains("查看报表", StringComparison.Ordinal),
    "权限勾选没有以稳定 Id 进入会话分配记录，或显示名称丢失。");

// Legacy 删除权限会同步删除角色权限关系；旧分配记录不得把已删名称重新造回候选。
management.SelectedCategory = management.Categories.Single(category => category.Key == "permissions");
management.SelectedRecord = firstPermission;
management.DeleteCommand.Execute(null);
Assert(allocationRecord.Selections.Count == 0, "删除权限后，旧角色分配仍保留已删除权限关系。");
management.SelectedCategory = management.Categories.Single(category => category.Key == "allocation");
management.SelectedRecord = allocationRecord;
Assert(management.PermissionOptions.All(option => option.Name != "查看报表")
       && allocationRecord.Selections.Count == 0,
    "重新选择旧权限分配记录时，已删除权限被错误复活。");

// Legacy 只约束权限代码和非空控件名，权限名称允许重复；分配关系必须按 Id 区分同名记录。
var sameNameManagement = new ManagementViewModel();
sameNameManagement.SelectedCategory = sameNameManagement.Categories.Single(category => category.Key == "roles");
sameNameManagement.DraftName = "同名权限角色";
sameNameManagement.DraftDescription = "验证稳定 Id 关联";
sameNameManagement.AddCommand.Execute(null);
sameNameManagement.SelectedCategory = sameNameManagement.Categories.Single(category => category.Key == "permissions");
foreach (var (code, controlName) in new[] { ("SAME_NAME_1", "btnSameOne"), ("SAME_NAME_2", "btnSameTwo") })
{
    sameNameManagement.DraftName = "同名权限";
    sameNameManagement.DraftCode = code;
    sameNameManagement.EditorFields.Single(field => field.Key == "control-name").Value = controlName;
    sameNameManagement.AddCommand.Execute(null);
    sameNameManagement.ResetCommand.Execute(null);
}
var sameNamePermissions = sameNameManagement.Records.ToArray();
sameNameManagement.SelectedCategory = sameNameManagement.Categories.Single(category => category.Key == "allocation");
Assert(sameNameManagement.PermissionOptions.Count == 2
       && sameNameManagement.PermissionOptions.Select(option => option.Key).Distinct(StringComparer.Ordinal).Count() == 2,
    "两个同名、不同代码的合法权限在分配候选中被错误合并。");
sameNameManagement.DraftName = "同名权限角色";
foreach (var option in sameNameManagement.PermissionOptions)
    option.IsSelected = true;
sameNameManagement.AddCommand.Execute(null);
var sameNameAllocation = sameNameManagement.Records.Single();
Assert(sameNameAllocation.Selections.Count == 2, "同名权限没有分别写入会话分配关系。");
sameNameManagement.SelectedCategory = sameNameManagement.Categories.Single(category => category.Key == "permissions");
sameNameManagement.SelectedRecord = sameNamePermissions[0];
sameNameManagement.DeleteCommand.Execute(null);
var remainingPermissionKey = sameNamePermissions[1].Id.ToString();
Assert(sameNameAllocation.Selections.SequenceEqual([remainingPermissionKey])
       && sameNameAllocation.Description.Contains("同名权限", StringComparison.Ordinal),
    "删除一个同名权限时错误清除了另一个稳定 Id 的分配关系。");
sameNameManagement.SelectedCategory = sameNameManagement.Categories.Single(category => category.Key == "allocation");
sameNameManagement.SelectedRecord = sameNameAllocation;
Assert(sameNameManagement.PermissionOptions.Count == 1
       && sameNameManagement.PermissionOptions.Single().Key == remainingPermissionKey
       && sameNameManagement.PermissionOptions.Single().IsSelected,
    "重新选择分配记录后，剩余同名权限的稳定 Id 关系没有恢复。");

management.SelectedCategory = management.Categories.Single(category => category.Key == "vehicle-types");
management.DraftName = "B11";
management.DraftDescription = "动车组车型";
management.AddCommand.Execute(null);
Assert(management.Records.Single().Values["description"] == "动车组车型",
    "车型备注没有进入会话记录。");

// 型号发布在 Legacy 中有数据库副作用；当前只允许单向变更会话状态。
management.SelectedCategory = management.Categories.Single(category => category.Key == "models");
Assert(management.CodeUsesSelection && management.CodeOptions.SequenceEqual(["B11"]),
    "型号管理没有从车型管理会话记录形成车型下拉候选。");
management.DraftName = "B11-A";
management.DraftCode = "B11";
management.DraftDescription = "标准型号";
management.AddCommand.Execute(null);
Assert(management.CanPublishSelected, "合法型号记录未开放会话内发布。");
management.PublishSelectedCommand.Execute(null);
Assert(management.SelectedRecord!.IsPublished
       && !management.PublishSelectedCommand.CanExecute(null)
       && management.FeedbackText.Contains("未写入型号数据库", StringComparison.Ordinal),
    "型号发布未保持单向会话状态或外部写入边界。");
management.ResetCommand.Execute(null);
management.DraftName = "B11-DRAFT";
management.DraftCode = "B11";
management.DraftDescription = "未发布型号";
management.AddCommand.Execute(null);
Assert(!management.SelectedRecord!.IsPublished, "未执行发布命令的型号被错误标记为已发布。");

// 项点维护只记录启用状态；Legacy 动态源码的创建/移动/删除必须始终锁定。
management.SelectedCategory = management.Categories.Single(category => category.Key == "test-items");
Assert(management.CategoryBoundaryText.Contains("动态 .cs", StringComparison.Ordinal),
    "项点管理没有声明 Legacy 动态源码副作用已锁定。");
Assert(management.DescriptionUsesSelection && management.DescriptionOptions.SequenceEqual(["B11"]),
    "项点管理没有从车型管理会话记录形成车型下拉候选。");
foreach (var (itemName, className) in new[]
         {
             ("绝缘试验", "InsulationTest"),
             ("耐压试验", "WithstandVoltageTest")
         })
{
    management.DraftName = itemName;
    management.DraftCode = className;
    management.DraftDescription = "B11";
    management.EditorFields.Single(field => field.Key == "enabled").BooleanValue = true;
    management.AddCommand.Execute(null);
    management.ResetCommand.Execute(null);
}
Assert(management.Records.Count == 2 && management.Records.All(record => record.Values["enabled"] == bool.TrueString),
    "项点启用状态没有进入会话记录。");

// 项点配置的左右列表顺序是合同的一部分；移动只更新会话，不写外部数据库。
management.SelectedCategory = management.Categories.Single(category => category.Key == "item-config");
Assert(management.ShowDualList && management.NameUsesSelection && management.CodeUsesSelection
       && management.CodeOptions.SequenceEqual(["B11"]),
    "项点配置没有启用 Legacy 双列表或车型/型号选择行为。");
management.DraftCode = "B11";
Assert(management.AvailableOptions.Select(option => option.Name).SequenceEqual(["绝缘试验", "耐压试验"]),
    "项点配置候选没有从同车型的项点管理会话记录同步。");
foreach (var itemName in new[] { "绝缘试验", "耐压试验" })
{
    management.SelectedAvailableOption = management.AvailableOptions.Single(option => option.Name == itemName);
    management.MoveToConfiguredCommand.Execute(null);
}
Assert(management.NameOptions.SequenceEqual(["B11-A"]),
    "项点配置没有按车型过滤已发布型号，或错误包含未发布型号。");
management.DraftName = "B11-A";
management.AddCommand.Execute(null);
Assert(management.Records.Single().Selections.SequenceEqual(["绝缘试验", "耐压试验"]),
    "项点配置没有保留双列表移动顺序。");

// Legacy 的“参数界面”页签为空，不能伪造成可编辑能力；只迁移实际存在的模板和目录字段。
management.SelectedCategory = management.Categories.Single(category => category.Key == "test-params");
Assert(!management.ShowDescription
       && management.NameUsesSelection && management.CodeUsesSelection
       && !management.CategoryFields.Contains("参数界面", StringComparison.Ordinal)
       && management.EditorFields.Select(field => field.Key).SequenceEqual(["report-template", "report-save-path"]),
    "试验参数错误声称空白参数界面可编辑，或遗漏实际报表字段。");
management.DraftCode = "B11";
Assert(management.NameOptions.SequenceEqual(["B11-A"]),
    "试验参数没有按车型过滤已发布型号，或错误包含未发布型号。");
management.DraftName = "B11-A";
management.EditorFields.Single(field => field.Key == "report-template").Value = "B11.xlsx";
management.EditorFields.Single(field => field.Key == "report-save-path").Value = @"D:\Reports";
management.AddCommand.Execute(null);
Assert(management.Records.Single().Values["report-template"] == "B11.xlsx"
       && management.Records.Single().Values["report-save-path"] == @"D:\Reports"
       && management.CategoryBoundaryText.Contains("INI 写入保持锁定", StringComparison.Ordinal),
    "试验参数实际字段或文件/INI 锁定边界不完整。");

var today = DateTimeOffset.Now;
var reports = new ReportsViewModel(
[
    new ReportRecordViewModel(1, "B11", "B11-A", "PN-001", "C-01", "tester", today, "待上传", "report-1.pdf", 2),
    new ReportRecordViewModel(2, "EP", "EP-A", "PN-002", "C-02", "tester", today.AddMinutes(-1), "已上传", "report-2.pdf")
]);
reports.ProductNumberFilter = "001";
reports.QueryCommand.Execute(null);
Assert(reports.Records.Count == 1 && reports.Records[0].ProductNumber == "PN-001", "报表组合筛选行为错误。");
reports.SelectedRecord = reports.Records[0];
reports.NextPageCommand.Execute(null);
Assert(reports.CurrentPage == 2 && !reports.CanNextPage, "报表下翻边界错误。");
reports.PreviousPageCommand.Execute(null);
Assert(reports.CurrentPage == 1, "报表上翻行为错误。");
Assert(!reports.RetryUploadCommand.CanExecute(null) && !reports.DeleteRecordCommand.CanExecute(null),
    "持久化边界未完成时，报表重传或删除命令不应可执行。");

var calibration = viewModel.Calibration;
Assert(calibration.Outputs.Count == 6
       && calibration.Outputs.Select(output => output.Name).Distinct(StringComparer.Ordinal).Count() == 6
       && calibration.Outputs.All(output => !output.IsEnabled),
    "Legacy 六个 AO 输出槽位未被逐项保留并安全锁定。");
calibration.SelectedChannel!.EngineeringValue = 10;
calibration.SelectedChannel.Gain = 2;
calibration.SelectedChannel.Zero = 1;
calibration.RecalculateCommand.Execute(null);
Assert(calibration.SelectedChannel.CalculatedValue == 19, "校准本地公式未按 Legacy 合同计算。");
Assert(!calibration.ApplyCalibrationCommand.CanExecute(null), "校准应用命令不应在只读阶段可用。");

// Diagnostics 独立行为样本：同时覆盖 Legacy 日期/等级语义与 Avalonia 关键字增强。
using var diagnostics = new DiagnosticsViewModel(log: null);
Assert(diagnostics.Levels.Select(level => level.DisplayName)
        .SequenceEqual(["全部等级", "跟踪", "调试", "信息", "警告", "错误", "致命"])
       && diagnostics.Levels.Select(level => level.Value)
           .SequenceEqual(["All", "Trace", "Debug", "Info", "Warn", "Error", "Fatal"]),
    "日志等级的中文显示或 Legacy 内部值不完整。");

var localDayStart = new DateTimeOffset(DateTime.Today);
var localDayEnd = localDayStart.AddDays(1).AddTicks(-1);
diagnostics.AddEntry(new GatewayLogEntry(
    localDayStart,
    GatewayLogLevel.Information,
    "ReadOnlyRuntime",
    "gateway-information",
    new Dictionary<string, string?>
    {
        ["UserName"] = "operator-a",
        ["MessageName"] = "mapped-operation",
        ["Source"] = "Legacy.Logger"
    }));
diagnostics.AddEntry(new GatewayLogEntry(
    localDayEnd,
    GatewayLogLevel.Warning,
    "ReadOnlyRuntime",
    "gateway-warning",
    new Dictionary<string, string?>
    {
        // 适配器键名大小写不应影响 Legacy 三字段的显示。
        ["username"] = "operator-b",
        ["messagename"] = "day-end-operation",
        ["source"] = "Gateway.Adapter"
    }));
diagnostics.AddEntry(new GatewayLogEntry(
    localDayStart.AddDays(1),
    GatewayLogLevel.Error,
    "ReadOnlyRuntime",
    "next-day-error",
    new Dictionary<string, string?>
    {
        ["UserName"] = " ",
        ["MessageName"] = null
    }));

Assert(diagnostics.Entries.Count == 2
       && diagnostics.Entries[0].Message == "gateway-warning"
       && diagnostics.Entries[1].Message == "gateway-information",
    "[day, next day) 日期边界或日志降序行为错误。");
var mappedEntry = diagnostics.Entries.Single(entry => entry.Message == "gateway-information");
Assert(mappedEntry.Level == "Info" && mappedEntry.LevelDisplayName == "信息"
       && mappedEntry.UserName == "operator-a"
       && mappedEntry.OperationInformation == "mapped-operation"
       && mappedEntry.Source == "Legacy.Logger",
    "Gateway 别名或 Legacy Properties 字段映射错误。");

diagnostics.SelectedDate = localDayStart.AddDays(1);
Assert(diagnostics.Entries.Count == 2,
    "日志日期草稿被错误地提前应用，Legacy 搜索按钮语义已被破坏。");
diagnostics.FilterCommand.Execute(null);
Assert(diagnostics.Entries.Count == 1 && diagnostics.Entries[0].Message == "next-day-error"
       && diagnostics.Entries[0].UserName == "—"
       && diagnostics.Entries[0].OperationInformation == "—"
       && diagnostics.Entries[0].Source == "—",
    "搜索未应用日期，或缺失的 Legacy 日志字段被伪造。");

diagnostics.SelectedDate = localDayStart;
diagnostics.FilterCommand.Execute(null);
diagnostics.SelectedLevel = diagnostics.Levels.Single(level => level.Value == "Warn");
Assert(diagnostics.Entries.Count == 1
       && diagnostics.Entries[0].Level == "Warn"
       && diagnostics.Entries[0].LevelDisplayName == "警告",
    "Warning → Warn 别名或等级变化即时筛选未生效。");
diagnostics.SelectedLevel = diagnostics.Levels.Single(level => level.Value == "Info");
Assert(diagnostics.Entries.Count == 1 && diagnostics.Entries[0].Level == "Info",
    "Information → Info 别名筛选未生效。");
diagnostics.SelectedLevel = diagnostics.Levels.Single(level => level.Value == "All");
Assert(diagnostics.Entries.Count == 2, "All 未按 Legacy 语义取消等级限制。");
diagnostics.SearchText = "mapped-operation";
diagnostics.FilterCommand.Execute(null);
Assert(diagnostics.Entries.Count == 1 && diagnostics.Entries[0].UserName == "operator-a",
    "Avalonia 关键字增强未检索 Properties 映射字段。");

using var boundedDiagnostics = new DiagnosticsViewModel(log: null);
for (var index = 0; index <= 500; index++)
{
    boundedDiagnostics.AddEntry(new GatewayLogEntry(
        localDayStart.AddSeconds(index),
        GatewayLogLevel.Information,
        "BoundedRuntime",
        $"bounded-{index}",
        new Dictionary<string, string?>()));
}
Assert(boundedDiagnostics.Entries.Count == 500
       && boundedDiagnostics.ResultText == "显示 500 / 500 条"
       && boundedDiagnostics.Entries[0].Message == "bounded-500"
       && boundedDiagnostics.Entries.All(entry => entry.Message != "bounded-0"),
    "日志会话缓存未保持最近 500 条上限。");

Assert(diagnostics.MaintenanceFeatures.Count == 4
       && diagnostics.MaintenanceFeatures.Where(feature => feature.Name is "设备检查" or "维保计量" or "问题统计")
           .All(feature => !feature.IsEnabled),
    "Legacy 项目级隐藏维护能力被错误启用。");

viewModel.Instrument.IpAddress = "not-an-ip";
viewModel.Instrument.ValidateDraftCommand.Execute(null);
Assert(viewModel.Instrument.FeedbackText.Contains("无效", StringComparison.Ordinal), "仪器端点负向校验未生效。");
viewModel.Instrument.IpAddress = "192.168.1.199";
viewModel.Instrument.ValidateDraftCommand.Execute(null);
Assert(viewModel.Instrument.FeedbackText.Contains("通过", StringComparison.Ordinal), "仪器参数正向校验未生效。");
Assert(new[]
    {
        viewModel.Instrument.ConnectCommand,
        viewModel.Instrument.SendParametersCommand,
        viewModel.Instrument.RequestTestCommand,
        viewModel.Instrument.EndTestCommand,
        viewModel.Instrument.CancelTestCommand
    }.All(command => !command.CanExecute(null)),
    "绝缘仪器外部命令不应在适配器缺失时可用。");

using var startupCancellation = new CancellationTokenSource();
startupCancellation.Cancel();
using var cancelledStartup = new MainWindowViewModel(
    new TestBenchStateMachine(),
    runtime,
    log: null,
    configPath: "offline-test",
    transportMode: "OfflineSimulation");
await cancelledStartup.StartAsync(startupCancellation.Token);
Assert(cancelledStartup.StatusText == "启动中"
       && cancelledStartup.FeedbackText.Contains("取消", StringComparison.Ordinal),
    "启动轮询取消后仍错误推进了 Core 状态。");

using var failedStartup = new MainWindowViewModel(
    new TestBenchStateMachine(),
    runtime: null,
    log: null,
    configPath: "missing",
    transportMode: "Unavailable",
    startupError: "测试配置失败");
await failedStartup.StartAsync();
Assert(failedStartup.StatusText == "故障" && failedStartup.FeedbackText == "测试配置失败",
    "配置失败时 UI 没有进入可审计 Faulted 状态。");

Console.WriteLine("PASS avalonia-vm navigation=8 product=test-items management=state reports=filter+paging calibration=6-outputs-local-only logs=filter instrument=gated startup=cancel+faulted writes=disabled");
