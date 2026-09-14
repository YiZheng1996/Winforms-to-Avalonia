using XXX.TestBench.App.ViewModels;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Devices.Modbus;

namespace XXX.TestBench.App.Tests;

public sealed class DevicePointDialogViewModelTests
{
    [Fact]
    public void SimplifiedDialogFiltersTypesAndLocksEditOwnership()
    {
        var device = new DevicePointDeviceChoice(
            "10000000-0000-0000-0000-000000000001",
            "DEV_A",
            "设备 A",
            "simulation",
            true);
        var group = new DevicePointGroupChoice(
            "20000000-0000-0000-0000-000000000001",
            device.Id,
            "PRESSURE",
            "压力",
            10);
        var context = new DevicePointEditContext(
            "仿真通道",
            device,
            new HashSet<DevicePointDataType> { DevicePointDataType.Double },
            new[] { group },
            group,
            GroupLocked: true);
        var current = new PointsConfig.PointEntry
        {
            Id = Guid.NewGuid().ToString(),
            Code = "A_PRESSURE",
            Name = "入口压力",
            Protocol = "Simulation",
            DeviceId = device.Id,
            GroupId = group.Id,
            Address = "sim.a.pressure",
            DataType = "Double",
            RawDataType = "Double"
        };

        var vm = new DevicePointDialogViewModel(true, current, context);

        Assert.Equal($"{context.DevicePath} / 压力", vm.PathText);
        Assert.False(vm.UseGroupSelection);
        Assert.Equal("压力", vm.CurrentGroupText);
        Assert.Single(vm.DataTypeOptions);
        Assert.Equal(DevicePointDataType.Double, vm.DataTypeOptions[0].Value);
        Assert.True(vm.TryBuildResult(out var result), vm.ValidationMessage);
        Assert.Equal(current.Id, result.PointId);
        Assert.Equal(current.Code, result.Code);
        Assert.Equal(group.Id, result.GroupId);
    }

    [Fact]
    public void NewSimplifiedDialog_GeneratesInternalIdentityWithoutPointTagInput()
    {
        var device = new DevicePointDeviceChoice(
            "10000000-0000-0000-0000-000000000001",
            "DEV_A",
            "设备 A",
            "simulation",
            true);
        var group = new DevicePointGroupChoice(
            "20000000-0000-0000-0000-000000000001",
            device.Id,
            "PRESSURE",
            "压力",
            10);
        var context = new DevicePointEditContext(
            "仿真通道",
            device,
            new HashSet<DevicePointDataType> { DevicePointDataType.Double },
            new[] { group },
            group,
            GroupLocked: true);
        var vm = new DevicePointDialogViewModel(false, null, context)
        {
            PointName = "入口压力",
            Address = "sim.a.pressure"
        };

        var success = vm.TryBuildResult(out var result);

        Assert.True(success, vm.ValidationMessage);
        Assert.True(Guid.TryParse(result.PointId, out _));
        Assert.StartsWith("PT_", result.Code, StringComparison.Ordinal);
        Assert.Equal("入口压力", result.Name);
        Assert.Equal(group.Id, result.GroupId);
        _ = result.ToEntry();
        Assert.True(string.IsNullOrEmpty(vm.PointTag));
    }

    [Fact]
    public void DataTypeOptions_UseKepServerStorageNamesOnly()
    {
        var device = new DevicePointDeviceChoice(
            "10000000-0000-0000-0000-000000000001",
            "DEV_A",
            "设备 A",
            "simulation",
            true);
        var allowed = new HashSet<DevicePointDataType>
        {
            DevicePointDataType.Char,
            DevicePointDataType.Byte,
            DevicePointDataType.Int16,
            DevicePointDataType.UInt16,
            DevicePointDataType.Int32,
            DevicePointDataType.UInt32,
            DevicePointDataType.Float32,
            DevicePointDataType.Double
        };
        var context = new DevicePointEditContext(
            "仿真通道",
            device,
            allowed,
            Array.Empty<DevicePointGroupChoice>(),
            null,
            GroupLocked: true);
        var vm = new DevicePointDialogViewModel(false, null, context);

        Assert.Equal(
            new[] { "字符", "字节", "短整型", "字", "长整型", "双字", "浮点型", "双精度" },
            vm.DataTypeOptions.Select(option => option.DisplayName));
    }

    [Fact]
    public void DataTypeOptions_ExposeStringAndBool()
    {
        var device = new DevicePointDeviceChoice(
            "10000000-0000-0000-0000-000000000001",
            "DEV_A",
            "设备 A",
            "simulation",
            true);
        var context = new DevicePointEditContext(
            "仿真通道",
            device,
            new HashSet<DevicePointDataType>
            {
                DevicePointDataType.String,
                DevicePointDataType.Bool
            },
            Array.Empty<DevicePointGroupChoice>(),
            null,
            GroupLocked: true);

        var vm = new DevicePointDialogViewModel(false, null, context);

        Assert.Equal(
            new[] { DevicePointDataType.String, DevicePointDataType.Bool },
            vm.DataTypeOptions.Select(option => option.Value));
        Assert.Equal(new[] { "字符串", "布尔量" },
            vm.DataTypeOptions.Select(option => option.DisplayName));
    }

    [Theory]
    [InlineData("simulation", "", "sim.pressure")]
    [InlineData("modbus-rtu", "", "HR:0")]
    [InlineData("modbus-tcp", "", "HR:0")]
    [InlineData("siemens-s7", "S7-1500", "DB144.DBD88")]
    [InlineData("siemens-s7", "S7-200 SMART", "VW5022")]
    public void AddressHelp_FollowsCurrentDeviceProtocol(
        string driverKey,
        string model,
        string expectedExample)
    {
        var device = new DevicePointDeviceChoice(
            "10000000-0000-0000-0000-000000000001",
            "DEV_A",
            "设备 A",
            driverKey,
            true,
            model,
            driverKey == "siemens-s7" ? $"如：{expectedExample}" : string.Empty,
            driverKey == "siemens-s7" ? $"{model} 示例：{expectedExample}" : string.Empty);
        var context = new DevicePointEditContext(
            "测试通道",
            device,
            new HashSet<DevicePointDataType> { DevicePointDataType.Double },
            Array.Empty<DevicePointGroupChoice>(),
            null,
            GroupLocked: true);
        var vm = new DevicePointDialogViewModel(false, null, context);

        Assert.Contains(expectedExample, vm.AddressWatermark, StringComparison.Ordinal);
        Assert.Contains(expectedExample, vm.AddressHint, StringComparison.Ordinal);
    }

    [Fact]
    public void AccessOptions_AreReadOnlyAndReadWrite()
    {
        var device = new DevicePointDeviceChoice(
            "10000000-0000-0000-0000-000000000001",
            "DEV_A",
            "设备 A",
            "simulation",
            true);
        var context = new DevicePointEditContext(
            "仿真通道",
            device,
            new HashSet<DevicePointDataType> { DevicePointDataType.Double },
            Array.Empty<DevicePointGroupChoice>(),
            null,
            GroupLocked: true);
        var vm = new DevicePointDialogViewModel(false, null, context);

        Assert.Equal(new[] { "只读", "读写" }, vm.WritePolicyOptions.Select(option => option.DisplayName));
        Assert.Equal("只读", vm.SelectedWritePolicy?.DisplayName);

        vm.SelectedWritePolicy = vm.WritePolicyOptions.Single(option => option.Key == "ReadWrite");

        Assert.True(vm.IsWritable);
        Assert.Equal(WriteRiskLevel.Normal, vm.SelectedRiskLevel);
    }

    [Fact]
    public void HighRiskPolicy_IsPreservedWhileAccessOptionsStayTwoState()
    {
        var device = new DevicePointDeviceChoice(
            "10000000-0000-0000-0000-000000000001",
            "DEV_A",
            "设备 A",
            "simulation",
            true);
        var context = new DevicePointEditContext(
            "仿真通道",
            device,
            new HashSet<DevicePointDataType> { DevicePointDataType.Double },
            Array.Empty<DevicePointGroupChoice>(),
            null,
            GroupLocked: true);
        var current = new PointsConfig.PointEntry
        {
            Id = Guid.NewGuid().ToString(),
            Code = "DO_Start",
            Name = "启动命令",
            Protocol = "Simulation",
            DeviceId = device.Id,
            Address = "sim.start",
            DataType = "Double",
            RawDataType = "Double",
            IsWritable = true,
            RiskLevel = WriteRiskLevel.HighRisk
        };
        var vm = new DevicePointDialogViewModel(true, current, context);

        var success = vm.TryBuildResult(out var result);

        Assert.True(success);
        Assert.True(result.IsWritable);
        Assert.Equal(WriteRiskLevel.HighRisk, result.RiskLevel);
        Assert.Equal("读写", vm.SelectedWritePolicy?.DisplayName);
        Assert.Contains("二次确认", vm.WriteSafetyMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void NewModbusBitPoint_UsesStructuredAddressAndBoolStorage()
    {
        var device = new DevicePointDeviceChoice(
            "10000000-0000-0000-0000-000000000010",
            "DEV_RTU",
            "RTU 设备",
            "modbus-rtu",
            DeviceMode.Hardware,
            "GENERIC_MODBUS_RTU");
        var context = new DevicePointEditContext(
            "串口通道",
            device,
            new HashSet<DevicePointDataType>
            {
                DevicePointDataType.Bool,
                DevicePointDataType.Int16,
                DevicePointDataType.UInt16,
                DevicePointDataType.Int32,
                DevicePointDataType.UInt32,
                DevicePointDataType.Float32,
                DevicePointDataType.Double
            },
            Array.Empty<DevicePointGroupChoice>(),
            null,
            GroupLocked: true);
        var vm = new DevicePointDialogViewModel(false, null, context)
        {
            PointName = "运行状态",
            ModbusOffsetText = "7"
        };
        vm.SelectedModbusArea = vm.ModbusAreaOptions.Single(option =>
            option.DisplayName.StartsWith("Coil", StringComparison.Ordinal));

        vm.SelectedDataTypeOption = vm.DataTypeOptions.Single(option => option.Value == DevicePointDataType.Bool);
        vm.SelectedWritePolicy = vm.WritePolicyOptions.Single(option => option.Key == "ReadWrite");

        Assert.True(vm.TryBuildResult(out var result), vm.ValidationMessage);
        Assert.Equal("C:7", result.Address);
        Assert.Equal(DevicePointDataType.Bool, result.DataType);
        Assert.Equal("Bool", result.RawDataType);
        Assert.Equal("C", result.AddressDefinition?.Area);
        Assert.Equal(7, result.AddressDefinition?.Offset);
        Assert.Equal(ByteOrder.BigEndian, result.DecodeOptions?.ByteOrder);
        Assert.Equal(WordOrder.None, result.DecodeOptions?.WordOrder);

    }

    [Fact]
    public void ModbusAreaChange_FiltersTypesAndForcesReadOnlyInputArea()
    {
        var device = new DevicePointDeviceChoice(
            "10000000-0000-0000-0000-000000000011",
            "DEV_TCP",
            "TCP 设备",
            "modbus-tcp",
            DeviceMode.Simulation,
            "GENERIC_MODBUS_TCP");
        var context = new DevicePointEditContext(
            "TCP 通道",
            device,
            new HashSet<DevicePointDataType>
            {
                DevicePointDataType.Bool,
                DevicePointDataType.Int16,
                DevicePointDataType.UInt16,
                DevicePointDataType.Int32,
                DevicePointDataType.UInt32,
                DevicePointDataType.Float32,
                DevicePointDataType.Double
            },
            Array.Empty<DevicePointGroupChoice>(),
            null,
            GroupLocked: true);
        var vm = new DevicePointDialogViewModel(false, null, context)
        {
            PointName = "输入寄存器值",
            ModbusOffsetText = "10",
            SelectedWritePolicy = null
        };

        // 新建 Modbus 默认落在 HR，数据类型下拉不能出现 Bool。
        Assert.DoesNotContain(vm.DataTypeOptions, option => option.Value == DevicePointDataType.Bool);
        Assert.Contains(vm.DataTypeOptions, option => option.Value == DevicePointDataType.Int16);

        vm.SelectedWritePolicy = vm.WritePolicyOptions.Single(option => option.Key == "ReadWrite");
        vm.SelectedModbusArea = vm.ModbusAreaOptions.Single(option => option.Value == ModbusArea.DiscreteInput);

        Assert.Equal(new[] { DevicePointDataType.Bool }, vm.DataTypeOptions.Select(option => option.Value));
        Assert.Equal("只读", vm.SelectedWritePolicy?.DisplayName);
        Assert.False(vm.IsModbusAccessSelectionEnabled);
        Assert.True(vm.IsModbusReadOnlyArea);
        Assert.False(vm.CanSave); // 区域切换会清空不再适用的 Int16，必须重新明确选择 Bool。

        vm.SelectedDataTypeOption = vm.DataTypeOptions.Single();
        Assert.True(vm.CanSave);
    }
}
