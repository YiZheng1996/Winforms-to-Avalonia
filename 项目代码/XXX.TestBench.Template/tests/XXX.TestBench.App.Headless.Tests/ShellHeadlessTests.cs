using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEllipse = Avalonia.Controls.Shapes.Ellipse;
using AvaloniaLine = Avalonia.Controls.Shapes.Line;
using AvaloniaPolyline = Avalonia.Controls.Shapes.Polyline;
using AvaloniaPolygon = Avalonia.Controls.Shapes.Polygon;
using XXX.TestBench.App;
using XXX.TestBench.App.Composition;
using XXX.TestBench.App.Controls.Process;
using XXX.TestBench.App.Icons;
using XXX.TestBench.App.ViewModels;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Ports;
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
            Tcp = new TcpChannelParameters()
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
            Tcp = new TcpChannelParameters()
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
    public void PointGroupDialogKeepsOwnerInHeaderAndFooterVisibleWhenAdvancedDescriptionOpens()
    {
        var form = new PointGroupDialogViewModel(
            isEdit: false,
            current: null,
            deviceId: "device-1",
            deviceName: "新设备");
        form.GroupName = "压力分组";
        var dialog = new PointGroupDialogWindow { DataContext = form };

        dialog.Show();
        dialog.UpdateLayout();

        Assert.Equal("所属设备：新设备", form.OwnerDeviceSummaryText);
        Assert.True(form.TryBuildResult(out var result));
        Assert.Equal("device-1", result.DeviceId);
        Assert.Contains(
            dialog.GetVisualDescendants().OfType<TextBlock>(),
            text => text.Text?.ToString() == "所属设备：新设备");
        Assert.DoesNotContain(
            dialog.GetVisualDescendants().OfType<TextBox>(),
            text => text.Text?.ToString() == "新设备");

        var expander = dialog.GetVisualDescendants().OfType<Expander>()
            .Single(item => item.Header?.ToString() == "高级说明（可选）");
        expander.IsExpanded = true;
        dialog.UpdateLayout();

        var description = dialog.GetVisualDescendants().OfType<TextBox>()
            .Single(text => text.Watermark == "填写分组用途");
        Assert.True(description.IsVisible);
        foreach (var buttonText in new[] { "取消", "保存" })
        {
            var button = dialog.GetVisualDescendants().OfType<Button>()
                .Single(item => item.Content?.ToString() == buttonText);
            Assert.True(button.IsVisible);
            Assert.True(button.Bounds.Height >= 38);
            var bottom = button.TranslatePoint(new Point(0, button.Bounds.Height), dialog);
            Assert.NotNull(bottom);
            Assert.True(bottom!.Value.Y <= dialog.Bounds.Height);
        }

        SaveDeviceWizardFrame(dialog, "point-group-dialog-advanced.png");
        dialog.Close();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
    }

    [AvaloniaFact]
    public void DevicePointDialogAlignsConfigurationHintIconWithText()
    {
        var device = new DevicePointDeviceChoice(
            "10000000-0000-0000-0000-000000000012",
            "DEV_SIM",
            "仿真设备",
            "simulation",
            DeviceMode.Simulation);
        var context = new DevicePointEditContext(
            "仿真通道",
            device,
            new HashSet<DevicePointDataType> { DevicePointDataType.Double },
            Array.Empty<DevicePointGroupChoice>(),
            null,
            GroupLocked: true);
        var dialog = new DevicePointDialogWindow
        {
            DataContext = new DevicePointDialogViewModel(false, null, context)
        };

        dialog.Show();
        dialog.UpdateLayout();

        var hintText = dialog.GetVisualDescendants().OfType<TextBlock>()
            .Single(text => text.Text?.ToString() == "配置修改不会直接写入现场设备");
        var hintIcon = dialog.GetVisualDescendants().OfType<AppIcon>()
            .Single(icon => icon.Kind == AppIconKind.Information);
        var textPoint = hintText.TranslatePoint(new Point(0, 0), dialog);
        var iconPoint = hintIcon.TranslatePoint(new Point(0, 0), dialog);

        Assert.NotNull(textPoint);
        Assert.NotNull(iconPoint);
        Assert.InRange(Math.Abs(iconPoint!.Value.Y - textPoint!.Value.Y), 0, 2);
        Assert.True(hintIcon.Bounds.Width >= 18);
        Assert.True(hintIcon.Bounds.Height >= 18);

        dialog.Close();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);
    }

    [AvaloniaFact]
    public void XinjePointDialogShowsNativeAddressAndHidesStructuredModbusFields()
    {
        var order = new ModbusDataOrderOptions
        {
            ByteOrder = ByteOrder.BigEndian,
            WordOrder = WordOrder.LowWordFirst
        };
        var device = new DevicePointDeviceChoice(
            "10000000-0000-0000-0000-000000000101",
            "DEV_XINJE",
            "信捷 PLC",
            DriverKeyCatalog.ModbusTcp,
            DeviceMode.Simulation,
            DeviceModelKeyCatalog.XinjeFieldV1,
            "如：D100、HD1000、M400、HM40",
            "仅支持现场已验证的 D/HD/M/HM；不支持寄存器位地址。",
            1,
            order);
        var context = new DevicePointEditContext(
            "TCP 通道",
            device,
            new HashSet<DevicePointDataType>
            {
                DevicePointDataType.Bool,
                DevicePointDataType.UInt16,
                DevicePointDataType.Int32,
                DevicePointDataType.UInt32,
                DevicePointDataType.Float32
            },
            Array.Empty<DevicePointGroupChoice>(),
            null,
            GroupLocked: true);
        var form = new DevicePointDialogViewModel(false, null, context)
        {
            PointName = "高速压力",
            Address = "HD1000"
        };
        var dialog = new DevicePointDialogWindow { DataContext = form };

        dialog.Show();
        dialog.UpdateLayout();

        var nativeAddress = dialog.GetVisualDescendants().OfType<TextBox>()
            .Single(text => text.Watermark == form.AddressWatermark);
        var offset = dialog.GetVisualDescendants().OfType<TextBox>()
            .Single(text => text.Watermark == "0～65535");
        var area = dialog.GetVisualDescendants().OfType<ComboBox>()
            .Single(combo => ReferenceEquals(combo.ItemsSource, form.ModbusAreaOptions));
        Assert.True(nativeAddress.IsVisible);
        Assert.True(form.IsStandardAddressVisible);
        Assert.False(form.IsStructuredModbusAddressVisible);
        Assert.Contains(offset.GetVisualAncestors().OfType<StackPanel>(), panel => !panel.IsVisible);
        Assert.Contains(area.GetVisualAncestors().OfType<StackPanel>(), panel => !panel.IsVisible);
        Assert.Equal(
            new[]
            {
                DevicePointDataType.UInt16,
                DevicePointDataType.Float32,
                DevicePointDataType.Int32,
                DevicePointDataType.UInt32
            }.OrderBy(type => type),
            form.DataTypeOptions.Select(option => option.Value).OrderBy(type => type));

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
            Assert.False(pointPage.EventLog.IsExpanded);
            var collapsedEventLogPanel = view.GetVisualDescendants().OfType<Border>()
                .Where(border => border.IsVisible)
                .Single(border => border.Classes.Contains("point-panel")
                    && border.GetVisualDescendants().OfType<TextBlock>().Any(text =>
                        text.Text?.ToString() == "设备事件日志")
                    && border.GetVisualDescendants().OfType<Button>().Any(button =>
                        button.Content?.ToString() == "展开"));
            var collapsedEventLogTitle = collapsedEventLogPanel.GetVisualDescendants().OfType<TextBlock>()
                .Single(text => text.Text?.ToString() == "设备事件日志");
            var collapsedEventLogButton = collapsedEventLogPanel.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Content?.ToString() == "展开");
            Assert.True(collapsedEventLogPanel.IsVisible);
            Assert.True(collapsedEventLogPanel.Bounds.Height >= 54);
            Assert.True(collapsedEventLogTitle.Bounds.Height > 0);
            Assert.True(collapsedEventLogButton.Bounds.Height >= 38);

            pointPage.EventLog.ToggleExpanded();
            window.UpdateLayout();
            Assert.True(pointPage.EventLog.IsExpanded);
            var expandedEventLogPanel = view.GetVisualDescendants().OfType<Border>()
                .Where(border => border.IsVisible)
                .Single(border => border.Classes.Contains("point-panel")
                    && border.GetVisualDescendants().OfType<TextBlock>().Any(text =>
                        text.Text?.ToString() == "设备事件日志")
                    && border.GetVisualDescendants().OfType<Button>().Any(button =>
                        button.Content?.ToString() == "收起"));
            var expandedEventLogTitle = expandedEventLogPanel.GetVisualDescendants().OfType<TextBlock>()
                .Single(text => text.Text?.ToString() == "设备事件日志");
            var expandedEventLogButton = expandedEventLogPanel.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Content?.ToString() == "收起");
            Assert.True(expandedEventLogPanel.Bounds.Height >= 170);
            Assert.True(expandedEventLogTitle.Bounds.Height > 0);
            Assert.True(expandedEventLogButton.Bounds.Height >= 38);
            pointPage.EventLog.ToggleExpanded();
            window.UpdateLayout();
            Assert.False(pointPage.EventLog.IsExpanded);
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

        RunOnUi(() =>
        {
            // 模拟当前 Active 设备被配置为 Modbus，确认站号在设备属性卡中可见。
            var modbusDevice = pointPage.CurrentDeviceEntries.Single(device => device.Code == "DEV_A");
            var originalDriverKey = modbusDevice.DriverKey;
            var originalStation = modbusDevice.ModbusUnitId;
            var originalModel = modbusDevice.Model;
            modbusDevice.DriverKey = DriverKeyCatalog.ModbusRtu;
            modbusDevice.Model = "GENERIC_MODBUS_RTU";
            modbusDevice.ModbusUnitId = 17;
            var modbusDeviceNode = pointPage.TreeNodes[0].Children
                .Single(node => node.Code == "CH_SIM")
                .Children.Single(node => node.Code == "DEV_A");
            pointPage.SelectedTreeNode = modbusDeviceNode;
            window.UpdateLayout();
            var visibleDetailTexts = view!.GetVisualDescendants().OfType<TextBlock>()
                .Where(text => text.IsVisible)
                .Select(text => text.Text?.ToString())
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToArray();
            Assert.Contains("Modbus 站号", visibleDetailTexts);
            Assert.Contains("17", visibleDetailTexts);
            Assert.Contains("通用 Modbus RTU 设备", visibleDetailTexts);
            Assert.DoesNotContain("GENERIC_MODBUS_RTU", visibleDetailTexts);
            modbusDevice.DriverKey = originalDriverKey;
            modbusDevice.Model = originalModel;
            modbusDevice.ModbusUnitId = originalStation;
            pointPage.SelectedTreeNode = pointPage.TreeNodes[0];
            window.UpdateLayout();
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
            var frame = RunOnUi(() => window.CaptureRenderedFrame());
            Assert.NotNull(frame);
            frame!.Save(Path.Combine(captureDirectory, "device-points-search-permission.png"));
        }

            RunOnUi(window.Close);
        }
        finally
        {
            if (composition.DeviceModes is { } deviceModes)
                await deviceModes.ShutdownAsync();
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
            Path.GetTempPath(),
            "headless-runs",
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
          "schemaVersion": 5,
          "channels": [
            { "id": "10000000-0000-0000-0000-000000000001", "code": "CH_SIM", "name": "仿真设备网络通道", "transportKind": "Tcp", "enabled": true, "timeoutMs": 1000, "retryCount": 0, "tcp": {} },
            { "id": "10000000-0000-0000-0000-000000000002", "code": "CH_EMPTY", "name": "备用网络通道", "transportKind": "Tcp", "enabled": true, "timeoutMs": 1000, "retryCount": 0, "tcp": {} }
          ],
          "devices": [
            { "id": "20000000-0000-0000-0000-000000000001", "code": "DEV_A", "name": "设备 A", "deviceMode": "Simulation", "channelId": "10000000-0000-0000-0000-000000000001", "driverKey": "siemens-s7", "model": "S7-1500", "pollIntervalMs": 500, "staleAfterMs": 1500, "scanMode": "FixedInterval", "timing": { "connectTimeoutMs": 1000, "requestTimeoutMs": 500, "retryCount": 0, "interRequestDelayMs": 0 }, "autoDemotion": { "enabled": true, "failureThreshold": 3, "demotionPeriodMs": 10000 }, "siemensS7": { "host": "127.0.0.1", "port": 102, "rack": 0, "slot": 0 } },
            { "id": "20000000-0000-0000-0000-000000000002", "code": "DEV_B", "name": "设备 B", "deviceMode": "Simulation", "channelId": "10000000-0000-0000-0000-000000000001", "driverKey": "siemens-s7", "model": "S7-1500", "pollIntervalMs": 500, "staleAfterMs": 1500, "scanMode": "FixedInterval", "timing": { "connectTimeoutMs": 1000, "requestTimeoutMs": 500, "retryCount": 0, "interRequestDelayMs": 0 }, "autoDemotion": { "enabled": true, "failureThreshold": 3, "demotionPeriodMs": 10000 }, "siemensS7": { "host": "127.0.0.1", "port": 103, "rack": 0, "slot": 0 } }
          ]
        }
        """);
        File.WriteAllText(Path.Combine(configRoot, "points.json"), """
        {
          "schemaVersion": 4,
          "groups": [
            { "id": "30000000-0000-0000-0000-000000000001", "deviceId": "20000000-0000-0000-0000-000000000001", "code": "DEFAULT", "name": "未分组", "description": "设备默认分组", "sortOrder": 0 },
            { "id": "30000000-0000-0000-0000-000000000002", "deviceId": "20000000-0000-0000-0000-000000000001", "code": "PRESSURE", "name": "压力", "description": "压力点位", "sortOrder": 10 },
            { "id": "30000000-0000-0000-0000-000000000003", "deviceId": "20000000-0000-0000-0000-000000000002", "code": "DEFAULT", "name": "未分组", "description": "设备默认分组", "sortOrder": 0 }
          ],
          "points": [
            { "id": "40000000-0000-0000-0000-000000000001", "code": "AI_PRESSURE", "name": "入口压力", "protocol": "SiemensS7", "deviceId": "20000000-0000-0000-0000-000000000001", "groupId": "30000000-0000-0000-0000-000000000002", "address": "DB1.DBD0", "dataType": "Float32", "decodeOptions": { "byteOrder": "BigEndian", "wordOrder": "None" }, "isWritable": false, "riskLevel": "Normal", "rawMin": 0, "rawMax": 10000, "engMin": 0, "engMax": 10, "description": "仿真压力" },
            { "id": "40000000-0000-0000-0000-000000000002", "code": "DO_START", "name": "启动命令", "protocol": "SiemensS7", "deviceId": "20000000-0000-0000-0000-000000000002", "groupId": "30000000-0000-0000-0000-000000000003", "address": "DB1.DBX4.0", "dataType": "Bool", "decodeOptions": { "byteOrder": "BigEndian", "wordOrder": "None" }, "writePolicy": "ReadBackEqual", "isWritable": true, "riskLevel": "HighRisk", "description": "仿真启动命令" }
          ]
        }
        """);
        File.WriteAllText(Path.Combine(configRoot, "simulation.json"), """
        {
          "schemaVersion": 3,
          "initialValuesByPointId": {
            "40000000-0000-0000-0000-000000000001": 0.0,
            "40000000-0000-0000-0000-000000000002": false
          },
          "changeRules": [],
          "faultInjectionScenarios": []
        }
        """);
        File.WriteAllText(Path.Combine(configRoot, "signal-bindings.json"), """
        {
          "schemaVersion": 1,
          "bindings": { "AI_Pressure": "40000000-0000-0000-0000-000000000001" }
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

        Assert.Equal(8, shell.NavItems.Count);
        Assert.NotNull(shell.CurrentPage);
        Assert.IsType<ProcessMonitorViewModel>(shell.CurrentPage);
        Assert.Equal("工艺监控", shell.CurrentPage!.Title);

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

            var previewPlan = new DevicePointImportPlanner().Build(
                new[] { new DevicePointImportRow(2, devicePointViewModel.CurrentEntries[0]) },
                devicePointViewModel.CurrentEntries,
                devicePointViewModel.CurrentGroups);
            var importDialog = new DevicePointImportPreviewWindow
            {
                DataContext = new DevicePointImportPreviewViewModel(
                    previewPlan,
                    devicePointViewModel.CurrentEntries.Count,
                    devicePointViewModel.CurrentGroups,
                    devicePointViewModel.CurrentDeviceEntries)
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
        Assert.IsType<ProcessMonitorViewModel>(shell.NavItems[0].Page);
        RunOnUi(() =>
        {
            shell.SelectProductModel(new ProductModelSelectionOption(
                11, "气密试验", 27, "TB-2026A", true, DateTime.UtcNow));
            shell.ProductNumberInput = "CP20260916001";
            shell.VehicleNumberInput = "CR400AF-0217";
        });
        await RunOnUiAsync(async () =>
        {
            var confirmation = await shell.TryConfirmProductContextAsync();
            Assert.True(confirmation.Succeeded);
        });
        RunOnUi(window.UpdateLayout);

        RunOnUi(() => VerifyResponsiveLayout(window, 1440, 900, "main-1440x900.png"));

        RunOnUi(() => VerifyResponsiveLayout(window, 1680, 945, "main-1680x945.png"));
        RunOnUi(() => VerifyResponsiveLayout(window, 1920, 1080, "main-1920x1080.png"));
        RunOnUi(() => VerifyResponsiveLayout(window, 1152, 720, "main-1440x900-at-125dpi.png"));
        RunOnUi(() => VerifyResponsiveLayout(window, 1586, 992, "process-monitor-reference-1586x992.png"));
        await VerifyStatusBarOnEveryPage(window, shell);

        RunOnUi(window.Close);
        }
        finally
        {
            if (composition.DeviceModes is { } deviceModes)
                await deviceModes.ShutdownAsync();
        }
    }

    private static void VerifyResponsiveLayout(MainWindow window, double width, double height, string captureName)
    {
        window.WindowState = WindowState.Normal;
        window.Width = width;
        window.Height = height;
        window.UpdateLayout();

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        Assert.True(frame.PixelSize.Width > 0);
        Assert.True(frame.PixelSize.Height > 0);

        var shellRoot = window.FindControl<Grid>("ShellRoot");
        var topHeader = window.FindControl<Border>("TopHeader");
        var productBar = window.FindControl<Border>("ProductInfoBar");
        var productInfoCard = window.FindControl<Border>("ProductInfoCard");
        var workspace = window.FindControl<Border>("WorkspaceRegion");
        var bottomBar = window.FindControl<Border>("BottomControlBar");
        var statusBar = window.FindControl<Border>("StatusBar");
        var exitButton = window.FindControl<Button>("ExitButton");
        var processView = window.GetVisualDescendants().OfType<ProcessMonitorView>().Single();
        var processScrollViewer = processView.FindControl<ScrollViewer>("ProcessScrollViewer");
        var diagram = processView.GetVisualDescendants().OfType<PneumaticDiagramControl>().Single();
        var diagramScaleViewbox = diagram.FindControl<Viewbox>("DiagramScaleViewbox");
        var analogInputs = processView.GetVisualDescendants().OfType<AnalogInputControl>().ToArray();
        var valveSymbols = processView.GetVisualDescendants().OfType<PneumaticValveSymbol>().ToArray();
        var mainPipe = diagram.FindControl<PneumaticPipeControl>("DiagramMainPipe");
        var inletValveControlSignal = diagram.FindControl<PneumaticPipeControl>("DiagramInletValveControlSignal");
        var exhaustValveLowerPipe = diagram.FindControl<PneumaticPipeControl>("DiagramExhaustValveLowerPipe");
        var pressureSetpointCard = diagram.FindControl<Border>("DiagramPressureSetpointCard");
        var pressureDecreaseButton = diagram.FindControl<Button>("DiagramPressureDecreaseButton");
        var pressureIncreaseButton = diagram.FindControl<Button>("DiagramPressureIncreaseButton");
        var pressureSetpointInput = diagram.FindControl<Border>("DiagramPressureSetpointInput");
        var pressureSetpointSignal = diagram.FindControl<AvaloniaLine>("DiagramPressureSetpointSignal");
        var pressureSetpointSpring = diagram.FindControl<AvaloniaPolyline>("DiagramPressureSetpointSpring");
        var supplyPressureGauge = diagram.FindControl<Border>("DiagramSupplyPressureGauge");
        var mainPressureGauge = diagram.FindControl<Border>("DiagramMainPressureGauge");
        var dutPressureGauge = diagram.FindControl<Border>("DiagramDutPressureGauge");
        var dutHousing = diagram.FindControl<Border>("DiagramDutHousing");
        var dutPressureLine = diagram.FindControl<AvaloniaLine>("DiagramDutPressureLine");
        var supplyPressureNode = diagram.FindControl<AvaloniaEllipse>("DiagramSupplyPressureNode");
        var mainPressureNode = diagram.FindControl<AvaloniaEllipse>("DiagramMainPressureNode");
        var exhaustBranchNode = diagram.FindControl<AvaloniaEllipse>("DiagramExhaustBranchNode");
        var supplyDirection = diagram.FindControl<AvaloniaPolygon>("DiagramSupplyDirection");
        var atmosphereDirection = diagram.FindControl<AvaloniaPolygon>("DiagramAtmosphereDirection");
        var supplyEndpoint = diagram.FindControl<Border>("DiagramSupplyEndpoint");
        var atmosphereEndpoint = diagram.FindControl<Border>("DiagramAtmosphereEndpoint");
        var supplyLabel = diagram.FindControl<TextBlock>("DiagramSupplyLabel");
        var filterLabel = diagram.FindControl<TextBlock>("DiagramFilterLabel");
        var regulatorLabel = diagram.FindControl<TextBlock>("DiagramRegulatorLabel");
        var inletLabel = diagram.FindControl<TextBlock>("DiagramInletLabel");
        var dutLabel = diagram.FindControl<TextBlock>("DiagramDutLabel");
        var safetyDoorName = diagram.FindControl<TextBlock>("DiagramSafetyDoorName");
        var safetyDoorLight = diagram.FindControl<AvaloniaEllipse>("DiagramSafetyDoorLight");
        var safetyDoorState = diagram.FindControl<TextBlock>("DiagramSafetyDoorState");
        var clampReadyName = diagram.FindControl<TextBlock>("DiagramClampReadyName");
        var clampReadyLight = diagram.FindControl<AvaloniaEllipse>("DiagramClampReadyLight");
        var clampReadyState = diagram.FindControl<TextBlock>("DiagramClampReadyState");
        var atmosphereLabel = diagram.FindControl<TextBlock>("DiagramAtmosphereLabel");
        var digitalInput = processView.GetVisualDescendants().OfType<DigitalInputControl>().First();
        var pointLight = digitalInput.FindControl<AvaloniaEllipse>("PointLight");
        var pointState = digitalInput.FindControl<TextBlock>("PointState");
        var cardGridWide = processView.FindControl<Grid>("CardGridWide");
        var cardGridCompact = processView.FindControl<Grid>("CardGridCompact");
        var footerStatus = processView.FindControl<Border>("FooterStatus");
        var normalStatusChip = processView.FindControl<Border>("NormalStatusChip");
        var communicationStatusChip = processView.FindControl<Border>("CommunicationStatusChip");
        var awaitingFeedbackStatusChip = processView.FindControl<Border>("AwaitingFeedbackStatusChip");

        Assert.NotNull(shellRoot);
        Assert.NotNull(topHeader);
        Assert.NotNull(productBar);
        Assert.NotNull(productInfoCard);
        Assert.NotNull(workspace);
        Assert.NotNull(bottomBar);
        Assert.NotNull(statusBar);
        Assert.NotNull(exitButton);
        Assert.NotNull(processView);
        Assert.NotNull(processScrollViewer);
        Assert.NotNull(diagram);
        Assert.NotNull(diagram.Model);
        Assert.NotNull(diagramScaleViewbox);
        Assert.NotNull(mainPipe);
        Assert.NotNull(inletValveControlSignal);
        Assert.NotNull(exhaustValveLowerPipe);
        Assert.Equal("Both", diagramScaleViewbox.StretchDirection.ToString());
        Assert.Equal(new Thickness(72, 18), diagramScaleViewbox.Margin);
        Assert.NotEmpty(analogInputs);
        Assert.Equal(2, valveSymbols.Length);
        Assert.NotNull(pressureSetpointCard);
        Assert.NotNull(pressureDecreaseButton);
        Assert.NotNull(pressureIncreaseButton);
        Assert.NotNull(pressureSetpointInput);
        Assert.NotNull(pressureSetpointSignal);
        Assert.NotNull(pressureSetpointSpring);
        Assert.NotNull(supplyPressureGauge);
        Assert.NotNull(mainPressureGauge);
        Assert.NotNull(dutPressureGauge);
        Assert.NotNull(dutHousing);
        Assert.NotNull(dutPressureLine);
        Assert.NotNull(supplyPressureNode);
        Assert.NotNull(mainPressureNode);
        Assert.NotNull(exhaustBranchNode);
        Assert.NotNull(supplyDirection);
        Assert.NotNull(atmosphereDirection);
        Assert.NotNull(supplyEndpoint);
        Assert.NotNull(atmosphereEndpoint);
        Assert.NotNull(supplyLabel);
        Assert.NotNull(filterLabel);
        Assert.NotNull(regulatorLabel);
        Assert.NotNull(inletLabel);
        Assert.NotNull(dutLabel);
        Assert.NotNull(safetyDoorName);
        Assert.NotNull(safetyDoorLight);
        Assert.NotNull(safetyDoorState);
        Assert.NotNull(clampReadyName);
        Assert.NotNull(clampReadyLight);
        Assert.NotNull(clampReadyState);
        Assert.NotNull(atmosphereLabel);
        Assert.NotNull(pointLight);
        Assert.NotNull(pointState);
        var inletValveSymbol = Assert.Single(valveSymbols, valve => !valve.IsVertical);
        var exhaustValveSymbol = Assert.Single(valveSymbols, valve => valve.IsVertical);
        Assert.InRange(inletValveSymbol.Bounds.Width, 105, 107);
        Assert.InRange(inletValveSymbol.Bounds.Height, 67, 69);
        Assert.InRange(exhaustValveSymbol.Bounds.Width, 91, 93);
        Assert.InRange(exhaustValveSymbol.Bounds.Height, 83, 85);
        Assert.All(analogInputs, analogInput =>
        {
            Assert.NotNull(analogInput.Model);
            Assert.Same(analogInput.Model, analogInput.DataContext);
        });
        Assert.NotNull(cardGridWide);
        Assert.NotNull(cardGridCompact);
        Assert.NotNull(footerStatus);
        Assert.NotNull(normalStatusChip);
        Assert.NotNull(communicationStatusChip);
        Assert.NotNull(awaitingFeedbackStatusChip);
        Assert.True(shellRoot.Bounds.Width > 900);
        Assert.InRange(topHeader.Bounds.Height, 64, 68);
        Assert.True(productBar.IsVisible);
        Assert.InRange(productBar.Bounds.Width - productInfoCard.Bounds.Width, 23, 25);
        Assert.InRange(pressureSetpointCard.Bounds.Width, 227, 229);
        Assert.InRange(pressureSetpointCard.Bounds.Height, 63, 65);
        Assert.InRange(pressureDecreaseButton.Bounds.Width, 23, 25);
        Assert.InRange(pressureDecreaseButton.Bounds.Height, 24, 26);
        Assert.InRange(pressureIncreaseButton.Bounds.Width, 23, 25);
        Assert.InRange(pressureIncreaseButton.Bounds.Height, 24, 26);
        Assert.InRange(pressureSetpointInput.Bounds.Width, 69, 71);
        Assert.InRange(pressureSetpointInput.Bounds.Height, 28, 30);
        var pressureSetpointValue = pressureSetpointInput.GetVisualDescendants().OfType<TextBlock>().Single();
        Assert.InRange(pressureSetpointValue.FontSize, 15, 17);
        Assert.Equal(395, Canvas.GetLeft(pressureSetpointCard));
        Assert.Equal(509, pressureSetpointSignal.StartPoint.X);
        Assert.Equal(509, pressureSetpointSignal.EndPoint.X);
        Assert.Equal(82, pressureSetpointSignal.StartPoint.Y);
        Assert.Equal(114, pressureSetpointSignal.EndPoint.Y);
        Assert.Equal(910, inletValveControlSignal.StartPoint.X);
        Assert.Equal(910, inletValveControlSignal.EndPoint.X);
        Assert.Equal(84, inletValveControlSignal.StartPoint.Y);
        Assert.Equal(143, inletValveControlSignal.EndPoint.Y);
        Assert.Equal(1017, exhaustValveLowerPipe.StartPoint.X);
        Assert.Equal(1017, exhaustValveLowerPipe.EndPoint.X);
        Assert.Equal(306, exhaustValveLowerPipe.StartPoint.Y);
        Assert.Equal(404, exhaustValveLowerPipe.EndPoint.Y);
        Assert.Equal(0.56, mainPipe.ArrowPosition);
        Assert.Equal(151, dutPressureLine.StartPoint.Y);
        Assert.Equal(82, dutPressureLine.EndPoint.Y);
        Assert.Empty(dutHousing.GetVisualDescendants().OfType<AvaloniaLine>());
        Assert.Empty(dutHousing.GetVisualDescendants().OfType<AvaloniaPolyline>());
        Assert.Same(dutHousing, dutLabel.Parent);
        Assert.Equal(509, pressureSetpointSpring.Points[0].X);
        Assert.Equal(114, pressureSetpointSpring.Points[0].Y);
        Assert.Equal(509, pressureSetpointSpring.Points[^1].X);
        Assert.Equal(147, pressureSetpointSpring.Points[^1].Y);
        Assert.Equal(105, Canvas.GetTop(supplyPressureGauge));
        Assert.Equal(105, Canvas.GetTop(mainPressureGauge));
        Assert.Equal(105, Canvas.GetTop(dutPressureGauge));
        Assert.Equal(170, Canvas.GetTop(supplyPressureNode));
        Assert.Equal(170, Canvas.GetTop(mainPressureNode));
        Assert.Equal(170, Canvas.GetTop(exhaustBranchNode));
        Assert.Equal(214, Canvas.GetTop(supplyEndpoint));
        Assert.Equal(386, Canvas.GetTop(atmosphereEndpoint));
        Assert.True(supplyDirection.Points[1].X > supplyDirection.Points[0].X);
        Assert.True(atmosphereDirection.Points[1].X > atmosphereDirection.Points[0].X);
        Assert.Equal(VerticalAlignment.Center, supplyLabel.VerticalAlignment);
        Assert.Equal(222, Canvas.GetTop(filterLabel));
        Assert.Equal(222, Canvas.GetTop(regulatorLabel));
        Assert.Equal(222, Canvas.GetTop(inletLabel));
        Assert.Single(
            diagram.GetVisualDescendants().OfType<TextBlock>(),
            text => string.Equals(text.Text, "排气阀", StringComparison.Ordinal));
        Assert.True(safetyDoorName.Bounds.Width >= 40);
        Assert.True(clampReadyName.Bounds.Width >= 54);
        Assert.True(atmosphereLabel.Bounds.Width >= 50);
        Assert.True(atmosphereLabel.Bounds.Right <= 1360);
        Assert.Equal(VerticalAlignment.Center, safetyDoorLight.VerticalAlignment);
        Assert.Equal(VerticalAlignment.Center, safetyDoorState.VerticalAlignment);
        Assert.Equal(VerticalAlignment.Center, clampReadyLight.VerticalAlignment);
        Assert.Equal(VerticalAlignment.Center, clampReadyState.VerticalAlignment);
        Assert.InRange(Math.Abs(safetyDoorLight.Bounds.Center.Y - safetyDoorState.Bounds.Center.Y), 0, 1);
        Assert.InRange(Math.Abs(clampReadyLight.Bounds.Center.Y - clampReadyState.Bounds.Center.Y), 0, 1);
        Assert.Equal(VerticalAlignment.Center, pointLight.VerticalAlignment);
        Assert.Equal(VerticalAlignment.Center, pointState.VerticalAlignment);
        Assert.InRange(Math.Abs(pointLight.Bounds.Center.Y - pointState.Bounds.Center.Y), 0, 1);
        Assert.True(workspace.Bounds.Height > 350);
        Assert.False(bottomBar.IsVisible);
        Assert.False(statusBar.IsVisible);
        Assert.InRange(exitButton.Bounds.Height, 46, 50);
        Assert.True(diagram.Bounds.Width > 300);
        Assert.True(diagram.Bounds.Height > 300);
        var compactLayout = processView.Bounds.Width < 1320;
        Assert.Equal(!compactLayout, cardGridWide.IsVisible);
        Assert.Equal(compactLayout, cardGridCompact.IsVisible);
        Assert.Equal(
            compactLayout ? "Auto" : "Disabled",
            processScrollViewer.VerticalScrollBarVisibility.ToString());
        var visibleCardGrid = compactLayout ? cardGridCompact : cardGridWide;
        Assert.True(visibleCardGrid.Bounds.Width > 600);
        Assert.True(visibleCardGrid.Bounds.Height > 200);
        Assert.True(footerStatus.IsVisible);
        Assert.InRange(footerStatus.Bounds.Height, 54, 58);
        Assert.InRange(normalStatusChip.Bounds.Width, 103, 105);
        Assert.InRange(communicationStatusChip.Bounds.Width, 127, 129);
        Assert.InRange(awaitingFeedbackStatusChip.Bounds.Width, 127, 129);
        Assert.All(
            new[] { normalStatusChip, communicationStatusChip, awaitingFeedbackStatusChip },
            chip => Assert.InRange(chip.Bounds.Height, 37, 39));
        if (!compactLayout)
        {
            Assert.True(
                processScrollViewer.Extent.Height <= processScrollViewer.Viewport.Height + 1,
                $"宽屏工艺页不应产生纵向溢出：extent={processScrollViewer.Extent}, viewport={processScrollViewer.Viewport}");
            var footerBottom = footerStatus.TranslatePoint(
                new Point(0, footerStatus.Bounds.Height),
                processView);
            Assert.NotNull(footerBottom);
            Assert.True(
                footerBottom!.Value.Y <= processView.Bounds.Height + 1,
                $"宽屏统一状态栏不应超出可视区：footerBottom={footerBottom}, processView={processView.Bounds}");
        }
        if (height >= 992)
        {
            Assert.True(
                footerStatus.Bounds.Bottom <= workspace.Bounds.Bottom + 1,
                $"统一状态栏超出工作区：footer={footerStatus.Bounds}, workspace={workspace.Bounds}");
        }
        Assert.Contains(
            processView.GetVisualDescendants().OfType<TextBlock>(),
            text => string.Equals(text.Text, "气路工艺图", StringComparison.Ordinal));
        Assert.DoesNotContain(
            processView.GetVisualDescendants().OfType<TextBlock>(),
            text => string.Equals(text.Text, "工艺监控", StringComparison.Ordinal));
        Assert.DoesNotContain(
            processView.GetVisualDescendants().OfType<TextBlock>(),
            text => string.Equals(text.Text, "气路与点位监控", StringComparison.Ordinal));
        Assert.DoesNotContain(
            processView.GetVisualDescendants().OfType<Button>(),
            button => string.Equals(button.Content?.ToString(), "演示数据", StringComparison.Ordinal));
        Assert.Contains(
            productBar.GetVisualDescendants().OfType<TextBlock>(),
            text => string.Equals(text.Text, "产品类型", StringComparison.Ordinal));
        Assert.Contains(
            productBar.GetVisualDescendants().OfType<TextBlock>(),
            text => string.Equals(text.Text, "产品型号", StringComparison.Ordinal));
        Assert.Contains(
            productBar.GetVisualDescendants().OfType<TextBlock>(),
            text => string.Equals(text.Text, "产品信息已确认", StringComparison.Ordinal));
        Assert.Contains(
            productBar.GetVisualDescendants().OfType<TextBox>(),
            textBox => string.Equals(textBox.Text, "CP20260916001", StringComparison.Ordinal));
        Assert.Contains(
            productBar.GetVisualDescendants().OfType<Button>(),
            button => string.Equals(button.Content?.ToString(), "确认选择", StringComparison.Ordinal));
        Assert.Contains(
            processView.GetVisualDescendants().OfType<TextBlock>(),
            text => string.Equals(text.Text, "0.628", StringComparison.Ordinal));
        Assert.Contains(
            processView.GetVisualDescendants().OfType<TextBlock>(),
            text => string.Equals(text.Text, "0.650", StringComparison.Ordinal));
        Assert.Contains(
            processView.GetVisualDescendants().OfType<TextBlock>(),
            text => string.Equals(text.Text, "已开启", StringComparison.Ordinal));
        Assert.Contains(
            processView.GetVisualDescendants().OfType<TextBlock>(),
            text => string.Equals(text.Text, "已闭合", StringComparison.Ordinal));
        Assert.Contains(
            processView.GetVisualDescendants().OfType<TextBlock>(),
            text => string.Equals(text.Text, "已到位", StringComparison.Ordinal));

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

                if (item.Page is ProcessMonitorViewModel)
                {
                    Assert.False(statusBar.IsVisible, $"工艺页不应重复显示全局状态条：{item.DisplayTitle}。");
                    return;
                }

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
