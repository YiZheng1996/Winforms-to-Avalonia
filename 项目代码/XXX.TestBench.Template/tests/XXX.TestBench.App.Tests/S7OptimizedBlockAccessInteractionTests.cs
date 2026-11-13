using XXX.TestBench.App.ViewModels;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.App.Tests;

public sealed class S7OptimizedBlockAccessInteractionTests
{
    [Fact]
    public void PointEditor_S7HardwareDbAddress_ShowsHintAndRequiresOneConfirmation()
    {
        var device = new DevicePointDeviceChoice(
            Guid.NewGuid().ToString("D"),
            "DEV_S7",
            "测试 PLC",
            DriverKeyCatalog.SiemensS7,
            DeviceMode.Hardware,
            "S7-1200",
            "如：DB1.DBD0",
            "请填写 PLC 地址");
        var context = new DevicePointEditContext(
            "PLC 通道",
            device,
            new HashSet<DevicePointDataType> { DevicePointDataType.Decimal },
            Array.Empty<DevicePointGroupChoice>(),
            null,
            GroupLocked: true);
        var viewModel = new DevicePointDialogViewModel(false, null, context)
        {
            PointName = "入口压力",
            Address = "DB1.DBD0"
        };

        Assert.True(viewModel.HasS7OptimizedBlockAccessHint);
        Assert.True(viewModel.RequiresS7OptimizedBlockAccessConfirmation);
        Assert.Contains("DB1.DBD0", viewModel.S7OptimizedBlockAccessShortMessage, StringComparison.Ordinal);
        Assert.Contains("TIA Portal", viewModel.S7OptimizedBlockAccessDetailMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("driverKey", viewModel.S7OptimizedBlockAccessDetailMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PointEditor_NonDbAddress_DoesNotRequireConfirmation()
    {
        var device = new DevicePointDeviceChoice(
            Guid.NewGuid().ToString("D"),
            "DEV_S7",
            "测试 PLC",
            DriverKeyCatalog.SiemensS7,
            DeviceMode.Hardware,
            "S7-1200");
        var context = new DevicePointEditContext(
            "PLC 通道",
            device,
            new HashSet<DevicePointDataType> { DevicePointDataType.Decimal },
            Array.Empty<DevicePointGroupChoice>(),
            null,
            GroupLocked: true);
        var viewModel = new DevicePointDialogViewModel(false, null, context)
        {
            PointName = "启动",
            Address = "M10.1"
        };

        Assert.False(viewModel.HasS7OptimizedBlockAccessHint);
        Assert.False(viewModel.RequiresS7OptimizedBlockAccessConfirmation);
    }

    [Fact]
    public void ImportPreview_S7HardwareDbAddress_ShowsProminentHintAndRequiresConfirmation()
    {
        var device = CreateDevice(DeviceMode.Hardware);
        var point = CreatePoint(device, "DB1.DBW0");
        var plan = new DevicePointImportPlanner().Build(
            new[] { new DevicePointImportRow(2, point) },
            Array.Empty<PointsConfig.PointEntry>(),
            Array.Empty<PointsConfig.PointGroupEntry>());
        var preview = new DevicePointImportPreviewViewModel(
            plan,
            currentPointCount: 0,
            groups: Array.Empty<PointsConfig.PointGroupEntry>(),
            devices: new[] { device });

        Assert.True(preview.HasS7OptimizedBlockAccessNotice);
        Assert.True(preview.RequiresS7OptimizedBlockAccessConfirmation);
        Assert.Contains("DB1.DBW0", preview.S7OptimizedBlockAccessShortMessage, StringComparison.Ordinal);
        Assert.Contains("优化的块访问", preview.S7OptimizedBlockAccessShortMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("rawDataType", preview.S7OptimizedBlockAccessDetailMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IsImplemented", preview.S7OptimizedBlockAccessDetailMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ImportPreview_S7SimulationDbAddress_StillShowsNotice()
    {
        var device = CreateDevice(DeviceMode.Simulation);
        var point = CreatePoint(device, "DB1.DBW0");
        var plan = new DevicePointImportPlanner().Build(
            new[] { new DevicePointImportRow(2, point) },
            Array.Empty<PointsConfig.PointEntry>(),
            Array.Empty<PointsConfig.PointGroupEntry>());
        var preview = new DevicePointImportPreviewViewModel(
            plan,
            currentPointCount: 0,
            groups: Array.Empty<PointsConfig.PointGroupEntry>(),
            devices: new[] { device });

        Assert.True(preview.HasS7OptimizedBlockAccessNotice);
        Assert.True(preview.RequiresS7OptimizedBlockAccessConfirmation);
    }
    [Fact]
    public void ImportPreview_NonDbAddress_DoesNotShowOptimizedBlockAccessNotice()
    {
        var device = CreateDevice(DeviceMode.Hardware);
        var point = CreatePoint(device, "VW5022");
        var plan = new DevicePointImportPlanner().Build(
            new[] { new DevicePointImportRow(2, point) },
            Array.Empty<PointsConfig.PointEntry>(),
            Array.Empty<PointsConfig.PointGroupEntry>());
        var preview = new DevicePointImportPreviewViewModel(
            plan,
            currentPointCount: 0,
            groups: Array.Empty<PointsConfig.PointGroupEntry>(),
            devices: new[] { device });

        Assert.False(preview.HasS7OptimizedBlockAccessNotice);
        Assert.False(preview.RequiresS7OptimizedBlockAccessConfirmation);
    }

    [Fact]
    public async Task PointListScope_ShowsShortS7DbAbsoluteNotice()
    {
        using var harness = AppTestHarness.Create("Hardware", grouped: true);
        var actor = await harness.AdminActorAsync();
        var viewModel = new DevicePointManagementViewModel(harness.Services, actor);
        await viewModel.LoadAsync();
        var channel = viewModel.TreeNodes.Single().Children.Single(node => node.Code == "CH_SIM");
        viewModel.SelectedTreeNode = channel.Children.Single(node => node.Code == "DEV_A");

        Assert.True(viewModel.HasS7OptimizedBlockAccessScopeNotice);
        Assert.Contains("DB1.DBD0", viewModel.S7OptimizedBlockAccessScopeNotice, StringComparison.Ordinal);
        Assert.DoesNotContain("rawDataType", viewModel.S7OptimizedBlockAccessScopeNotice, StringComparison.OrdinalIgnoreCase);
    }
    private static DeviceConfig.DeviceEntry CreateDevice(DeviceMode mode)
        => new()
        {
            Id = Guid.NewGuid().ToString("D"),
            Code = "DEV_S7",
            Name = "测试 PLC",
            ChannelId = Guid.NewGuid().ToString("D"),
            DriverKey = DriverKeyCatalog.SiemensS7,
            Model = "S7-1200",
            DeviceMode = mode
        };

    private static PointsConfig.PointEntry CreatePoint(
        DeviceConfig.DeviceEntry device,
        string address)
        => new()
        {
            Id = Guid.NewGuid().ToString("D"),
            Code = "P_S7",
            Name = "压力",
            DeviceId = device.Id,
            DeviceCode = device.Code,
            Address = address,
            Protocol = "SiemensS7",
            DataType = "Float32",
            RawDataType = "Float32"
        };
}