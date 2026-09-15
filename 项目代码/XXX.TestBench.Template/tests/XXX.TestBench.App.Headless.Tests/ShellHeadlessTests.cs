using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using XXX.TestBench.App;
using XXX.TestBench.App.Composition;
using XXX.TestBench.App.ViewModels;
using XXX.TestBench.App.Views;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Devices.Drivers;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(XXX.TestBench.App.Headless.Tests.HeadlessTestAppBuilder))]
[assembly: AvaloniaTestIsolation(AvaloniaTestIsolationLevel.PerAssembly)]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace XXX.TestBench.App.Headless.Tests;

public static class HeadlessTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<HeadlessTestApp>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>
/// Headless 测试只加载生产资源，不启动生产组合根；保留一个主窗口维持桌面生命周期，
/// 用例自行创建临时目录下的组合根和被测窗口。
/// </summary>
public sealed class HeadlessTestApp : App
{
    protected override bool EnableProductionStartup => false;

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.MainWindow = new Window { Width = 1, Height = 1 };
            desktop.MainWindow.Show();
        }
        base.OnFrameworkInitializationCompleted();
    }
}

public class ShellHeadlessTests
{
    [AvaloniaFact]
    public void DeviceEditorWizard_RendersEachStepAndMovesToConfirmation()
    {
        var channel = new ChannelEntry
        {
            Id = "10000000-0000-0000-0000-000000000001",
            Code = "CH_SIM",
            Name = "仿真通道",
            // Simulation 仅保留为旧配置兼容字段；新设备必须挂到真实传输通道，
            // 仿真模式通过 DeviceMode 控制，不再把 Simulation 当作物理承载通道。
            TransportKind = ChannelTransportKind.Tcp,
            TimeoutMs = 1000,
            RetryCount = 0,
            Tcp = new TcpChannelParameters { Host = "127.0.0.1", Port = 1502 }
        };
        var form = new DeviceEditorViewModel(
            null,
            [channel],
            descriptors: null,
            defaultCode: "DEV_HEADLESS",
            defaultChannelId: channel.Id)
        {
            Name = "仿真设备"
        };
        var dialog = new DeviceEditorDialogWindow { DataContext = form };

        dialog.Show();
        dialog.UpdateLayout();
        Assert.Equal(DeviceMode.Simulation, form.SelectedDeviceMode?.Value);
        Assert.DoesNotContain(
            form.DriverOptions,
            option => string.Equals(option.DriverKey, DriverKeyCatalog.Simulation, StringComparison.OrdinalIgnoreCase));
        var stepOneTexts = dialog.GetVisualDescendants().OfType<TextBlock>()
            .Select(text => text.Text?.ToString())
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray();
        Assert.Contains(stepOneTexts, text => string.Equals(text, "运行模式", StringComparison.Ordinal));
        Assert.DoesNotContain(stepOneTexts, text => text?.Contains("设备编码", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(stepOneTexts, text => text?.Contains("创建后启用", StringComparison.Ordinal) == true);
        SaveDeviceWizardFrame(dialog, "device-wizard-step1.png");
        Assert.Contains(
            dialog.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text?.ToString()),
            text => string.Equals(text, "基本信息", StringComparison.Ordinal));

        Assert.True(form.MoveNext(), form.ValidationMessage);
        dialog.UpdateLayout();
        SaveDeviceWizardFrame(dialog, "device-wizard-step2.png");
        Assert.Contains(
            dialog.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text?.ToString()),
            text => string.Equals(text, "通信参数", StringComparison.Ordinal));

        Assert.True(form.MoveNext(), form.ValidationMessage);
        dialog.UpdateLayout();
        SaveDeviceWizardFrame(dialog, "device-wizard-step3.png");
        Assert.Contains(
            dialog.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text?.ToString()),
            text => string.Equals(text, "确认保存", StringComparison.Ordinal));

        dialog.Close();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
    }

    [AvaloniaFact]
    public void DeviceEditorWizard_ShowsModbusStationBeforeSimulationNote()
    {
        var channel = new ChannelEntry
        {
            Id = "10000000-0000-0000-0000-000000000002",
            Code = "CH_MODBUS",
            Name = "Modbus TCP 通道",
            TransportKind = ChannelTransportKind.Tcp,
            TimeoutMs = 1000,
            RetryCount = 0,
            Tcp = new TcpChannelParameters { Host = "127.0.0.1", Port = 1502 }
        };
        var form = new DeviceEditorViewModel(
            null,
            [channel],
            [new ModbusTcpDriverDescriptor()],
            defaultCode: "DEV_MODBUS_HEADLESS",
            defaultChannelId: channel.Id)
        {
            Name = "Modbus 仿真设备"
        };
        form.SelectedDriver = form.DriverOptions.Single(option =>
            string.Equals(option.DriverKey, DriverKeyCatalog.ModbusTcp, StringComparison.OrdinalIgnoreCase));
        form.ModbusUnitIdText = "1";
        var dialog = new DeviceEditorDialogWindow { DataContext = form };

        dialog.Show();
        dialog.UpdateLayout();
        Assert.True(form.IsSimulation);
        Assert.True(form.MoveNext(), form.ValidationMessage);
        dialog.UpdateLayout();

        var stationLabel = dialog.GetVisualDescendants().OfType<TextBlock>()
            .Single(text => string.Equals(text.Text?.ToString(), "Modbus 站号", StringComparison.Ordinal));
        var simulationNote = dialog.GetVisualDescendants().OfType<Border>()
            .Single(border => border.Classes.Contains("device-wizard-note")
                && border.GetVisualDescendants().OfType<TextBlock>().Any(text =>
                    text.Text?.ToString()?.Contains("当前设备为仿真模式", StringComparison.Ordinal) == true));

        Assert.True(stationLabel.IsVisible);
        Assert.True(simulationNote.IsVisible);
        var stationBottom = stationLabel.TranslatePoint(new Point(0, stationLabel.Bounds.Height), dialog);
        var noteTop = simulationNote.TranslatePoint(new Point(0, 0), dialog);
        Assert.NotNull(stationBottom);
        Assert.NotNull(noteTop);
        Assert.True(stationBottom!.Value.Y <= noteTop!.Value.Y);

        dialog.Close();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
    }

    [AvaloniaFact]
    public async Task DevicePointPage_RendersTwoScopedSearchesAndKeepsSignalBindingOutOfToolbar()
    {
        var (configRoot, dataRoot) = CreateTempConfig();
        var composition = CreateComposition(configRoot, dataRoot);
        try
        {
            Assert.Null(composition.StartupError);
            var shell = composition.Shell!;

        shell.LoginName = "admin";
        shell.Password = "admin123";
        await RunOnUiAsync(() => shell.LoginAsync());
        shell.NewPassword = "newpass123";
        shell.ConfirmPassword = "newpass123";
        await RunOnUiAsync(() => shell.ChangePasswordAsync());
        Assert.True(shell.IsAuthenticated);

        var devicePointNav = shell.NavItems.Single(item => item.Title == "设备点位");
        await RunOnUiAsync(() => shell.NavigateCommand.ExecuteAsync(devicePointNav));

        var window = RunOnUi(() => new MainWindow { DataContext = shell });
        RunOnUi(window.Show);
        RunOnUi(window.UpdateLayout);
        var pointPage = Assert.IsType<DevicePointManagementViewModel>(devicePointNav.Page);
        Assert.Contains(pointPage.Points, row => row.WritePolicyText is "只读" or "读写");
        DevicePointManagementView? view = null;
        RunOnUi(() =>
        {
            view = window.GetVisualDescendants().OfType<DevicePointManagementView>().Single();
            var searchBoxes = view.GetVisualDescendants().OfType<TextBox>()
                .Where(box => box.Classes.Contains("point-search-input"))
                .ToArray();

            Assert.Equal(2, searchBoxes.Length);
            Assert.Contains(searchBoxes, box => box.Watermark == "搜索点位名称、地址或编码");
            Assert.Contains(searchBoxes, box => box.Watermark == "搜索通道、设备或分组");
            Assert.DoesNotContain(
                view.GetVisualDescendants().OfType<Button>(),
                button => string.Equals(button.Content?.ToString(), "信号绑定", StringComparison.Ordinal));

            var moreButton = view.GetVisualDescendants().OfType<Button>()
                .Single(button => AutomationProperties.GetName(button) == "设备点位更多操作");
            moreButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var moreMenu = Assert.IsType<ContextMenu>(moreButton.ContextMenu);
            Assert.Contains(
                moreMenu.Items.OfType<MenuItem>(),
                item => string.Equals(item.Header?.ToString(), "业务信号绑定", StringComparison.Ordinal));
            Assert.Contains(
                moreMenu.Items.OfType<MenuItem>(),
                item => string.Equals(item.Header?.ToString(), "点位诊断", StringComparison.Ordinal));
            moreMenu.Close();

            var importExportButton = view.GetVisualDescendants().OfType<Button>()
                .Single(button => AutomationProperties.GetName(button) == "导入或导出设备点位模板");
            importExportButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var importExportMenu = Assert.IsType<ContextMenu>(importExportButton.ContextMenu);
            var importExportHeaders = importExportMenu.Items.OfType<MenuItem>()
                .Select(item => item.Header?.ToString())
                .ToArray();
            Assert.Contains("导入点位", importExportHeaders);
            Assert.Contains("导出当前范围", importExportHeaders);
            Assert.Contains("下载填写模板", importExportHeaders);
            importExportMenu.Close();
        });

        var diagnosticsViewModel = pointPage.CreateDiagnosticsViewModel();
        Assert.NotNull(diagnosticsViewModel);
        await RunOnUiAsync(() => diagnosticsViewModel!.LoadAsync());
        var diagnosticsTexts = Array.Empty<string?>();
        var diagnosticsWindow = RunOnUi(() => new DevicePointDiagnosticsWindow { DataContext = diagnosticsViewModel });
        RunOnUi(() =>
        {
            diagnosticsWindow.Show();
            diagnosticsWindow.UpdateLayout();
            diagnosticsTexts = diagnosticsWindow.GetVisualDescendants().OfType<TextBlock>()
                .Select(text => text.Text?.ToString())
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToArray();
        });
        Assert.Contains("只读点位诊断", diagnosticsTexts);
        Assert.Contains(diagnosticsTexts, text => text!.Contains("仿真模式", StringComparison.Ordinal));
        RunOnUi(diagnosticsWindow.Close);


        var captureDirectory = Environment.GetEnvironmentVariable("TESTBENCH_UI_CAPTURE_DIR");
        if (!string.IsNullOrWhiteSpace(captureDirectory))
        {
            Directory.CreateDirectory(captureDirectory);
            var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame.Save(Path.Combine(captureDirectory, "device-points-search-permission.png"));
        }

            RunOnUi(window.Close);
        }
        finally
        {
            await composition.DeviceModes!.ShutdownAsync();
        }
    }

    private static void SaveDeviceWizardFrame(Window dialog, string fileName)
    {
        var captureDirectory = Environment.GetEnvironmentVariable("TESTBENCH_UI_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(captureDirectory))
            return;

        Directory.CreateDirectory(captureDirectory);
        dialog.CaptureRenderedFrame()?.Save(Path.Combine(captureDirectory, fileName));
    }

    private static (string ConfigRoot, string DataRoot) CreateTempConfig()
    {
        var root = Path.Combine(
            @"D:\Codex相关\设备点位简化改造\headless-runs",
            "testbench-headless-" + Guid.NewGuid().ToString("N")[..8]);
        var configRoot = Path.Combine(root, "config");
        var dataRoot = Path.Combine(root, "data");
        Directory.CreateDirectory(configRoot);
        Directory.CreateDirectory(dataRoot);
        File.WriteAllText(Path.Combine(configRoot, "app.json"), """
        {
          "schemaVersion": 1,
          "systemName": "XXX试验台",
          "brand": "XXX.TestBench.Template",
          "language": "zh-CN",
          "modules": { "processMonitor": true, "deviceCalibration": true },
          "defaultPaths": { "database": "testbench.db", "reportOutput": "reports", "reportTemplates": "assets/report-templates" }
        }
        """);
        File.WriteAllText(Path.Combine(configRoot, "device.json"), """
        {
          "schemaVersion": 3,
          "pollIntervalMs": 500,
          "timeoutMs": 1000,
          "channels": [
            { "id": "10000000-0000-0000-0000-000000000001", "code": "CH_SIM", "name": "仿真设备网络通道", "transportKind": "Tcp", "enabled": true, "timeoutMs": 1000, "retryCount": 0, "tcp": { "host": "127.0.0.1", "port": 102 } },
            { "id": "10000000-0000-0000-0000-000000000002", "code": "CH_EMPTY", "name": "备用网络通道", "transportKind": "Tcp", "enabled": true, "timeoutMs": 1000, "retryCount": 0, "tcp": { "host": "127.0.0.1", "port": 103 } }
          ],
          "devices": [
            { "id": "20000000-0000-0000-0000-000000000001", "code": "DEV_A", "name": "设备 A", "deviceMode": "Simulation", "protocol": "SiemensS7", "address": "127.0.0.1", "channelId": "10000000-0000-0000-0000-000000000001", "driverKey": "siemens-s7", "model": "S7-1500", "pollIntervalMs": 500, "staleAfterMs": 1500 },
            { "id": "20000000-0000-0000-0000-000000000002", "code": "DEV_B", "name": "设备 B", "deviceMode": "Simulation", "protocol": "SiemensS7", "address": "127.0.0.1", "channelId": "10000000-0000-0000-0000-000000000001", "driverKey": "siemens-s7", "model": "S7-1500", "pollIntervalMs": 500, "staleAfterMs": 1500 }
          ]
        }
        """);
        File.WriteAllText(Path.Combine(configRoot, "points.json"), """
        {
          "schemaVersion": 3,
          "groups": [
            { "id": "30000000-0000-0000-0000-000000000001", "deviceId": "20000000-0000-0000-0000-000000000001", "code": "DEFAULT", "name": "未分组", "sortOrder": 0 },
            { "id": "30000000-0000-0000-0000-000000000002", "deviceId": "20000000-0000-0000-0000-000000000001", "code": "PRESSURE", "name": "压力", "sortOrder": 10 },
            { "id": "30000000-0000-0000-0000-000000000003", "deviceId": "20000000-0000-0000-0000-000000000002", "code": "DEFAULT", "name": "未分组", "sortOrder": 0 }
          ],
          "points": [
            { "id": "40000000-0000-0000-0000-000000000001", "code": "AI_PRESSURE", "name": "入口压力", "protocol": "SiemensS7", "deviceId": "20000000-0000-0000-0000-000000000001", "groupId": "30000000-0000-0000-0000-000000000002", "address": "DB1.DBD0", "dataType": "Decimal", "rawDataType": "Decimal", "writePolicy": "ReadBackEqual", "isWritable": false, "riskLevel": "Normal" },
            { "id": "40000000-0000-0000-0000-000000000002", "code": "DO_START", "name": "启动命令", "protocol": "SiemensS7", "deviceId": "20000000-0000-0000-0000-000000000002", "groupId": "30000000-0000-0000-0000-000000000003", "address": "DB1.DBX4.0", "dataType": "Boolean", "rawDataType": "Boolean", "writePolicy": "ReadBackEqual", "isWritable": true, "riskLevel": "HighRisk" }
          ]
        }
        """);
        File.WriteAllText(Path.Combine(configRoot, "simulation.json"), """
        {
          "schemaVersion": 2,
          "initialValuesByPointId": {
            "40000000-0000-0000-0000-000000000001": 0.0,
            "40000000-0000-0000-0000-000000000002": false
          },
          "changeRules": [],
          "faultInjectionScenarios": []
        }
        """);
        return (configRoot, dataRoot);
    }

    private static AppComposition CreateComposition(string configRoot, string dataRoot)
    {
        // AppComposition 保持现有同步入口；测试宿主的 UI 同步上下文不能被同步创建调用阻塞。
        AppComposition composition;
        using (ExecutionContext.SuppressFlow())
        {
            composition = Task.Run(() =>
            {
                // Avalonia/xUnit 会把测试同步上下文附着到线程池任务；组合根内部仍有同步等待，
                // 因此在测试专用线程池入口明确清除它，避免 JSON 异步 I/O 回投到已被阻塞的 UI 线程。
                SynchronizationContext.SetSynchronizationContext(null);
                return AppComposition.Create(configRoot, dataRoot);
            }).GetAwaiter().GetResult();
        }
        return composition;
    }

    /// <summary>
    /// 单个会话内完成外壳端到端验证：故障横幅、全局模式入口移除、登录与导航。
    /// 无头测试框架在该环境存在挂起问题（启动守卫已缓解但仍偶发），因此把多个断言合并为一个用例。
    /// </summary>
    [AvaloniaFact]
    public async Task Shell_EndToEnd_FaultBanner_GlobalModeRemoved_LoginAndNavigate()
    {
        var faulted = new ShellViewModel(null, isFaulted: true, faultMessage: "配置无效");

        var faultWindow = new MainWindow { DataContext = faulted };
        faultWindow.Show();
        Assert.True(faultWindow.FindControl<Border>("FaultBanner")?.IsVisible);
        faultWindow.Close();

        var (configRoot, dataRoot) = CreateTempConfig();

        var composition = CreateComposition(configRoot, dataRoot);
        try
        {
        Assert.Null(composition.StartupError);
        var shell = composition.Shell!;

        var loginWindow = new LoginWindow(shell);
        loginWindow.Show();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);
        var loginView = loginWindow.GetVisualDescendants().OfType<LoginView>().Single();
        var loginNameBox = loginView.FindControl<TextBox>("LoginNameBox");
        Assert.NotNull(loginNameBox);
        Assert.Same(loginNameBox, loginWindow.FocusManager?.GetFocusedElement());
        loginWindow.Close();

        var window = new MainWindow { DataContext = shell };
        window.Show();

        Assert.Null(window.FindControl<TextBlock>("ModeTextBlock"));
        Assert.DoesNotContain(
            window.GetVisualDescendants().OfType<TextBlock>(),
            text => string.Equals(text.Text, "仿真模式", StringComparison.Ordinal));
        Assert.False(window.FindControl<Border>("FaultBanner")?.IsVisible);

        shell.LoginName = "admin";

        shell.Password = "admin123";
        await RunOnUiAsync(() => shell.LoginAsync());
        Assert.True(shell.MustChangePassword);
        shell.NewPassword = "newpass123";
        shell.ConfirmPassword = "newpass123";
        await RunOnUiAsync(() => shell.ChangePasswordAsync());

        Assert.True(shell.IsAuthenticated);

        Assert.Equal(9, shell.NavItems.Count);
        Assert.NotNull(shell.CurrentPage);
        Assert.Equal("运行总览", shell.CurrentPage!.Title);

        var devicePointNav = shell.NavItems.Single(item => item.Title == "设备点位");
        await RunOnUiAsync(() => shell.NavigateCommand.ExecuteAsync(devicePointNav));
        RunOnUi(window.UpdateLayout);
        RunOnUi(() =>
        {
        var devicePointViewModel = Assert.IsType<DevicePointManagementViewModel>(devicePointNav.Page);
        Assert.True(devicePointViewModel.TreeNodes.Count == 1, devicePointViewModel.StatusMessage);
        Assert.Equal("root", devicePointViewModel.TreeNodes[0].Code);
        Assert.Equal("全部设备", devicePointViewModel.TreeNodes[0].Name);
        Assert.Equal(2, devicePointViewModel.TreeNodes[0].Children.Count);
        Assert.All(devicePointViewModel.TreeNodes[0].Children, channel =>
        {
            Assert.All(channel.Children, device =>
                Assert.All(device.Children, group => Assert.Empty(group.Children)));
        });
        var devicePointView = window.GetVisualDescendants().OfType<DevicePointManagementView>().Single();
        var tree = devicePointView.GetVisualDescendants().OfType<TreeView>().SingleOrDefault();
        Assert.NotNull(tree);
        var treeTexts = tree!.GetVisualDescendants().OfType<TextBlock>()
            .Where(text => text.IsVisible)
            .Select(text => text.Text)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray();
        Assert.Contains("仿真设备网络通道", treeTexts);
        Assert.Contains("设备 A", treeTexts);
        var hiddenTreeSummaries = devicePointViewModel.TreeNodes
            .SelectMany(root => root.Children)
            .SelectMany(channel => new[] { channel }.Concat(channel.Children))
            .Where(node => node.Kind is DevicePointTreeNodeKind.Channel or DevicePointTreeNodeKind.Device)
            .Select(node => node.TreeSummaryText)
            .Where(summary => !string.IsNullOrWhiteSpace(summary));
        Assert.All(hiddenTreeSummaries, summary => Assert.DoesNotContain(summary, treeTexts));
        var pointGrid = devicePointView.FindControl<DataGrid>("PointGrid");
        Assert.NotNull(pointGrid);
        Assert.Equal(
            new[] { "点位名称", "设备地址", "数据类型", "访问权限", "点位备注" },
            pointGrid!.Columns.Select(column => column.Header?.ToString()).ToArray());
        Assert.True(pointGrid.Bounds.Width > 500);
        Assert.True(pointGrid.Bounds.Height > 0);

        var captureDirectory = Environment.GetEnvironmentVariable("TESTBENCH_UI_CAPTURE_DIR");
        if (!string.IsNullOrWhiteSpace(captureDirectory))
        {
            Directory.CreateDirectory(captureDirectory);
            var pointFrame = window.CaptureRenderedFrame();
            Assert.NotNull(pointFrame);
            pointFrame.Save(Path.Combine(captureDirectory, "device-points-1680x945.png"));

            var firstChannel = devicePointViewModel.TreeNodes[0].Children.First();
            var configurationEditor = devicePointViewModel.CreateDeviceConfigurationDialog();
            var channelForm = configurationEditor.CreateChannelEditor(firstChannel.Id);
            if (channelForm is not null)
            {
                var channelDialog = new ChannelEditorDialogWindow { DataContext = channelForm };
                channelDialog.Show();
                channelDialog.UpdateLayout();
                var channelFrame = channelDialog.CaptureRenderedFrame()
                    ?? throw new InvalidOperationException("通道编辑弹窗未生成渲染帧");
                channelFrame.Save(Path.Combine(captureDirectory, "channel-editor.png"));
                channelDialog.Close();
            }

            var firstDevice = firstChannel.Children.First();
            var deviceForm = configurationEditor.CreateDeviceEditor(firstDevice.Id);
            if (deviceForm is not null)
            {
                var deviceDialog = new DeviceEditorDialogWindow { DataContext = deviceForm };
                deviceDialog.Show();
                deviceDialog.UpdateLayout();
                var deviceFrame = deviceDialog.CaptureRenderedFrame()
                    ?? throw new InvalidOperationException("设备编辑弹窗未生成渲染帧");
                deviceFrame.Save(Path.Combine(captureDirectory, "device-editor.png"));
                deviceDialog.Close();
            }

            var pointRow = devicePointViewModel.Points.First();
            var pointContext = devicePointViewModel.CreatePointDialogContext(true, pointRow);
            var pointDialog = new DevicePointDialogWindow
            {
                DataContext = new DevicePointDialogViewModel(true, pointRow.Entry, pointContext)
            };
            pointDialog.Show();
            pointDialog.UpdateLayout();
            var pointEditorFrame = pointDialog.CaptureRenderedFrame()
                ?? throw new InvalidOperationException("点位编辑弹窗未生成渲染帧");
            pointEditorFrame.Save(Path.Combine(captureDirectory, "point-editor.png"));
            pointDialog.Close();

            var importDialog = new DevicePointImportPreviewWindow
            {
                DataContext = new DevicePointImportPreviewViewModel(
                    devicePointViewModel.CurrentEntries,
                    devicePointViewModel.CurrentEntries.Count,
                    devicePointViewModel.CurrentEntries,
                    devicePointViewModel.CurrentGroups)
            };
            importDialog.Show();
            importDialog.UpdateLayout();
            var importFrame = importDialog.CaptureRenderedFrame()
                ?? throw new InvalidOperationException("点位导入预览弹窗未生成渲染帧");
            importFrame.Save(Path.Combine(captureDirectory, "point-import-preview.png"));
            importDialog.Close();
        }

        var workspaceRegion = window.FindControl<Border>("WorkspaceRegion");
        var statusBarOnDevicePage = window.FindControl<Border>("StatusBar");
        var devicePointPageLayout = devicePointView.FindControl<Grid>("DevicePointPageLayout");
        Assert.NotNull(workspaceRegion);
        Assert.NotNull(statusBarOnDevicePage);
        Assert.NotNull(devicePointPageLayout);
        Assert.InRange(statusBarOnDevicePage.Bounds.Top - workspaceRegion.Bounds.Bottom, -1, 1);
        Assert.Equal(new Thickness(0, 0, 0, 12), devicePointPageLayout.Margin);
        Assert.DoesNotContain(
            devicePointView.GetVisualDescendants().OfType<Button>(),
            button => string.Equals(button.Content?.ToString(), "节点操作", StringComparison.Ordinal));
        });
        await RunOnUiAsync(() => shell.NavigateCommand.ExecuteAsync(shell.NavItems[0]));
        RunOnUi(window.UpdateLayout);

        RunOnUi(() => VerifyResponsiveLayout(window, 1440, 900, "main-1440x900.png"));

        RunOnUi(() => VerifyResponsiveLayout(window, 1680, 945, "main-1680x945.png"));
        RunOnUi(() => VerifyResponsiveLayout(window, 1920, 1080, "main-1920x1080.png"));
        RunOnUi(() => VerifyResponsiveLayout(window, 1152, 720, "main-1440x900-at-125dpi.png"));
        await VerifyStatusBarOnEveryPage(window, shell);

        RunOnUi(window.Close);
        }
        finally
        {
            await composition.DeviceModes!.ShutdownAsync();
        }
    }

    private static void VerifyResponsiveLayout(MainWindow window, double width, double height, string captureName)
    {
        window.WindowState = WindowState.Normal;
        window.Width = width;
        window.Height = height;

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        Assert.True(frame.PixelSize.Width > 0);
        Assert.True(frame.PixelSize.Height > 0);

        var shellRoot = window.FindControl<Grid>("ShellRoot");
        var topHeader = window.FindControl<Border>("TopHeader");
        var productBar = window.FindControl<Border>("ProductInfoBar");
        var workspace = window.FindControl<Border>("WorkspaceRegion");
        var bottomBar = window.FindControl<Border>("BottomControlBar");
        var statusBar = window.FindControl<Border>("StatusBar");
        var exitButton = window.FindControl<Button>("ExitButton");
        var overview = window.GetVisualDescendants().OfType<OverviewView>().Single();
        var schematic = overview.FindControl<Border>("PipeSchematicHost");
        var taskPanel = overview.FindControl<Border>("PointPanel");
        var measurementPanel = overview.FindControl<Border>("MeasurementPanel");

        Assert.NotNull(shellRoot);
        Assert.NotNull(topHeader);
        Assert.NotNull(productBar);
        Assert.NotNull(workspace);
        Assert.NotNull(bottomBar);
        Assert.NotNull(statusBar);
        Assert.NotNull(exitButton);
        Assert.NotNull(schematic);
        Assert.NotNull(taskPanel);
        Assert.NotNull(measurementPanel);
        Assert.True(shellRoot.Bounds.Width > 900);
        Assert.InRange(topHeader.Bounds.Height, 76, 80);
        Assert.True(productBar.Bounds.Height >= 68);
        Assert.True(workspace.Bounds.Height > 350);
        Assert.InRange(bottomBar.Bounds.Height, 74, 78);
        Assert.True(statusBar.IsVisible);
        Assert.InRange(statusBar.Bounds.Height, 46, 50);
        Assert.InRange(exitButton.Bounds.Height, 46, 50);
        Assert.True(statusBar.Bounds.Top >= workspace.Bounds.Bottom - 1);
        Assert.True(statusBar.Bounds.Bottom >= shellRoot.Bounds.Height - 1);
        Assert.True(schematic.Bounds.Width > 300);
        Assert.True(schematic.Bounds.Height > 300);
        Assert.InRange(taskPanel.Bounds.Width, 190, 249);
        Assert.InRange(measurementPanel.Bounds.Width, 210, 281);

        var captureDirectory = Environment.GetEnvironmentVariable("TESTBENCH_UI_CAPTURE_DIR");
        if (!string.IsNullOrWhiteSpace(captureDirectory))
        {
            Directory.CreateDirectory(captureDirectory);
            frame.Save(Path.Combine(captureDirectory, captureName));
        }
    }

    private static async Task VerifyStatusBarOnEveryPage(MainWindow window, ShellViewModel shell)
    {
        var controls = RunOnUi(() =>
        (
            ShellRoot: window.FindControl<Grid>("ShellRoot"),
            Workspace: window.FindControl<Border>("WorkspaceRegion"),
            StatusBar: window.FindControl<Border>("StatusBar")
        ));
        var shellRoot = controls.ShellRoot;
        var workspace = controls.Workspace;
        var statusBar = controls.StatusBar;

        Assert.NotNull(shellRoot);
        Assert.NotNull(workspace);
        Assert.NotNull(statusBar);

        foreach (var item in shell.NavItems)
        {
            await RunOnUiAsync(() => shell.NavigateCommand.ExecuteAsync(item));
            RunOnUi(() =>
            {
                window.UpdateLayout();

                Assert.True(statusBar.IsVisible, $"状态条在“{item.DisplayTitle}”页面不可见。");
                Assert.True(statusBar!.Bounds.Top >= workspace!.Bounds.Bottom - 1,
                    $"状态条未位于“{item.DisplayTitle}”页面底部。");
                Assert.True(statusBar.Bounds.Bottom >= shellRoot!.Bounds.Height - 1,
                    $"状态条未贴合“{item.DisplayTitle}”页面底边。");
            });
        }

        await RunOnUiAsync(() => shell.NavigateCommand.ExecuteAsync(shell.NavItems[0]));
        RunOnUi(window.UpdateLayout);
    }

    private static void RunOnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.UIThread.InvokeAsync(action).GetAwaiter().GetResult();
    }

    private static async Task RunOnUiAsync(Func<Task> action)
    {
        await Dispatcher.UIThread.InvokeAsync(action);
    }

    private static T RunOnUi<T>(Func<T> factory)
    {
        if (Dispatcher.UIThread.CheckAccess())
            return factory();

        return Dispatcher.UIThread.InvokeAsync(factory).GetAwaiter().GetResult();
    }

}
