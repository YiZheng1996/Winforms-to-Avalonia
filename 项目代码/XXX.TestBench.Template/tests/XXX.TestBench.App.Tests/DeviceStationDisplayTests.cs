using XXX.TestBench.App.ViewModels;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.App.Tests;

public sealed class DeviceStationDisplayTests
{
    [Fact]
    public async Task ModbusStationFlowsToPointPagePointEditorAndDiagnostics()
    {
        using var harness = AppTestHarness.Create(grouped: true);
        var actor = await harness.AdminActorAsync();
        var device = harness.Services.DeviceConfig.Devices.Single(item => item.Code == "DEV_A");

        var viewModel = new DevicePointManagementViewModel(harness.Services, actor);
        await viewModel.LoadAsync();
        device = harness.Services.DeviceConfig.Devices.Single(item => item.Code == "DEV_A");
        device.DriverKey = DriverKeyCatalog.ModbusTcp;
        device.ModbusUnitId = 17;

        var deviceNode = Assert.Single(
            Assert.Single(viewModel.TreeNodes).Children.Single(node => node.Code == "CH_SIM").Children,
            node => node.Code == "DEV_A");
        viewModel.SelectedTreeNode = deviceNode;

        Assert.True(viewModel.IsScopeDetailsModbusVisible);
        Assert.Equal("17", viewModel.ScopeDetailsModbusUnitIdText);
        Assert.Equal("站号 17", viewModel.Points[0].ModbusStationText);

        var context = viewModel.CreatePointDialogContext(isEdit: false);
        Assert.Contains("站号 17", context.DevicePath, StringComparison.Ordinal);

        var diagnostics = viewModel.CreateDiagnosticsViewModel();
        Assert.NotNull(diagnostics);
        Assert.NotEmpty(diagnostics!.Points);
        Assert.Equal("站号 17", diagnostics.Points[0].ModbusStationText);
    }

    [Fact]
    public async Task ScopeDetailsUsesLocalizedDeviceModelName()
    {
        using var harness = AppTestHarness.Create(grouped: true);
        var actor = await harness.AdminActorAsync();
        var viewModel = new DevicePointManagementViewModel(harness.Services, actor);
        await viewModel.LoadAsync();

        var device = harness.Services.DeviceConfig.Devices.Single(item => item.Code == "DEV_A");
        device.DriverKey = DriverKeyCatalog.ModbusRtu;
        device.Model = "GENERIC_MODBUS_RTU";
        viewModel.SelectedTreeNode = viewModel.TreeNodes.Single().Children.Single(node => node.Code == "CH_SIM")
            .Children.Single(node => node.Code == "DEV_A");

        Assert.Equal("通用 Modbus RTU 设备", viewModel.ScopeDetailsDescription);
        Assert.DoesNotContain("GENERIC_MODBUS_RTU", viewModel.ScopeDetailsDescription, StringComparison.Ordinal);
    }

    [Fact]
    public void DeviceSummariesAndSignalBindingCandidatesShowModbusStation()
    {
        var device = new DeviceConfig.DeviceEntry
        {
            Id = "device-1",
            Code = "DEV_MODBUS",
            Name = "Modbus 设备",
            DriverKey = DriverKeyCatalog.ModbusRtu,
            ModbusUnitId = 9
        };
        var row = new DeviceConfigurationRow
        {
            Entry = device,
            ChannelText = "串口通道",
            DriverText = "Modbus RTU",
            PointCount = 1
        };
        var pointChoice = new SignalBindingPointChoice(
            new PointsConfig.PointEntry { Id = "point-1", Code = "P1", Name = "压力" },
            device.Code,
            ModbusStationText: "站号 9");

        Assert.Equal("站号 9", row.ModbusStationText);
        Assert.Contains("站号 9", row.DetailsText, StringComparison.Ordinal);
        Assert.Contains("站号 9", pointChoice.DetailsText, StringComparison.Ordinal);

        var runtimePoint = new DevicePoint(
            "P1",
            DevicePointProtocol.ModbusRtu,
            "HR:0",
            DevicePointDataType.Decimal,
            false,
            WriteRiskLevel.Normal,
            null,
            null,
            null,
            null,
            "10000000-0000-0000-0000-000000000001",
            device.Id,
            "r1");
        var pointRow = PointRow.FromDevicePoint(runtimePoint, new[] { device });
        Assert.Equal("Modbus 设备", pointRow.DeviceText);
        Assert.Equal("站号 9", pointRow.ModbusStationText);

        var point = new PointsConfig.PointEntry
        {
            Id = "point-1",
            Code = "P1",
            Name = "压力",
            DeviceId = device.Id,
            DeviceCode = device.Code,
            Protocol = "ModbusRtu",
            DataType = "Decimal"
        };
        var plan = new DevicePointImportPlanner().Build(
            new[] { new DevicePointImportRow(2, point) },
            Array.Empty<PointsConfig.PointEntry>(),
            Array.Empty<PointsConfig.PointGroupEntry>());
        var preview = new DevicePointImportPreviewViewModel(
            plan,
            devices: new[] { device });

        Assert.Equal("站号 9", preview.Rows.Single().ModbusStationText);
    }
}
