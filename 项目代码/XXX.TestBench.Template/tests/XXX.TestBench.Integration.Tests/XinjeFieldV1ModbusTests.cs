using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Ports;
using XXX.TestBench.Devices.Drivers;
using XXX.TestBench.Devices.Modbus;
using XXX.TestBench.Devices.Runtime;
using Xunit;

namespace XXX.TestBench.Integration.Tests;

public sealed class XinjeFieldV1ModbusTests
{
    [Theory]
    [InlineData("D100", "D100", "HR:100")]
    [InlineData("HD1000", "HD1000", "HR:42088")]
    [InlineData("M400", "M400", "C:400")]
    [InlineData("HM40", "HM40", "C:49448")]
    [InlineData("hd01000", "HD1000", "HR:42088")]
    [InlineData(" d00100 ", "D100", "HR:100")]
    public void Parser_MapsOnlyTheFourFieldV1Areas(string source, string canonical, string expected)
    {
        var address = XinjeFieldV1AddressParser.Parse(source);

        Assert.Equal(canonical, address.Canonical);
        Assert.Equal(expected, address.ModbusAddress.Canonical);
    }

    [Theory]
    [InlineData("HD24448")]
    [InlineData("HD2147483647")]
    [InlineData("D999999999999999999999999999999999999")]
    [InlineData("")]
    [InlineData("D10.0")]
    [InlineData("100")]
    [InlineData("D-1")]
    [InlineData("HD 1")]
    [InlineData("FD0")]
    [InlineData("SD0")]
    [InlineData("SM0")]
    [InlineData("X0")]
    [InlineData("Y0")]
    [InlineData("S0")]
    public void Parser_RejectsUnsupportedOrOutOfRangeAddresses(string source)
    {
        Assert.False(XinjeFieldV1AddressParser.TryParse(source, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Throws<DomainException>(() => XinjeFieldV1AddressParser.Parse(source));
    }

    [Fact]
    public void Resolver_UsesModelAndDriverContextAndChecksPersistedStructure()
    {
        var xinje = Device();
        var native = Point("D100", DevicePointDataType.UInt16);
        Assert.True(ModbusPointAddressResolver.TryResolve(xinje, native, out var address, out _));
        Assert.Equal("HR:100", address.Canonical);

        var generic = Device();
        generic.Model = "GENERIC_MODBUS_TCP";
        Assert.False(ModbusPointAddressResolver.TryResolve(generic, native, out _, out _));

        xinje.DriverKey = DriverKeyCatalog.ModbusRtu;
        Assert.False(ModbusPointAddressResolver.TryResolve(xinje, native, out _, out var transportError));
        Assert.Contains("Modbus TCP", transportError, StringComparison.Ordinal);

        xinje.DriverKey = DriverKeyCatalog.ModbusTcp;
        native = native with
        {
            AddressDefinition = new PointAddressDefinition { Area = "HR", Offset = 0 }
        };
        Assert.False(ModbusPointAddressResolver.TryResolve(xinje, native, out _, out var mismatch));
        Assert.Contains("必须与 Address", mismatch, StringComparison.Ordinal);
    }

    [Fact]
    public void Descriptor_EnforcesFieldV1TransportOrderAndNativeTypeMatrix()
    {
        var descriptor = new ModbusTcpDriverDescriptor();
        var device = Device();
        Assert.Empty(descriptor.ValidateDevice(device, TcpChannel()));
        Assert.Contains(descriptor.DeviceModels, model => model.Key == DeviceModelKeyCatalog.XinjeFieldV1);

        device.ModbusDataOrder!.WordOrder = WordOrder.HighWordFirst;
        Assert.Contains(descriptor.ValidateDevice(device, TcpChannel()),
            issue => issue.Path == "modbusDataOrder" && issue.Message.Contains("低字在前", StringComparison.Ordinal));
        device.ModbusDataOrder.WordOrder = WordOrder.LowWordFirst;
        device.ModbusDataOrder.ByteOrder = ByteOrder.LittleEndian;
        Assert.Contains(descriptor.ValidateDevice(device, TcpChannel()), issue => issue.Path == "modbusDataOrder");
        device.ModbusDataOrder.ByteOrder = ByteOrder.BigEndian;

        var serialChannel = new ChannelEntry
        {
            Id = "10000000-0000-0000-0000-000000000002",
            Code = "SERIAL_XINJE",
            Name = "串口通道",
            TransportKind = ChannelTransportKind.Serial,
            Serial = new SerialChannelParameters { PortName = "COM1", BaudRate = 9600 }
        };
        Assert.Contains(descriptor.ValidateChannel(serialChannel), issue => issue.Path == "transportKind");

        Assert.Empty(descriptor.ValidatePoint(Entry("M400", DevicePointDataType.Bool), device));
        foreach (var type in new[] { DevicePointDataType.Float32, DevicePointDataType.UInt16,
                     DevicePointDataType.Int32, DevicePointDataType.UInt32 })
            Assert.Empty(descriptor.ValidatePoint(Entry("HD1000", type), device));
        foreach (var type in new[] { DevicePointDataType.Float32, DevicePointDataType.UInt16,
                     DevicePointDataType.Int32, DevicePointDataType.UInt32 })
            Assert.Contains(descriptor.ValidatePoint(Entry("M400", type), device), issue => issue.Path == "dataType");
        foreach (var type in new[] { DevicePointDataType.Bool, DevicePointDataType.Double, DevicePointDataType.Int16 })
            Assert.Contains(descriptor.ValidatePoint(Entry("HD1000", type), device), issue => issue.Path == "dataType");
    }

    [Fact]
    public void ConfigurationAndImportValidation_AcceptReloadedHdAddressAndRejectInconsistentStructure()
    {
        var descriptor = new ModbusTcpDriverDescriptor();
        var device = Device();
        device.DeviceMode = DeviceMode.Simulation;
        var deviceConfig = DeviceConfigFor(device);
        var valid = ConfigurationPoint(device, "HD1000", DevicePointDataType.Float32);

        Assert.Empty(DevicePointImportValidator.Validate(deviceConfig, new[] { valid }, new[] { descriptor }));
        Assert.Empty(MultiDeviceConfigurationValidator.Validate(Snapshot(device, valid), new[] { descriptor }));

        var inconsistent = ConfigurationPoint(device, "HD1000", DevicePointDataType.Float32);
        inconsistent.AddressDefinition = new PointAddressDefinition { Area = "HR", Offset = 1000 };
        inconsistent.CanonicalAddress = "HR:1000";
        Assert.Contains(DevicePointImportValidator.Validate(deviceConfig, new[] { inconsistent }, new[] { descriptor }),
            issue => issue.Message.Contains("结构化 Modbus 地址", StringComparison.Ordinal));
        Assert.Contains(MultiDeviceConfigurationValidator.Validate(Snapshot(device, inconsistent), new[] { descriptor }),
            issue => issue.Message.Contains("结构化 Modbus 地址", StringComparison.Ordinal));
    }

    [Fact]
    public void ConfigurationValidation_RejectsAliasesAndOverlappingRegisterRanges()
    {
        var descriptor = new ModbusTcpDriverDescriptor();
        var device = Device();
        device.DeviceMode = DeviceMode.Simulation;

        var aliases = new[]
        {
            ConfigurationPoint(device, "D42088", DevicePointDataType.UInt16),
            ConfigurationPoint(device, "HD1000", DevicePointDataType.UInt16)
        };
        var aliasIssues = MultiDeviceConfigurationValidator.Validate(Snapshot(device, aliases), new[] { descriptor });
        Assert.Contains(aliasIssues, issue => issue.Message.Contains("42088", StringComparison.Ordinal));

        var overlapping = new[]
        {
            ConfigurationPoint(device, "D100", DevicePointDataType.Float32),
            ConfigurationPoint(device, "D101", DevicePointDataType.UInt16)
        };
        var overlapIssues = MultiDeviceConfigurationValidator.Validate(Snapshot(device, overlapping), new[] { descriptor });
        Assert.Contains(overlapIssues, issue => issue.Message.Contains("重叠", StringComparison.Ordinal));

        var sameAddress = new[]
        {
            ConfigurationPoint(device, "D100", DevicePointDataType.UInt16),
            ConfigurationPoint(device, "d100", DevicePointDataType.UInt16)
        };
        var duplicateIssues = MultiDeviceConfigurationValidator.Validate(Snapshot(device, sameAddress), new[] { descriptor });
        Assert.Contains(duplicateIssues, issue => issue.Message.Contains("地址", StringComparison.Ordinal));
    }

    [Fact]
    public void Descriptor_EnforcesProtocolEndForOneAndTwoRegisterPoints()
    {
        var descriptor = new ModbusTcpDriverDescriptor();
        var device = Device();

        Assert.Empty(descriptor.ValidatePoint(Entry("D65535", DevicePointDataType.UInt16), device));
        Assert.Contains(descriptor.ValidatePoint(Entry("D65535", DevicePointDataType.Float32), device),
            issue => issue.Path == "address");
        Assert.Empty(descriptor.ValidatePoint(Entry("HD24447", DevicePointDataType.UInt16), device));
        Assert.Contains(descriptor.ValidatePoint(Entry("HD24447", DevicePointDataType.Int32), device),
            issue => issue.Path == "address");
    }

    [Fact]
    public void ReadBatchPlanner_Respects125And2000LimitsWithoutSplittingAValue()
    {
        var order = new ModbusDataOrderOptions
        {
            ByteOrder = ByteOrder.BigEndian,
            WordOrder = WordOrder.LowWordFirst
        };
        var registers = Enumerable.Range(0, 126)
            .Select(offset => StandardPoint($"HR:{offset}", DevicePointDataType.UInt16))
            .ToArray();
        var registerBatches = ModbusReadBatchPlanner.Plan(registers, order, requireContiguous: true);
        Assert.Equal(new ushort[] { 125, 1 }, registerBatches.Select(batch => batch.Count));

        var withDouble = Enumerable.Range(0, 124)
            .Select(offset => StandardPoint($"HR:{offset}", DevicePointDataType.UInt16))
            .Append(StandardPoint("HR:124", DevicePointDataType.Float32))
            .ToArray();
        var doubleBatches = ModbusReadBatchPlanner.Plan(withDouble, order, requireContiguous: true);
        Assert.Equal(new ushort[] { 124, 2 }, doubleBatches.Select(batch => batch.Count));
        Assert.Single(doubleBatches[1].Items);
        Assert.Equal(2, doubleBatches[1].Items[0].ValueLength);

        var coils = Enumerable.Range(0, 2001)
            .Select(offset => StandardPoint($"C:{offset}", DevicePointDataType.Bool))
            .ToArray();
        var coilBatches = ModbusReadBatchPlanner.Plan(coils, order, requireContiguous: true);
        Assert.Equal(new ushort[] { 2000, 1 }, coilBatches.Select(batch => batch.Count));
    }

    [Fact]
    public void ValueCodec_UsesFixedLowWordFirstForSignedAndUnsigned32BitBoundaries()
    {
        var order = new ModbusDataOrderOptions
        {
            ByteOrder = ByteOrder.BigEndian,
            WordOrder = WordOrder.LowWordFirst
        };
        var signed = StandardPoint("HR:0", DevicePointDataType.Int32);
        var unsigned = StandardPoint("HR:2", DevicePointDataType.UInt32);

        Assert.Equal(int.MinValue, ModbusValueCodec.DecodeRegisters(signed, order, new ushort[] { 0x0000, 0x8000 }));
        Assert.Equal(uint.MaxValue, ModbusValueCodec.DecodeRegisters(unsigned, order, new ushort[] { 0xFFFF, 0xFFFF }));
        Assert.Equal(0x12345678, ModbusValueCodec.DecodeRegisters(signed, order, new ushort[] { 0x5678, 0x1234 }));
        Assert.Equal(new ushort[] { 0x0000, 0x8000 }, ModbusValueCodec.EncodeRegisters(signed, order, int.MinValue));
        Assert.Equal(new ushort[] { 0xFFFF, 0xFFFF }, ModbusValueCodec.EncodeRegisters(unsigned, order, uint.MaxValue));
        Assert.Equal(new ushort[] { 0x5678, 0x1234 }, ModbusValueCodec.EncodeRegisters(signed, order, 0x12345678));
    }

    [Fact]
    public async Task Runtime_ReadsReference59PointAddressCatalogWithoutGapRequests()
    {
        var client = new FakeModbusClient();
        // Address/type are copied from the read-only reference PLC_XDP.csv.
        // Its R/W declarations are intentionally omitted; this fixture constructs read-only points only.
        const string addressTypes = """
            HD1000 Float32
            HD1002 Float32
            HD1004 Float32
            HD1006 Float32
            HD1008 Float32
            HD1020 Float32
            HD1022 Float32
            HD1026 Float32
            HD1032 Float32
            HD1036 Float32
            HD1040 Float32
            HD1042 Float32
            HD1044 Float32
            HD1052 Float32
            HD1054 Float32
            HD1056 Float32
            HD1060 Float32
            HD1062 Float32
            HD1068 Float32
            HD1072 UInt16
            HD1074 Float32
            D1820 Float32
            D1822 Float32
            D1824 Float32
            D1850 Float32
            D1852 Float32
            D1854 Float32
            D1866 Float32
            D1876 Float32
            D1886 Float32
            D1896 Float32
            D1906 Float32
            D1916 Float32
            D100 UInt32
            M400 Bool
            M403 Bool
            M404 Bool
            M410 Bool
            M411 Bool
            M412 Bool
            M413 Bool
            M414 Bool
            M417 Bool
            M418 Bool
            M500 Bool
            M501 Bool
            M502 Bool
            M506 Bool
            M508 Bool
            M510 Bool
            M511 Bool
            M512 Bool
            M514 Bool
            M515 Bool
            M520 Bool
            HM40 Bool
            HM50 Bool
            HM60 Bool
            HM70 Bool
            """;
        var typeMap = new Dictionary<string, DevicePointDataType>(StringComparer.Ordinal)
        {
            ["Float32"] = DevicePointDataType.Float32,
            ["UInt16"] = DevicePointDataType.UInt16,
            ["UInt32"] = DevicePointDataType.UInt32,
            ["Bool"] = DevicePointDataType.Bool
        };
        var points = addressTypes.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line =>
            {
                var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                return Point(fields[0], typeMap[fields[1]]);
            })
            .ToList();
        Assert.Equal(59, points.Count);
        Assert.All(points, point => Assert.False(point.IsWritable));

        await using var runtime = new ModbusTcpDeviceRuntime(
            Device(), TcpChannel(), points, new FixedClock(), "rev-59", new FakeModbusClientFactory(client));
        await runtime.StartAsync();
        var values = await runtime.ReadManyFreshAsync(points.Select(point => point.PointId).ToArray());

        Assert.Equal(59, values.Count);
        Assert.Equal(59, values.Select(value => value.PointId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(values, value => Assert.Equal(PointQuality.Good, value.Quality));
        var expectedReads = new (ModbusArea Area, ushort Start, ushort Count)[]
        {
            (ModbusArea.HoldingRegister, 42088, 10),
            (ModbusArea.HoldingRegister, 42108, 4),
            (ModbusArea.HoldingRegister, 42114, 2),
            (ModbusArea.HoldingRegister, 42120, 2),
            (ModbusArea.HoldingRegister, 42124, 2),
            (ModbusArea.HoldingRegister, 42128, 6),
            (ModbusArea.HoldingRegister, 42140, 6),
            (ModbusArea.HoldingRegister, 42148, 4),
            (ModbusArea.HoldingRegister, 42156, 2),
            (ModbusArea.HoldingRegister, 42160, 1),
            (ModbusArea.HoldingRegister, 42162, 2),
            (ModbusArea.HoldingRegister, 100, 2),
            (ModbusArea.HoldingRegister, 1820, 6),
            (ModbusArea.HoldingRegister, 1850, 6),
            (ModbusArea.HoldingRegister, 1866, 2),
            (ModbusArea.HoldingRegister, 1876, 2),
            (ModbusArea.HoldingRegister, 1886, 2),
            (ModbusArea.HoldingRegister, 1896, 2),
            (ModbusArea.HoldingRegister, 1906, 2),
            (ModbusArea.HoldingRegister, 1916, 2),
            (ModbusArea.Coil, 400, 1),
            (ModbusArea.Coil, 403, 2),
            (ModbusArea.Coil, 410, 5),
            (ModbusArea.Coil, 417, 2),
            (ModbusArea.Coil, 500, 3),
            (ModbusArea.Coil, 506, 1),
            (ModbusArea.Coil, 508, 1),
            (ModbusArea.Coil, 510, 3),
            (ModbusArea.Coil, 514, 2),
            (ModbusArea.Coil, 520, 1),
            (ModbusArea.Coil, 49448, 1),
            (ModbusArea.Coil, 49458, 1),
            (ModbusArea.Coil, 49468, 1),
            (ModbusArea.Coil, 49478, 1)
        };
        Assert.Equal(expectedReads, client.Reads);
    }

    [Fact]
    public async Task Runtime_ReadsContiguousNativeGroupsWithoutHolesAndKeepsNativeMetadata()
    {
        var client = new FakeModbusClient();
        client.Registers[100] = 0x1234;
        client.Registers[101] = 0x0000;
        client.Registers[102] = 0x3F80;
        client.Registers[103] = 0x5678;
        client.Registers[42088] = 0x0000;
        client.Registers[42089] = 0x3F80;
        client.Registers[42090] = 0x0000;
        client.Registers[42091] = 0x3F80;
        client.Registers[42096] = 0x9876;
        client.Registers[42097] = 0x0000;
        client.Registers[42108] = 0xA987;
        client.Registers[42109] = 0x0000;
        client.Coils[400] = true;
        client.Coils[402] = true;
        client.Coils[49448] = false;

        var points = new[]
        {
            Point("D100", DevicePointDataType.UInt16),
            Point("D101", DevicePointDataType.Float32),
            Point("D103", DevicePointDataType.UInt16),
            Point("HD1000", DevicePointDataType.Float32),
            Point("HD1002", DevicePointDataType.Float32),
            Point("HD1008", DevicePointDataType.Float32),
            Point("HD1020", DevicePointDataType.Float32),
            Point("M400", DevicePointDataType.Bool),
            Point("M402", DevicePointDataType.Bool),
            Point("HM40", DevicePointDataType.Bool)
        };
        await using var runtime = new ModbusTcpDeviceRuntime(
            Device(), TcpChannel(), points, new FixedClock(), "rev-xinje", new FakeModbusClientFactory(client));

        await runtime.StartAsync();
        Assert.Equal(DeviceConnectionState.Connecting, runtime.Status.ConnectionState);
        var values = await runtime.ReadManyFreshAsync(points.Select(point => point.PointId).ToArray());

        Assert.Equal(DeviceConnectionState.Online, runtime.Status.ConnectionState);
        Assert.Collection(client.Reads,
            read => Assert.Equal((ModbusArea.HoldingRegister, (ushort)100, (ushort)4), read),
            read => Assert.Equal((ModbusArea.HoldingRegister, (ushort)42088, (ushort)4), read),
            read => Assert.Equal((ModbusArea.HoldingRegister, (ushort)42096, (ushort)2), read),
            read => Assert.Equal((ModbusArea.HoldingRegister, (ushort)42108, (ushort)2), read),
            read => Assert.Equal((ModbusArea.Coil, (ushort)400, (ushort)1), read),
            read => Assert.Equal((ModbusArea.Coil, (ushort)402, (ushort)1), read),
            read => Assert.Equal((ModbusArea.Coil, (ushort)49448, (ushort)1), read));

        Assert.Equal(points.Length, values.Count);
        Assert.Equal(1f, Convert.ToSingle(values.Single(value => value.Address == "D101").Value));
        Assert.Equal(1f, Convert.ToSingle(values.Single(value => value.Address == "HD1000").Value));
        Assert.Equal("HD1000", values.Single(value => value.PointId == points[3].PointId).Address);
        Assert.All(values, value =>
        {
            Assert.Equal("20000000-0000-0000-0000-000000000001", value.DeviceId);
            Assert.Equal("rev-xinje", value.Revision);
            Assert.Equal(1, value.ConnectionGeneration);
            Assert.Equal(PointQuality.Good, value.Quality);
        });

        var single = await runtime.ReadFreshAsync(points[3].PointId);
        Assert.Equal("HD1000", single.Address);
        Assert.Equal(points[3].PointId, single.PointId);
        Assert.Equal("20000000-0000-0000-0000-000000000001", single.DeviceId);
        Assert.Equal("rev-xinje", single.Revision);
        Assert.Equal(1, single.ConnectionGeneration);
    }

    [Fact]
    public async Task Runtime_GapAddressFailureDoesNotAffectConfiguredPointReads()
    {
        var client = new FakeModbusClient { FailAtHoldingAddress = 42090 };
        var points = new[]
        {
            Point("HD1000", DevicePointDataType.UInt16),
            Point("HD1008", DevicePointDataType.UInt16)
        };
        await using var runtime = new ModbusTcpDeviceRuntime(
            Device(), TcpChannel(), points, new FixedClock(), "rev-gap", new FakeModbusClientFactory(client));

        await runtime.StartAsync();
        var values = await runtime.ReadManyFreshAsync(points.Select(point => point.PointId).ToArray());

        Assert.All(values, value => Assert.Equal(PointQuality.Good, value.Quality));
        Assert.Equal(new[]
        {
            (ModbusArea.HoldingRegister, (ushort)42088, (ushort)1),
            (ModbusArea.HoldingRegister, (ushort)42096, (ushort)1)
        }, client.Reads);
    }

    [Fact]
    public async Task Runtime_DecodeFailureReturnsBadValueWithOriginalNativeMetadata()
    {
        var device = Device();
        var client = new FakeModbusClient
        {
            BeforeHoldingRead = () => device.ModbusDataOrder!.WordOrder = (WordOrder)999
        };
        var point = Point("HD1000", DevicePointDataType.Float32);
        await using var runtime = new ModbusTcpDeviceRuntime(
            device, TcpChannel(), new[] { point }, new FixedClock(), "rev-decode", new FakeModbusClientFactory(client));

        await runtime.StartAsync();
        var value = Assert.Single(await runtime.ReadManyFreshAsync(new[] { point.PointId }));

        Assert.Equal(PointQuality.Bad, value.Quality);
        Assert.Equal("HD1000", value.Address);
        Assert.Equal(point.PointId, value.PointId);
        Assert.Equal(point.DeviceId, value.DeviceId);
        Assert.Equal("rev-decode", value.Revision);
        Assert.Equal(1, value.ConnectionGeneration);
    }

    [Fact]
    public async Task Runtime_WriteUsesMappedAddressAndFixedLowWordFirstEncoding()
    {
        var client = new FakeModbusClient();
        var points = new[]
        {
            Point("D100", DevicePointDataType.UInt16, writable: true),
            Point("HD1000", DevicePointDataType.Float32, writable: true),
            Point("M400", DevicePointDataType.Bool, writable: true)
        }.Select(point => point with { Revision = "rev-write" }).ToArray();
        await using var runtime = new ModbusTcpDeviceRuntime(
            Device(), TcpChannel(), points, new FixedClock(), "rev-write", new FakeModbusClientFactory(client));

        await runtime.StartAsync();
        await runtime.ReadManyFreshAsync(points.Select(point => point.PointId).ToArray());
        var singleRegister = await runtime.WriteAsync(points[0], (ushort)0x1234);
        var doubleRegister = await runtime.WriteAsync(points[1], 1f);
        var coil = await runtime.WriteAsync(points[2], true);

        Assert.Equal((ushort)100, client.RegisterWrites[0].Address);
        Assert.Equal(new ushort[] { 0x1234 }, client.RegisterWrites[0].Values);
        Assert.Equal((ushort)42088, client.RegisterWrites[1].Address);
        Assert.Equal(new ushort[] { 0x0000, 0x3F80 }, client.RegisterWrites[1].Values);
        Assert.Equal((ushort)400, client.CoilWrites.Single().Address);
        Assert.True(client.CoilWrites.Single().Value);
        Assert.Equal("D100", singleRegister.Address);
        Assert.Equal("HD1000", doubleRegister.Address);
        Assert.Equal("M400", coil.Address);
        Assert.All(new[] { singleRegister, doubleRegister, coil }, value =>
        {
            Assert.Equal("20000000-0000-0000-0000-000000000001", value.DeviceId);
            Assert.Equal("rev-write", value.Revision);
            Assert.Equal(1, value.ConnectionGeneration);
            Assert.Equal(PointQuality.Good, value.Quality);
        });
    }

    [Fact]
    public async Task WritePipelineReturnsFreshNativePointValueAndDoesNotReadBackAfterWriteFailure()
    {
        var client = new FakeModbusClient();
        var point = Point("HD1000", DevicePointDataType.Float32, writable: true) with { Revision = "rev-pipeline" };
        await using var runtime = new ModbusTcpDeviceRuntime(
            Device(), TcpChannel(), new[] { point }, new FixedClock(), "rev-pipeline", new FakeModbusClientFactory(client));
        await runtime.StartAsync();

        var firstRead = await runtime.ReadFreshAsync(point.PointId);
        Assert.Equal("HD1000", firstRead.Address);
        Assert.Equal(point.PointId, firstRead.PointId);
        Assert.Equal(point.DeviceId, firstRead.DeviceId);
        Assert.Equal("rev-pipeline", firstRead.Revision);
        Assert.Equal(1, firstRead.ConnectionGeneration);

        var audit = new FakeAuditLog();
        await using var operations = new DeviceOperationCoordinator();
        var pipeline = new DeviceWritePipeline(audit, new FakeAppLogger(), operations);
        var actor = ManualOperator();
        var verified = await pipeline.ExecuteAsync(new WriteCommand(
            actor, point, 1f, DeviceMode.Hardware, runtime, ActiveRunOk: true, RiskConfirmed: false,
            ExpectedRevision: "rev-pipeline"));

        Assert.Equal("HD1000", verified.Address);
        Assert.Equal(point.PointId, verified.PointId);
        Assert.Equal(point.DeviceId, verified.DeviceId);
        Assert.Equal("rev-pipeline", verified.Revision);
        Assert.Equal(1, verified.ConnectionGeneration);
        Assert.Equal(PointQuality.Good, verified.Quality);
        Assert.Equal(2, client.Reads.Count);

        client.FailAtRegisterWriteAddress = 42088;
        var readsBeforeFailure = client.Reads.Count;
        var writeFailure = await Assert.ThrowsAsync<DomainException>(() => pipeline.ExecuteAsync(new WriteCommand(
            actor, point, 2f, DeviceMode.Hardware, runtime, ActiveRunOk: true, RiskConfirmed: false,
            ExpectedRevision: "rev-pipeline")));

        Assert.Contains("写入 Modbus TCP 点位", writeFailure.Message, StringComparison.Ordinal);
        Assert.Equal(readsBeforeFailure, client.Reads.Count);
        Assert.Equal(2, client.RegisterWriteAttempts);
        Assert.Contains("DeviceWrite", audit.Actions);
        Assert.Contains("DeviceWriteFailed", audit.Actions);
    }

    [Fact]
    public async Task Runtime_RequestFailurePropagatesAndInvalidatesConnection()
    {
        var client = new FakeModbusClient { FailAtHoldingAddress = 103 };
        var points = new[]
        {
            Point("D100", DevicePointDataType.UInt16),
            Point("D103", DevicePointDataType.UInt16)
        };
        await using var runtime = new ModbusTcpDeviceRuntime(
            Device(), TcpChannel(), points, new FixedClock(), "rev-fail", new FakeModbusClientFactory(client));

        await runtime.StartAsync();
        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await runtime.ReadManyFreshAsync(points.Select(point => point.PointId).ToArray()));

        Assert.Equal(DeviceConnectionState.Offline, runtime.Status.ConnectionState);
        Assert.Equal(1, client.DisposeCount);
    }

    [Fact]
    public void ChangingDeviceModelRejectsIncompatibleExistingAddressesWithoutRewritingPoints()
    {
        var descriptor = new ModbusTcpDriverDescriptor();
        var xinjeDevice = Device();
        xinjeDevice.DeviceMode = DeviceMode.Simulation;
        var xinjeAddressPoint = ConfigurationPoint(xinjeDevice, "D100", DevicePointDataType.UInt16);
        var xinjeSnapshot = Snapshot(xinjeDevice, xinjeAddressPoint);
        xinjeDevice.Model = "GENERIC_MODBUS_TCP";

        var xinjeToGenericIssues = MultiDeviceConfigurationValidator.Validate(xinjeSnapshot, new[] { descriptor });

        Assert.NotEmpty(xinjeToGenericIssues);
        Assert.Equal("D100", xinjeSnapshot.Points.Points.Single().Address);
        Assert.Single(xinjeSnapshot.Points.Points);

        var genericDevice = Device();
        genericDevice.DeviceMode = DeviceMode.Simulation;
        genericDevice.Model = "GENERIC_MODBUS_TCP";
        var protocolAddressPoint = ConfigurationPoint(genericDevice, "D100", DevicePointDataType.UInt16);
        protocolAddressPoint.Address = "HR:100";
        var genericSnapshot = Snapshot(genericDevice, protocolAddressPoint);
        genericDevice.Model = DeviceModelKeyCatalog.XinjeFieldV1;

        var genericToXinjeIssues = MultiDeviceConfigurationValidator.Validate(genericSnapshot, new[] { descriptor });

        Assert.NotEmpty(genericToXinjeIssues);
        Assert.Equal("HR:100", genericSnapshot.Points.Points.Single().Address);
        Assert.Single(genericSnapshot.Points.Points);
    }

    private static DeviceConfig.DeviceEntry Device()
        => new()
        {
            Id = "20000000-0000-0000-0000-000000000001",
            Code = "XINJE",
            Name = "信捷测试设备",
            ChannelId = "10000000-0000-0000-0000-000000000001",
            DriverKey = DriverKeyCatalog.ModbusTcp,
            Model = DeviceModelKeyCatalog.XinjeFieldV1,
            DeviceMode = DeviceMode.Hardware,
            ModbusUnitId = 1,
            ModbusDataOrder = new ModbusDataOrderOptions
            {
                ByteOrder = ByteOrder.BigEndian,
                WordOrder = WordOrder.LowWordFirst
            },
            ModbusTcp = new ModbusTcpConnectionOptions { Host = "127.0.0.1", Port = 1502 },
            StaleAfterMs = 2000,
            Timing = new DeviceTimingOptions { ConnectTimeoutMs = 100, RequestTimeoutMs = 100, RetryCount = 0 }
        };

    private static DevicePoint Point(string address, DevicePointDataType type, bool writable = false)
    {
        var native = XinjeFieldV1AddressParser.Parse(address);
        return new DevicePoint(
            Code: "P_" + address,
            Protocol: DevicePointProtocol.ModbusTcp,
            Address: native.Canonical,
            DataType: type,
            IsWritable: writable,
            RiskLevel: WriteRiskLevel.Normal,
            RawMin: null,
            RawMax: null,
            EngMin: null,
            EngMax: null,
            Name: address,
            PointId: Guid.NewGuid().ToString("D"),
            DeviceId: "20000000-0000-0000-0000-000000000001",
            Revision: "rev-1",
            DriverKey: DriverKeyCatalog.ModbusTcp,
            AddressDefinition: new PointAddressDefinition
            {
                Area = native.ModbusAddress.Area.ToCode(),
                Offset = native.ModbusAddress.Offset
            });
    }

    private static DevicePoint StandardPoint(string address, DevicePointDataType type)
        => new(
            Code: "STANDARD_" + address,
            Protocol: DevicePointProtocol.ModbusTcp,
            Address: address,
            DataType: type,
            IsWritable: false,
            RiskLevel: WriteRiskLevel.Normal,
            RawMin: null,
            RawMax: null,
            EngMin: null,
            EngMax: null,
            PointId: Guid.NewGuid().ToString("D"),
            DeviceId: "standard-device",
            Revision: "rev-1",
            DriverKey: DriverKeyCatalog.ModbusTcp);

    private static PointsConfig.PointEntry Entry(string address, DevicePointDataType type)
        => new()
        {
            Code = "POINT_" + address,
            Address = address,
            Protocol = nameof(DevicePointProtocol.ModbusTcp),
            DataType = DevicePointTypeCatalog.ToStorage(type)
        };

    private static DeviceConfig DeviceConfigFor(DeviceConfig.DeviceEntry device)
        => new()
        {
            SchemaVersion = DeviceConfig.CurrentSchemaVersion,
            Channels = [TcpChannel()],
            Devices = [device]
        };

    private static DeviceConfigurationSnapshot Snapshot(
        DeviceConfig.DeviceEntry device,
        params PointsConfig.PointEntry[] points)
        => new()
        {
            Revision = "xj-configuration-test",
            Device = DeviceConfigFor(device),
            Points = new PointsConfig
            {
                SchemaVersion = PointsConfig.CurrentSchemaVersion,
                Groups =
                [
                    new PointsConfig.PointGroupEntry
                    {
                        Id = "30000000-0000-0000-0000-000000000001",
                        DeviceId = device.Id,
                        Code = "DEFAULT",
                        Name = "未分组",
                        SortOrder = 0
                    }
                ],
                Points = points.ToList()
            },
            Simulation = new SimulationConfig(),
            SignalBindings = new SignalBindingsConfig()
        };

    private static PointsConfig.PointEntry ConfigurationPoint(
        DeviceConfig.DeviceEntry device,
        string address,
        DevicePointDataType type)
    {
        var native = XinjeFieldV1AddressParser.Parse(address);
        var point = Entry(address, type);
        point.Id = Guid.NewGuid().ToString("D");
        point.DeviceId = device.Id;
        point.DeviceCode = device.Code;
        point.GroupId = "30000000-0000-0000-0000-000000000001";
        point.GroupCode = "DEFAULT";
        point.AddressDefinition = new PointAddressDefinition
        {
            Area = native.ModbusAddress.Area.ToCode(),
            Offset = native.ModbusAddress.Offset
        };
        return point;
    }

    private static ChannelEntry TcpChannel()
        => new()
        {
            Id = "10000000-0000-0000-0000-000000000001",
            Code = "TCP_XINJE",
            Name = "信捷 TCP",
            TransportKind = ChannelTransportKind.Tcp,
            Tcp = new TcpChannelParameters()
        };

    private static UserContext ManualOperator()
    {
        var role = new Role { Id = 99, Name = "测试操作员" };
        role.Permissions.Add(PermissionCode.ManualControl);
        return new UserContext { UserId = 99, LoginName = "test-operator", DisplayName = "测试操作员", Role = role };
    }

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => new(2026, 9, 23, 0, 0, 0, DateTimeKind.Utc);
    }

    private sealed class FakeModbusClientFactory(FakeModbusClient client) : IModbusClientFactory
    {
        public Task<IModbusClient> CreateTcpAsync(string host, int port, TimeSpan connectTimeout, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IModbusClient>(client);
        }

        public Task<IModbusClient> CreateRtuAsync(SerialChannelParameters serial, TimeSpan connectTimeout, CancellationToken ct)
            => throw new NotSupportedException();
    }

    private sealed class FakeModbusClient : IModbusClient
    {
        public Dictionary<ushort, ushort> Registers { get; } = new();
        public Dictionary<ushort, bool> Coils { get; } = new();
        public List<(ModbusArea Area, ushort Start, ushort Count)> Reads { get; } = new();
        public List<(ushort Address, ushort[] Values)> RegisterWrites { get; } = new();
        public List<(ushort Address, bool Value)> CoilWrites { get; } = new();
        public ushort? FailAtHoldingAddress { get; init; }
        public ushort? FailAtRegisterWriteAddress { get; set; }
        public Action? BeforeHoldingRead { get; init; }
        public int RegisterWriteAttempts { get; private set; }
        public int DisposeCount { get; private set; }

        public Task<bool[]> ReadCoilsAsync(byte unitId, ushort start, ushort count, TimeSpan requestTimeout, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Reads.Add((ModbusArea.Coil, start, count));
            return Task.FromResult(Enumerable.Range(start, count)
                .Select(offset => Coils.GetValueOrDefault(checked((ushort)offset))).ToArray());
        }

        public Task<bool[]> ReadDiscreteInputsAsync(byte unitId, ushort start, ushort count, TimeSpan requestTimeout, CancellationToken ct)
            => Task.FromResult(new bool[count]);

        public Task<ushort[]> ReadHoldingRegistersAsync(byte unitId, ushort start, ushort count, TimeSpan requestTimeout, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Reads.Add((ModbusArea.HoldingRegister, start, count));
            BeforeHoldingRead?.Invoke();
            if (FailAtHoldingAddress is { } failAt && start <= failAt && (int)start + count > failAt)
                throw new TimeoutException("假客户端模拟 Modbus 请求超时");
            return Task.FromResult(Enumerable.Range(start, count)
                .Select(offset => Registers.GetValueOrDefault(checked((ushort)offset))).ToArray());
        }

        public Task<ushort[]> ReadInputRegistersAsync(byte unitId, ushort start, ushort count, TimeSpan requestTimeout, CancellationToken ct)
            => Task.FromResult(new ushort[count]);

        public Task WriteSingleCoilAsync(byte unitId, ushort address, bool value, TimeSpan requestTimeout, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            CoilWrites.Add((address, value));
            Coils[address] = value;
            return Task.CompletedTask;
        }

        public Task WriteSingleRegisterAsync(byte unitId, ushort address, ushort value, TimeSpan requestTimeout, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            RegisterWriteAttempts++;
            if (FailAtRegisterWriteAddress == address)
                throw new IOException("假客户端模拟寄存器写入失败");
            RegisterWrites.Add((address, new[] { value }));
            Registers[address] = value;
            return Task.CompletedTask;
        }

        public Task WriteMultipleRegistersAsync(byte unitId, ushort address, ushort[] values, TimeSpan requestTimeout, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            RegisterWriteAttempts++;
            if (FailAtRegisterWriteAddress == address)
                throw new IOException("假客户端模拟寄存器写入失败");
            RegisterWrites.Add((address, values.ToArray()));
            for (var index = 0; index < values.Length; index++)
                Registers[checked((ushort)(address + index))] = values[index];
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeAuditLog : IAuditLog
    {
        public List<string> Actions { get; } = new();
        public Task WriteAsync(string actor, string action, string? target, string? detail, CancellationToken ct = default)
        {
            Actions.Add(action);
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<AuditEntry>> ListRecentAsync(int limit, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AuditEntry>>(Array.Empty<AuditEntry>());
        public Task<IReadOnlyList<AuditEntry>> SearchAsync(AuditLogQuery query, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AuditEntry>>(Array.Empty<AuditEntry>());
    }

    private sealed class FakeAppLogger : IAppLogger
    {
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, Exception? exception = null) { }
    }
}
