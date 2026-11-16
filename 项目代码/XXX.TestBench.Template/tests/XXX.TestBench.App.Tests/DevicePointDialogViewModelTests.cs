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
            DeviceMode.Simulation);
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
            DeviceMode.Simulation);
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
            DeviceMode.Simulation);
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
            DeviceMode.Simulation);
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

    [Fact]
    public void ModbusRtuAreaChoices_UseChineseCustomerLabelsAndHideProtocolCodes()
    {
        var device = new DevicePointDeviceChoice(
            "10000000-0000-0000-0000-000000000012",
            "DEV_RTU",
            "串口设备",
            "modbus-rtu",
            DeviceMode.Hardware,
            "GENERIC_MODBUS_RTU",
            ModbusUnitId: 1,
            ModbusDataOrder: new ModbusDataOrderOptions
            {
                ByteOrder = ByteOrder.BigEndian,
                WordOrder = WordOrder.HighWordFirst
            });
        var context = new DevicePointEditContext(
            "串口通道",
            device,
            new HashSet<DevicePointDataType>
            {
                DevicePointDataType.Bool,
                DevicePointDataType.Int16,
                DevicePointDataType.UInt16
            },
            Array.Empty<DevicePointGroupChoice>(),
            null,
            GroupLocked: true);
        var vm = new DevicePointDialogViewModel(false, null, context)
        {
            ModbusOffsetText = "7"
        };

        Assert.Equal(
            new[] { "线圈", "离散输入", "保持寄存器", "输入寄存器" },
            vm.ModbusAreaOptions.Select(option => option.DisplayName));
        Assert.All(vm.ModbusAreaOptions, option =>
        {
            Assert.DoesNotContain("Coil", option.DisplayName, StringComparison.Ordinal);
            Assert.DoesNotContain("Input", option.DisplayName, StringComparison.Ordinal);
            Assert.DoesNotContain("Register", option.DisplayName, StringComparison.Ordinal);
            Assert.DoesNotContain("Bool", option.Description, StringComparison.Ordinal);
        });

        vm.SelectedModbusArea = vm.ModbusAreaOptions.Single(option => option.Value == ModbusArea.Coil);

        Assert.Equal("已生成地址：线圈 7", vm.ModbusCanonicalAddress);
        Assert.DoesNotContain("C:", vm.ModbusAddressHint, StringComparison.Ordinal);
        Assert.DoesNotContain("HR:", vm.AddressWatermark, StringComparison.Ordinal);
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
            DeviceMode.Simulation,
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

        if (driverKey is "modbus-rtu" or "modbus-tcp")
        {
            Assert.DoesNotContain(expectedExample, vm.AddressWatermark, StringComparison.Ordinal);
            Assert.DoesNotContain(expectedExample, vm.AddressHint, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains(expectedExample, vm.AddressWatermark, StringComparison.Ordinal);
            Assert.Contains(expectedExample, vm.AddressHint, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AccessOptions_AreReadOnlyAndReadWrite()
    {
        var device = new DevicePointDeviceChoice(
            "10000000-0000-0000-0000-000000000001",
            "DEV_A",
            "设备 A",
            "simulation",
            DeviceMode.Simulation);
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
            DeviceMode.Simulation);
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
            "GENERIC_MODBUS_RTU",
            ModbusUnitId: 7,
            ModbusDataOrder: new ModbusDataOrderOptions
            {
                ByteOrder = ByteOrder.BigEndian,
                WordOrder = WordOrder.HighWordFirst
            });
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
        vm.SelectedModbusArea = vm.ModbusAreaOptions.Single(option => option.Value == ModbusArea.Coil);

        vm.SelectedDataTypeOption = vm.DataTypeOptions.Single(option => option.Value == DevicePointDataType.Bool);
        vm.SelectedWritePolicy = vm.WritePolicyOptions.Single(option => option.Key == "ReadWrite");

        Assert.True(vm.TryBuildResult(out var result), vm.ValidationMessage);
        Assert.Equal("C:7", result.Address);
        Assert.Equal(DevicePointDataType.Bool, result.DataType);
        Assert.Equal(DevicePointDataType.Bool, result.DataType);
        Assert.Equal("C", result.AddressDefinition?.Area);
        Assert.Equal(7, result.AddressDefinition?.Offset);
        Assert.Null(result.S7DecodeOptions);
        Assert.Equal("大端模式 · 高字在前", vm.ModbusDataOrderText);

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
            "GENERIC_MODBUS_TCP",
            ModbusUnitId: 1,
            ModbusDataOrder: new ModbusDataOrderOptions
            {
                ByteOrder = ByteOrder.BigEndian,
                WordOrder = WordOrder.HighWordFirst
            });
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

    [Fact]
    public void XinjeEditor_ShowsNativeAddressAndFiltersTypesAsPrefixChanges()
    {
        var device = XinjeDeviceChoice();
        var context = XinjeContext(device);
        var vm = new DevicePointDialogViewModel(false, null, context)
        {
            PointName = "现场点位",
            Address = "D100"
        };

        Assert.False(vm.IsStructuredModbusAddressVisible);
        Assert.True(vm.IsStandardAddressVisible);
        Assert.Equal("如：D100、HD1000、M400、HM40", vm.AddressWatermark);
        Assert.Equal(
            new[]
            {
                DevicePointDataType.UInt16,
                DevicePointDataType.Float32,
                DevicePointDataType.Int32,
                DevicePointDataType.UInt32
            }.OrderBy(type => type),
            vm.DataTypeOptions.Select(option => option.Value).OrderBy(type => type));

        vm.SelectedDataTypeOption = vm.DataTypeOptions.Single(option => option.Value == DevicePointDataType.Float32);
        vm.Address = "M400";
        Assert.Equal(new[] { DevicePointDataType.Bool }, vm.DataTypeOptions.Select(option => option.Value));
        Assert.Null(vm.SelectedDataTypeOption);
        vm.SelectedDataTypeOption = vm.DataTypeOptions.Single();

        Assert.True(vm.TryBuildResult(out var bitPoint), vm.ValidationMessage);
        Assert.Equal("M400", bitPoint.Address);
        Assert.Equal("C", bitPoint.AddressDefinition?.Area);
        Assert.Equal(400, bitPoint.AddressDefinition?.Offset);
        Assert.Equal(DevicePointDataType.Bool, bitPoint.DataType);

        vm.Address = string.Empty;
        Assert.False(vm.CanSave);

        vm.Address = "X1";
        Assert.False(vm.CanSave);
        Assert.Contains("D/HD/M/HM", vm.AddressHint, StringComparison.Ordinal);
        vm.Address = "HD1000";
        Assert.Equal(
            new[]
            {
                DevicePointDataType.UInt16,
                DevicePointDataType.Float32,
                DevicePointDataType.Int32,
                DevicePointDataType.UInt32
            }.OrderBy(type => type),
            vm.DataTypeOptions.Select(option => option.Value).OrderBy(type => type));
    }

    [Fact]
    public void XinjeEditorReappliesNativeAddressRulesWhenEditingOrPasting()
    {
        var device = XinjeDeviceChoice();
        var group = new DevicePointGroupChoice("group-xinje", device.Id, "DEFAULT", "未分组", 0);
        var source = new PointsConfig.PointEntry
        {
            Id = "point-xinje-copy-source",
            Code = "HD_SPEED",
            Name = "高速压力",
            Protocol = "ModbusTcp",
            DeviceId = device.Id,
            GroupId = group.Id,
            GroupCode = group.Code,
            Address = "HD1000",
            AddressDefinition = new PointAddressDefinition { Area = "HR", Offset = 42088 },
            DataType = "Float32",
        };
        var context = XinjeContext(device, group);

        var editVm = new DevicePointDialogViewModel(true, source, context);
        Assert.True(editVm.IsStandardAddressVisible);
        Assert.False(editVm.IsStructuredModbusAddressVisible);
        Assert.Equal("HD1000", editVm.Address);
        Assert.Contains(editVm.DataTypeOptions, option => option.Value == DevicePointDataType.Float32);

        editVm.Address = "M400";
        Assert.Equal(new[] { DevicePointDataType.Bool }, editVm.DataTypeOptions.Select(option => option.Value));
        Assert.Null(editVm.SelectedDataTypeOption);
        editVm.SelectedDataTypeOption = editVm.DataTypeOptions.Single();
        Assert.True(editVm.TryBuildResult(out var edited), editVm.ValidationMessage);
        Assert.Equal("M400", edited.Address);
        Assert.Equal("C", edited.AddressDefinition?.Area);
        Assert.Equal(400, edited.AddressDefinition?.Offset);

        var draft = new DevicePointPasteDraft(
            DevicePointClipboardMode.Copy,
            source.Id,
            source.DeviceId,
            source.GroupId,
            source,
            device,
            group);
        var pasteVm = new DevicePointDialogViewModel(draft, context);
        Assert.True(pasteVm.IsStandardAddressVisible);
        Assert.False(pasteVm.IsStructuredModbusAddressVisible);
        Assert.Equal("HD1000", pasteVm.Address);
        Assert.Contains(pasteVm.DataTypeOptions, option => option.Value == DevicePointDataType.Float32);

        pasteVm.Address = "HD1004";
        Assert.True(pasteVm.TryBuildResult(out var pasted), pasteVm.ValidationMessage);
        Assert.NotEqual(source.Id, pasted.PointId);
        Assert.Equal("HD1004", pasted.Address);
        Assert.Equal("HR", pasted.AddressDefinition?.Area);
        Assert.Equal(42092, pasted.AddressDefinition?.Offset);
    }

    [Fact]
    public void XinjeEditor_PreservesNativeAddressAndWritesMappedStructure()
    {
        var vm = new DevicePointDialogViewModel(false, null, XinjeContext(XinjeDeviceChoice()))
        {
            PointName = "高速压力",
            Address = "hd01000"
        };
        vm.SelectedDataTypeOption = vm.DataTypeOptions.Single(option => option.Value == DevicePointDataType.Float32);

        Assert.Contains("HR:42088", vm.ModbusCanonicalAddress, StringComparison.Ordinal);
        Assert.True(vm.TryBuildResult(out var point), vm.ValidationMessage);
        Assert.Equal("HD1000", point.Address);
        Assert.Equal("HR", point.AddressDefinition?.Area);
        Assert.Equal(42088, point.AddressDefinition?.Offset);
    }

    [Fact]
    public void XinjeEditor_RejectsPersistedAddressAndStructureMismatchUntilAddressIsEdited()
    {
        var device = XinjeDeviceChoice();
        var group = new DevicePointGroupChoice("group-xinje", device.Id, "DEFAULT", "未分组", 0);
        var current = new PointsConfig.PointEntry
        {
            Id = "point-xinje",
            Code = "HD_SPEED",
            Name = "高速压力",
            Protocol = "ModbusTcp",
            DeviceId = device.Id,
            GroupId = group.Id,
            Address = "HD1000",
            AddressDefinition = new PointAddressDefinition { Area = "HR", Offset = 1000 },
            DataType = "Float32",
        };
        var vm = new DevicePointDialogViewModel(true, current, XinjeContext(device, group));

        Assert.False(vm.CanSave);
        Assert.False(vm.TryBuildResult(out _));
        Assert.Contains("与 Address 不一致", vm.ValidationMessage, StringComparison.Ordinal);

        vm.Address = "HD1001";
        Assert.True(vm.TryBuildResult(out var corrected), vm.ValidationMessage);
        Assert.Equal("HD1001", corrected.Address);
        Assert.Equal(42089, corrected.AddressDefinition?.Offset);
    }

    [Fact]
    public void CopyPasteDialogRestoresCompleteScaleAndCreatesNewIdentity()
    {
        var device = new DevicePointDeviceChoice(
            "10000000-0000-0000-0000-000000000020",
            "DEV_SCALE",
            "量程设备",
            "simulation",
            DeviceMode.Simulation);
        var group = new DevicePointGroupChoice(
            "20000000-0000-0000-0000-000000000020",
            device.Id,
            "DEFAULT",
            "未分组",
            0);
        var context = new DevicePointEditContext(
            "仿真通道",
            device,
            new HashSet<DevicePointDataType> { DevicePointDataType.Double },
            new[] { group },
            group,
            GroupLocked: true);
        var source = new PointsConfig.PointEntry
        {
            Id = "40000000-0000-0000-0000-000000000020",
            Code = "AI_SCALE",
            Name = "压力换算",
            Protocol = "Simulation",
            DeviceId = device.Id,
            GroupId = group.Id,
            GroupCode = group.Code,
            Address = "sim.scale",
            DataType = "Double",
            RawMin = -1.25m,
            RawMax = 4.5m,
            EngMin = -10.75m,
            EngMax = 20.125m,
            Description = "源点位备注"
        };
        var sourceId = source.Id;
        var sourceAddress = source.Address;
        var draft = new DevicePointPasteDraft(
            DevicePointClipboardMode.Copy,
            source.Id,
            source.DeviceId,
            source.GroupId,
            source,
            device,
            group);
        var vm = new DevicePointDialogViewModel(draft, context)
        {
            Address = "sim.scale.copy"
        };

        Assert.True(vm.IsScalingEnabled);
        Assert.True(vm.TryBuildResult(out var result), vm.ValidationMessage);

        Assert.NotEqual(source.Id, result.PointId);
        Assert.Equal(-1.25m, result.RawMin);
        Assert.Equal(4.5m, result.RawMax);
        Assert.Equal(-10.75m, result.EngMin);
        Assert.Equal(20.125m, result.EngMax);
        Assert.Equal("sim.scale.copy", result.Address);
        Assert.Equal(sourceId, source.Id);
        Assert.Equal(sourceAddress, source.Address);
        Assert.Equal(source.Description, result.Description);
    }

    private static DevicePointDeviceChoice XinjeDeviceChoice()
    {
        var order = new ModbusDataOrderOptions
        {
            ByteOrder = ByteOrder.BigEndian,
            WordOrder = WordOrder.LowWordFirst
        };
        return new DevicePointDeviceChoice(
            "10000000-0000-0000-0000-000000000100",
            "DEV_XINJE",
            "信捷 PLC",
            "modbus-tcp",
            DeviceMode.Simulation,
            DeviceModelKeyCatalog.XinjeFieldV1,
            "如：D100、HD1000、M400、HM40",
            "仅支持现场已验证的 D/HD/M/HM；不支持寄存器位地址。",
            1,
            order);
    }

    private static DevicePointEditContext XinjeContext(
        DevicePointDeviceChoice device,
        DevicePointGroupChoice? group = null)
        => new(
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
            group is null ? Array.Empty<DevicePointGroupChoice>() : new[] { group },
            group,
            GroupLocked: true);
}
