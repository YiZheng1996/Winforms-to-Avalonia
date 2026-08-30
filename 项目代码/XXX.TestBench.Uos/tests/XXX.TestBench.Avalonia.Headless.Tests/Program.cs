using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using XXX.TestBench.Avalonia;
using XXX.TestBench.Avalonia.ViewModels;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Gateway.Domain;
using XXX.TestBench.Gateway.Infrastructure;

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

using var session = HeadlessUnitTestSession.StartNew(typeof(XXX.TestBench.Avalonia.App));
await session.Dispatch(async () =>
{
    var settings = new GatewaySettingsDocument();
    var runtime = new ReadOnlyGatewayRuntime(
        settings.Gateway,
        new SimulatedS7ReadOnlyTransport(),
        new SimulatedModbusReadOnlyTransport(),
        isSimulated: true);
    try
    {
        using var viewModel = new MainWindowViewModel(
            new TestBenchStateMachine(),
            runtime,
            log: null,
            configPath: "headless-test",
            transportMode: "OfflineSimulation");

        await viewModel.StartAsync();
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        T? FindNamed<T>(string name) where T : Control =>
            window.GetVisualDescendants().OfType<T>().FirstOrDefault(control => control.Name == name);

        var input = FindNamed<TextBox>("ProductIdInput");
        var login = FindNamed<Button>("LoginButton");
        var refresh = FindNamed<Button>("RefreshButton");
        var navigation = FindNamed<ListBox>("NavigationList");
        Assert(input is not null && login is not null && refresh is not null, "主窗口关键控件未加载。");
        Assert(window.MinWidth == 960 && window.MinHeight == 560, "主窗口最小尺寸合同错误。");
        Assert(input!.Text == "B11-Offline-Demo", "产品输入框初始绑定错误。");
        Assert(AutomationProperties.GetName(input) == "产品标识输入框", "产品输入框无可访问名称。");
        Assert(login!.IsEnabled, "离线仿真登录按钮未启用。");
        Assert(refresh!.IsEnabled, "首轮刷新完成后刷新命令没有恢复。");
        Assert(navigation is not null && navigation.ItemCount == 8, "完整功能导航未加载。");
        Assert(AutomationProperties.GetName(navigation!) == "主功能导航", "主导航无可访问名称。");
        Assert(window.GetVisualDescendants().OfType<TextBlock>().Any(x => x.Text == "1"), "连接代次没有通过实际绑定显示。");

        input.Text = "B11-Headless";
        Assert(viewModel.ProductId == "B11-Headless", "产品输入框双向绑定未生效。");
        login.Command?.Execute(null);
        Assert(viewModel.StatusText == "就绪", "Headless 登录命令未驱动 Core 状态。");
        var overviewStartButton = window.GetVisualDescendants().OfType<Button>().SingleOrDefault(button =>
            AutomationProperties.GetName(button) == "启动自动试验");
        Assert(!viewModel.CanStartAutomaticTest
               && overviewStartButton is not null
               && !overviewStartButton.IsEnabled,
            "Test00 未接入时 Headless UI 错误开放自动试验。");

        // 先通过真实 Headless 键盘事件逐个验证 Ctrl+1～Ctrl+8，再用 ListBox 选择
        // 覆盖鼠标/触控共用的导航状态路径；两者最终必须落到同一个 ViewModel 状态。
        var shortcutKeys = new[]
        {
            PhysicalKey.Digit1, PhysicalKey.Digit2, PhysicalKey.Digit3, PhysicalKey.Digit4,
            PhysicalKey.Digit5, PhysicalKey.Digit6, PhysicalKey.Digit7, PhysicalKey.Digit8
        };
        for (var index = 0; index < shortcutKeys.Length; index++)
        {
            window.KeyPressQwerty(shortcutKeys[index], RawInputModifiers.Control);
            Dispatcher.UIThread.RunJobs();
            Assert(viewModel.SelectedNavigationItem == viewModel.NavigationItems[index],
                $"Ctrl+{index + 1} 未切换到预期任务页面。");
        }

        navigation!.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        Assert(viewModel.IsTestOperationVisible, "导航选择未切换试验作业页面。");
        var operationStartButton = window.GetVisualDescendants().OfType<Button>().SingleOrDefault(button =>
            AutomationProperties.GetName(button) == "试验作业开始试验");
        Assert(operationStartButton is not null && !operationStartButton.IsEnabled,
            "试验作业页的真实开始按钮没有执行 Test00/项点双重门禁。");
        Assert(window.GetVisualDescendants().OfType<TextBlock>().Any(text =>
                text.Text?.Contains("调压试验与阀升程调整对话框已折叠", StringComparison.Ordinal) == true),
            "Headless 未呈现 Legacy 两个人工调整对话框的折叠提示与安全边界。");
        var productNumber = FindNamed<TextBox>("TestProductNumberInput");
        var vehicleNumber = FindNamed<TextBox>("VehicleNumberInput");
        var submitProduct = FindNamed<Button>("SubmitProductButton");
        Assert(productNumber is not null && vehicleNumber is not null && submitProduct is not null,
            "试验作业产品输入控件未加载。");
        Assert(AutomationProperties.GetName(productNumber!) == "产品编号输入框", "产品编号缺少可访问名称。");
        productNumber!.Text = "B11-HEADLESS-001";
        vehicleNumber!.Text = "CAR-H-01";
        Dispatcher.UIThread.RunJobs();
        Assert(viewModel.TestOperation.ProductNumber == "B11-HEADLESS-001", "产品编号双向绑定未生效。");
        Assert(submitProduct!.IsEnabled, "完整产品信息未使提交按钮可用。");
        submitProduct.Command?.Execute(null);
        Assert(viewModel.ProductId == "B11-HEADLESS-001", "产品提交没有进入同一 Core 状态。");

        navigation.SelectedIndex = 2;
        Dispatcher.UIThread.RunJobs();
        // 固定清单来自 Legacy 工艺页，不能从被测 ViewModel 反向生成期望值，否则删除输出时测试也会同步缩小。
        var processOutputNames = Enumerable.Range(1, 12)
            .Select(index => $"VX{index:00}")
            .Concat(["36V 供电", "160V 供电", "耐压合闸", "电阻合闸", "故障复位"])
            .ToHashSet(StringComparer.Ordinal);
        var processOutputButtons = window.GetVisualDescendants().OfType<Button>()
            .Where(button => AutomationProperties.GetName(button) is { } automationName
                             && processOutputNames.Contains(automationName))
            .ToArray();
        Assert(processOutputButtons.Length == processOutputNames.Count
               && processOutputButtons.All(button => !button.IsEnabled),
            "Headless 工艺输出集合未完整呈现，或仍存在可执行的 VX/DO/复位按钮。");
        Assert(window.GetVisualDescendants().OfType<TextBlock>().Any(text =>
                text.Text?.Contains("手动数值输出均不可操作", StringComparison.Ordinal) == true),
            "Headless 手动数值输出安全边界未呈现，或仍暗示可执行写入。");

        navigation.SelectedIndex = 3;
        Dispatcher.UIThread.RunJobs();
        var managementCategories = FindNamed<ListBox>("ManagementCategoryList");
        var managementName = FindNamed<TextBox>("ManagementNameInput");
        var managementNameSelector = FindNamed<ComboBox>("ManagementNameSelector");
        var managementCode = FindNamed<TextBox>("ManagementCodeInput");
        var managementCodeSelector = FindNamed<ComboBox>("ManagementCodeSelector");
        var managementDescription = FindNamed<TextBox>("ManagementDescriptionInput");
        var managementDescriptionSelector = FindNamed<ComboBox>("ManagementDescriptionSelector");
        var managementAdd = FindNamed<Button>("ManagementAddButton");
        Assert(managementCategories?.ItemCount == 9
               && managementName is not null
               && managementNameSelector is not null
               && managementCode is not null
               && managementCodeSelector is not null
               && managementDescription is not null
               && managementDescriptionSelector is not null
               && managementAdd is not null,
            "AH-MGMT 九类管理页面 Headless 交互证据缺失。");

        // 先建立角色记录；用户和权限分配的下拉候选只能来自这里，不能自由输入外部库不存在的角色。
        managementCategories!.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();
        managementName!.Text = "操作员";
        managementDescription!.Text = "Headless 会话角色";
        Dispatcher.UIThread.RunJobs();
        Assert(managementAdd!.IsEnabled, "角色名称和描述完整后新增命令未启用。");
        managementAdd.Command?.Execute(null);

        managementCategories.SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();
        Assert(AutomationProperties.GetName(managementName) == "用户名"
               && AutomationProperties.GetName(managementCodeSelector!) == "角色"
               && managementCodeSelector!.ItemCount == 1,
            "用户管理没有通过角色下拉候选呈现专属字段。");
        managementName.Text = "headless-user";
        Dispatcher.UIThread.RunJobs();
        Assert(!managementAdd.IsEnabled, "用户管理错误允许未选择角色的记录。");
        managementCodeSelector!.SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();
        Assert(managementAdd.IsEnabled, "用户名和角色完整后仍未启用会话新增命令。");
        managementAdd.Command?.Execute(null);
        Assert(viewModel.Management.Records.Count == 1, "参数管理按钮未驱动 ViewModel 集合。");

        // 权限候选先由权限管理创建，再由权限分配页同步；分配页不提供凭空造权限的入口。
        managementCategories.SelectedIndex = 2;
        Dispatcher.UIThread.RunJobs();
        var controlName = window.GetVisualDescendants().OfType<TextBox>()
            .FirstOrDefault(textBox => AutomationProperties.GetName(textBox) == "控件名称");
        Assert(controlName is not null, "权限管理控件名称输入框未加载。");
        managementName.Text = "查看报表";
        managementCode!.Text = "REPORT_VIEW";
        controlName!.Text = "btnReport";
        Dispatcher.UIThread.RunJobs();
        Assert(managementAdd.IsEnabled, "权限名称、代码和控件名称完整后新增命令未启用。");
        managementAdd.Command?.Execute(null);

        managementCategories.SelectedIndex = 3;
        Dispatcher.UIThread.RunJobs();
        var permissionCheckBox = window.GetVisualDescendants().OfType<CheckBox>()
            .FirstOrDefault(checkBox => AutomationProperties.GetName(checkBox) == "查看报表");
        Assert(permissionCheckBox is not null
               && managementNameSelector!.ItemCount == 1
               && viewModel.Management.CategoryBoundaryText.Contains("不能授予", StringComparison.Ordinal),
            "权限分配没有同步角色/权限候选或显示真实授权锁定说明。");
        managementNameSelector!.SelectedIndex = 0;
        permissionCheckBox!.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert(managementAdd.IsEnabled, "角色和权限完整后仍未启用会话分配命令。");

        // 车型记录是后续型号、项点、配置和参数页的统一下拉来源。
        managementCategories.SelectedIndex = 4;
        Dispatcher.UIThread.RunJobs();
        managementName.Text = "B11";
        managementDescription.Text = "Headless 车型";
        Dispatcher.UIThread.RunJobs();
        Assert(managementAdd.IsEnabled, "车型名称未启用会话新增命令。");
        managementAdd.Command?.Execute(null);

        // 型号发布按钮只改变 ViewModel 会话状态，第二次发布必须由命令自身拒绝。
        managementCategories.SelectedIndex = 5;
        Dispatcher.UIThread.RunJobs();
        managementName.Text = "B11-A";
        Assert(managementCodeSelector.ItemCount == 1, "型号管理没有同步车型下拉候选。");
        managementCodeSelector.SelectedIndex = 0;
        managementDescription.Text = "Headless 型号";
        Dispatcher.UIThread.RunJobs();
        Assert(managementAdd.IsEnabled, "车型和型号完整后新增命令未启用。");
        managementAdd.Command?.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var publishModel = FindNamed<Button>("ManagementPublishButton");
        Assert(publishModel is not null && publishModel.IsVisible && publishModel.IsEnabled,
            "型号新增后未开放会话内发布按钮。");
        publishModel!.Command?.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert(viewModel.Management.SelectedRecord!.IsPublished && !publishModel.IsEnabled,
            "型号会话发布没有形成单向状态。");

        // 项点页显示启用字段，但动态 .cs 文件副作用没有任何可执行控件。
        managementCategories.SelectedIndex = 6;
        Dispatcher.UIThread.RunJobs();
        var enabledField = window.GetVisualDescendants().OfType<CheckBox>()
            .FirstOrDefault(checkBox => AutomationProperties.GetName(checkBox) == "启用状态");
        Assert(enabledField is not null && managementDescriptionSelector!.ItemCount == 1
               && window.GetVisualDescendants().OfType<TextBlock>().Any(text =>
                   text.Text?.Contains("动态 .cs 源码文件", StringComparison.Ordinal) == true),
            "项点启用字段或动态源码锁定说明未通过管理页呈现。");
        managementName.Text = "绝缘试验";
        managementCode.Text = "InsulationTest";
        managementDescriptionSelector!.SelectedIndex = 0;
        enabledField!.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert(managementAdd.IsEnabled, "项点专属字段完整后新增命令未启用。");
        managementAdd.Command?.Execute(null);

        // 双列表候选来自同车型的项点管理记录，通过选择和移动按钮保留配置顺序。
        managementCategories.SelectedIndex = 7;
        Dispatcher.UIThread.RunJobs();
        var availableList = FindNamed<ListBox>("ManagementAvailableList");
        var configuredList = FindNamed<ListBox>("ManagementConfiguredList");
        var moveToConfigured = FindNamed<Button>("ManagementMoveToConfiguredButton");
        var moveToAvailable = FindNamed<Button>("ManagementMoveToAvailableButton");
        Assert(availableList is not null && configuredList is not null && moveToConfigured is not null
               && moveToAvailable is not null
               && managementCodeSelector.ItemCount == 1,
            "项点配置双列表控件未加载。");
        var configuredPanel = configuredList!.Parent as StackPanel;
        var availablePanel = availableList!.Parent as StackPanel;
        Assert(configuredPanel is not null && availablePanel is not null
               && Grid.GetColumn(configuredPanel) == 0
               && Grid.GetColumn(availablePanel) == 2
               && Equals(moveToConfigured!.Content, "←")
               && Equals(moveToAvailable!.Content, "→"),
            "项点配置双列表未保持 Legacy 左已配置、右候选及对应箭头方向。");
        managementCodeSelector.SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();
        Assert(managementNameSelector.ItemCount == 1 && availableList!.ItemCount == 1,
            "项点配置没有按车型同步型号和项点候选。");
        managementNameSelector.SelectedIndex = 0;
        availableList!.SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();
        Assert(moveToConfigured!.IsEnabled, "选中候选项点后移动命令未启用。");
        moveToConfigured.Command?.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert(availableList.ItemCount == 0 && configuredList!.ItemCount == 1,
            "项点没有从候选列表移动到已配置列表。");
        Dispatcher.UIThread.RunJobs();
        Assert(managementAdd.IsEnabled, "车型、型号和已配置项点完整后新增命令未启用。");
        managementAdd.Command?.Execute(null);
        Assert(viewModel.Management.Records.Single().Selections.SequenceEqual(["绝缘试验"]),
            "Headless 双列表移动顺序没有进入会话记录。");

        // Legacy 空白“参数界面”不呈现为输入框；实际模板文件名与保存目录可编辑但不写 INI。
        managementCategories.SelectedIndex = 8;
        Dispatcher.UIThread.RunJobs();
        var reportTemplate = window.GetVisualDescendants().OfType<TextBox>()
            .FirstOrDefault(textBox => AutomationProperties.GetName(textBox) == "报表模板文件名");
        var reportSavePath = window.GetVisualDescendants().OfType<TextBox>()
            .FirstOrDefault(textBox => AutomationProperties.GetName(textBox) == "报表保存目录");
        Assert(reportTemplate is not null && reportSavePath is not null
               && !viewModel.Management.ShowDescription
               && managementCodeSelector.ItemCount == 1
               && !viewModel.Management.CategoryFields.Contains("参数界面", StringComparison.Ordinal),
            "试验参数实际字段或空白参数界面边界未正确呈现。");
        managementCodeSelector.SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();
        Assert(managementNameSelector.ItemCount == 1, "试验参数没有按车型同步型号候选。");
        managementNameSelector.SelectedIndex = 0;
        reportTemplate!.Text = "B11.xlsx";
        reportSavePath!.Text = @"D:\Reports";
        Dispatcher.UIThread.RunJobs();
        Assert(managementAdd.IsEnabled, "试验参数实际字段完整后新增命令未启用。");
        managementAdd.Command?.Execute(null);
        Assert(viewModel.Management.Records.Single().Values["report-template"] == "B11.xlsx"
               && viewModel.Management.CategoryBoundaryText.Contains("INI 写入保持锁定", StringComparison.Ordinal),
            "试验参数控件没有驱动会话记录或锁定外部写入。");

        navigation.SelectedIndex = 4;
        Dispatcher.UIThread.RunJobs();
        var reportQuery = FindNamed<Button>("ReportQueryButton");
        Assert(reportQuery is not null && reportQuery.IsEnabled, "数据查询入口不可达。");
        Assert(AutomationProperties.GetName(reportQuery!) == "搜索试验记录", "报表查询入口无可访问名称。");
        reportQuery!.Command?.Execute(null);
        Assert(viewModel.Reports.Records.Count == 0, "未接入持久化时不应伪造历史记录。");
        foreach (var actionName in new[] { "打印报表", "重新上传报表", "删除试验记录" })
        {
            var blockedReportAction = window.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(button => AutomationProperties.GetName(button) == actionName);
            Assert(blockedReportAction is not null && !blockedReportAction.IsEnabled,
                $"报表外部动作“{actionName}”未保持禁用。");
        }

        navigation.SelectedIndex = 5;
        Dispatcher.UIThread.RunJobs();
        var calibrationApply = FindNamed<Button>("CalibrationApplyButton");
        Assert(calibrationApply is not null && !calibrationApply.IsEnabled, "校准写入按钮未安全锁定。");
        var calibrationOutputNames = viewModel.Calibration.Outputs.Select(output => output.Name).ToHashSet(StringComparer.Ordinal);
        Assert(calibrationOutputNames.Count == 6
               && calibrationOutputNames.All(name => window.GetVisualDescendants().OfType<TextBlock>()
                   .Any(text => string.Equals(text.Text, name, StringComparison.Ordinal))),
            "校准页未完整呈现 Legacy 六个 AO 输出槽位。");

        navigation.SelectedIndex = 6;
        Dispatcher.UIThread.RunJobs();
        var logFilter = FindNamed<Button>("LogFilterButton");
        var logDate = FindNamed<DatePicker>("LogDatePicker");
        var logLevel = FindNamed<ComboBox>("LogLevelComboBox");
        var logKeyword = FindNamed<TextBox>("LogKeywordInput");
        var logList = FindNamed<ListBox>("LogList");
        Assert(logFilter is not null && logFilter.IsEnabled
               && logDate is not null && logLevel is not null && logKeyword is not null && logList is not null,
            "日志日期、等级、关键字、搜索或列表控件不可达。");
        Assert(logDate!.SelectedDate?.Date == DateTimeOffset.Now.Date, "日志日期未按 Legacy 默认到今天。");
        Assert(logLevel!.ItemCount == 7
               && logLevel.SelectedItem is LogLevelOptionViewModel { DisplayName: "全部等级", Value: "All" },
            "日志等级下拉框未显示完整的中文 Legacy 选项。");
        Assert(AutomationProperties.GetName(logDate) == "日志记录日期"
               && AutomationProperties.GetName(logLevel) == "日志等级"
               && AutomationProperties.GetName(logKeyword!) == "日志关键字"
               && AutomationProperties.GetName(logList!) == "运行日志列表",
            "日志查询控件缺少稳定的可访问名称。");

        var headlessDayStart = new DateTimeOffset(DateTime.Today);
        viewModel.Diagnostics.AddEntry(new GatewayLogEntry(
            headlessDayStart.AddHours(8),
            GatewayLogLevel.Information,
            "HeadlessRuntime",
            "headless-information",
            new Dictionary<string, string?>
            {
                ["UserName"] = "headless-user",
                ["MessageName"] = "headless-operation",
                ["Source"] = "Headless.Source"
            }));
        viewModel.Diagnostics.AddEntry(new GatewayLogEntry(
            headlessDayStart.AddHours(9),
            GatewayLogLevel.Error,
            "HeadlessRuntime",
            "headless-error",
            new Dictionary<string, string?>()));
        viewModel.Diagnostics.AddEntry(new GatewayLogEntry(
            headlessDayStart.AddDays(1).AddHours(8),
            GatewayLogLevel.Warning,
            "HeadlessRuntime",
            "headless-next-day",
            new Dictionary<string, string?>()));
        Dispatcher.UIThread.RunJobs();
        Assert(logList!.ItemCount == 2 && viewModel.Diagnostics.Entries[0].Message == "headless-error",
            "Headless 列表未按今日边界和时间降序显示。");

        // 真实改动 DatePicker 后结果应保持不变；聚焦按钮并发送空格键，
        // 覆盖控件点击 -> ICommand -> 已应用日期的完整绑定链。
        logDate.SelectedDate = headlessDayStart.AddDays(1);
        Dispatcher.UIThread.RunJobs();
        Assert(viewModel.Diagnostics.SelectedDate?.Date == headlessDayStart.AddDays(1).Date
               && logList.ItemCount == 2,
            "Headless DatePicker 变更提前触发了日期筛选。");
        logFilter!.Focus();
        Assert(logFilter.IsFocused, "Headless 日志搜索按钮无法获取键盘焦点。");
        window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert(logList.ItemCount == 1 && viewModel.Diagnostics.Entries[0].Message == "headless-next-day",
            "Headless 搜索按钮未通过真实键盘交互应用日期。");

        logLevel.SelectedItem = viewModel.Diagnostics.Levels.Single(level => level.Value == "Info");
        Dispatcher.UIThread.RunJobs();
        Assert(logList.ItemCount == 0 && viewModel.Diagnostics.SelectedLevel.Value == "Info",
            "Headless 日志等级选择未即时筛选已应用日期。");
        logLevel.SelectedItem = viewModel.Diagnostics.Levels.Single(level => level.Value == "All");
        logDate.SelectedDate = headlessDayStart;
        Dispatcher.UIThread.RunJobs();
        Assert(logList.ItemCount == 1, "Headless 日期草稿在搜索前错误改变了结果。");
        logFilter.Focus();
        window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert(logList.ItemCount == 2, "Headless 搜索未恢复今日的全部等级日志。");

        logKeyword!.Text = "headless-operation";
        Dispatcher.UIThread.RunJobs();
        logFilter.Focus();
        window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert(logList.ItemCount == 1
               && viewModel.Diagnostics.Entries[0].UserName == "headless-user"
               && viewModel.Diagnostics.Entries[0].OperationInformation == "headless-operation"
               && viewModel.Diagnostics.Entries[0].Source == "Headless.Source",
            "Headless 关键字控件未驱动 Properties 日志字段筛选与显示。");

        foreach (var maintenanceName in new[] { "修改密码", "设备检查", "维保计量", "问题统计" })
        {
            var blockedMaintenance = window.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(button => AutomationProperties.GetName(button) == maintenanceName);
            Assert(blockedMaintenance is not null && !blockedMaintenance.IsEnabled,
                $"Legacy 维护入口“{maintenanceName}”被错误启用。");
        }

        navigation.SelectedIndex = 7;
        Dispatcher.UIThread.RunJobs();
        var validateInstrument = FindNamed<Button>("InstrumentValidateButton");
        Assert(validateInstrument is not null && validateInstrument.IsEnabled, "仪器参数校验入口不可达。");
        Assert(validateInstrument!.MinHeight >= 44, "通用按钮未满足 44 高度触控目标。");
        var instrumentNumbers = window.GetVisualDescendants().OfType<NumericUpDown>().ToArray();
        Assert(instrumentNumbers.Length >= 7
               && instrumentNumbers.All(inputControl => !string.IsNullOrWhiteSpace(AutomationProperties.GetName(inputControl))),
            "仪器数值输入缺少可访问名称。");
        var connectInstrument = window.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(button => AutomationProperties.GetName(button) == "连接绝缘仪器");
        Assert(connectInstrument is not null && !connectInstrument.IsEnabled, "仪器连接命令未在适配器缺失时禁用。");
        var instrumentActionNames = new[] { "发送绝缘参数", "请求绝缘试验", "结束绝缘试验", "取消绝缘试验", "请求电阻测试" };
        foreach (var actionName in instrumentActionNames)
        {
            var blockedInstrumentAction = window.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(button => AutomationProperties.GetName(button) == actionName);
            Assert(blockedInstrumentAction is not null && !blockedInstrumentAction.IsEnabled,
                $"仪器外部动作“{actionName}”未保持禁用。");
        }
        Assert(processOutputButtons.All(button => !button.IsEnabled)
               && instrumentActionNames.All(name => window.GetVisualDescendants().OfType<Button>()
                   .Any(button => AutomationProperties.GetName(button) == name && !button.IsEnabled)),
            "工艺输出与仪器外部动作未共同保持安全禁用。");

        window.Width = 960;
        window.Height = 560;
        Dispatcher.UIThread.RunJobs();
        Assert(window.GetVisualDescendants().OfType<ScrollViewer>().Any(), "最小尺寸下缺少滚动容器。");
        window.Width = 1600;
        window.Height = 900;
        Dispatcher.UIThread.RunJobs();
        Assert(window.Bounds.Width >= 960 && window.Bounds.Height >= 560, "宽屏/最小尺寸切换破坏窗口约束。");

        window.Close();
    }
    finally
    {
        await runtime.DisposeAsync();
    }
}, CancellationToken.None);

Console.WriteLine("PASS avalonia-headless navigation=8 shortcuts=ctrl1-ctrl8 binding=two-way command=core-gated pages=test+process+management+reports+calibration+diagnostics+instrument accessibility=names+touch44 layout=narrow+wide writes=disabled");
