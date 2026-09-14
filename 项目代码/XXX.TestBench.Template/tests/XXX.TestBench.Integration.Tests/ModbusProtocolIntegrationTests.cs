using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Devices.Drivers;
using XXX.TestBench.Devices.Modbus;
using XXX.TestBench.Devices.Runtime;

namespace XXX.TestBench.Integration.Tests;

/// <summary>
/// Modbus 首版的协议边界、编解码、批量规划和硬件运行时测试。
///
/// 这些用例全部使用内存假客户端，不连接真实 PLC、仪表或串口；
/// 它们证明的是软件契约和生命周期，现场设备仍需单独验收。
/// </summary>
public sealed class ModbusProtocolIntegrationTests
{
    [Theory]
    [InlineData("C:0", ModbusArea.Coil, 0, "C:0")]
    [InlineData("DI:12", ModbusArea.DiscreteInput, 12, "DI:12")]
    [InlineData("HR:0", ModbusArea.HoldingRegister, 0, "HR:0")]
    [InlineData("IR:65535", ModbusArea.InputRegister, 65535, "IR:65535")]
    [InlineData("00001", ModbusArea.Coil, 0, "C:0")]
    [InlineData("10001", ModbusArea.DiscreteInput, 0, "DI:0")]
    [InlineData("30001", ModbusArea.InputRegister, 0, "IR:0")]
    [InlineData("40001", ModbusArea.HoldingRegister, 0, "HR:0")]
    public void AddressParser_UsesExplicitAreaAndCanonicalOffset(
        string text,
        ModbusArea expectedArea,
        int expectedOffset,
        string expectedCanonical)
    {
        var address = ModbusAddressParser.Parse(text);

        Assert.Equal(expectedArea, address.Area);
        Assert.Equal(expectedOffset, address.Offset);
        Assert.Equal(expectedCanonical, address.Canonical);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("400")]
    [InlineData("H40001")]
    [InlineData("HR:65536")]
    [InlineData("UNKNOWN:0")]
    public void AddressParser_RejectsAmbiguousOrOutOfRangeAddress(string text)
    {
        Assert.False(ModbusAddressParser.TryParse(text, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void TypeCapabilities_EnforceAreaAccessAndExplicitWordOrder()
    {
        var bigEndian = new DecodeOptions
        {
            ByteOrder = ByteOrder.BigEndian,
            WordOrder = WordOrder.HighWordFirst
        };
        var singleRegister = new DecodeOptions
        {
            ByteOrder = ByteOrder.BigEndian,
            WordOrder = WordOrder.None
        };

        Assert.True(ModbusTypeCapabilities.TryValidate(
            ModbusArea.Coil, DevicePointDataType.Bool, true, singleRegister, out _));
        Assert.False(ModbusTypeCapabilities.TryValidate(
            ModbusArea.DiscreteInput, DevicePointDataType.Bool, true, singleRegister, out var discreteError));
        Assert.Contains("只读", discreteError, StringComparison.Ordinal);
        Assert.False(ModbusTypeCapabilities.TryValidate(
            ModbusArea.HoldingRegister, DevicePointDataType.Bool, false, singleRegister, out _));
        Assert.False(ModbusTypeCapabilities.TryValidate(
            ModbusArea.HoldingRegister, DevicePointDataType.Float32, false, singleRegister, out var wordError));
        Assert.Contains("字序", wordError, StringComparison.Ordinal);
        Assert.True(ModbusTypeCapabilities.TryValidate(
            ModbusArea.HoldingRegister, DevicePointDataType.Float32, false, bigEndian, out _));
    }

    [Fact]
    public void ValueCodec_UsesConfiguredByteAndWordOrderWithoutHostEndianAssumption()
    {
        var highWordFirst = ModbusPoint(
            "HR:0",
            DevicePointDataType.Int32,
            byteOrder: ByteOrder.BigEndian,
            wordOrder: WordOrder.HighWordFirst);
        var lowWordFirst = ModbusPoint(
            "HR:0",
            DevicePointDataType.Int32,
            byteOrder: ByteOrder.BigEndian,
            wordOrder: WordOrder.LowWordFirst);
        var littleEndian = ModbusPoint(
            "HR:0",
            DevicePointDataType.Int32,
            byteOrder: ByteOrder.LittleEndian,
            wordOrder: WordOrder.HighWordFirst);

        Assert.Equal(DevicePointDataType.Int32, highWordFirst.DataType);
        var highDecoded = ModbusValueCodec.DecodeRegisters(
            highWordFirst,
            new ushort[] { 0x0102, 0x0304 });
        Assert.IsType<int>(highDecoded);
        Assert.Equal(0x01020304, (int)highDecoded);
        Assert.Equal(0x01020304, (int)ModbusValueCodec.DecodeRegisters(
            lowWordFirst,
            new ushort[] { 0x0304, 0x0102 }));
        Assert.Equal(0x01020304, (int)ModbusValueCodec.DecodeRegisters(
            littleEndian,
            new ushort[] { 0x0201, 0x0403 }));
        Assert.Equal(
            new ushort[] { 0x0201, 0x0403 },
            ModbusValueCodec.EncodeRegisters(littleEndian, 0x01020304));
    }

    [Fact]
    public void ReadBatchPlanner_GroupsByAreaAndHonorsRegisterLimit()
    {
        var points = new[]
        {
            ModbusPoint("C:0", DevicePointDataType.Bool),
            ModbusPoint("HR:0", DevicePointDataType.Int16),
            ModbusPoint("HR:2", DevicePointDataType.Float32, wordOrder: WordOrder.HighWordFirst),
            ModbusPoint("HR:126", DevicePointDataType.Int16)
        };

        var batches = ModbusReadBatchPlanner.Plan(points);

        Assert.Equal(3, batches.Count);
        var firstRegisterBatch = batches.Single(batch => batch.Area == ModbusArea.HoldingRegister && batch.Start == 0);
        Assert.Equal((ushort)4, firstRegisterBatch.Count);
        Assert.Equal(0, firstRegisterBatch.Items[0].OffsetInBatch);
        Assert.Equal(2, firstRegisterBatch.Items[1].OffsetInBatch);
        Assert.Equal((ushort)126, batches.Single(batch => batch.Start == 126).Start);

        var overlap = new[]
        {
            ModbusPoint("HR:10", DevicePointDataType.Int32, wordOrder: WordOrder.HighWordFirst),
            ModbusPoint("HR:11", DevicePointDataType.Int16)
        };
        Assert.Throws<DomainException>(() => ModbusReadBatchPlanner.Plan(overlap));
    }

    [Fact]
    public void DriverDescriptors_ExposeOnlyMatchingTransportAndValidatePointMatrix()
    {
        var tcp = new ModbusTcpDriverDescriptor();
        var rtu = new ModbusRtuDriverDescriptor();
        var tcpChannel = TcpChannel();
        var serialChannel = SerialChannel();

        Assert.Empty(tcp.ValidateChannel(tcpChannel));
        Assert.NotEmpty(tcp.ValidateChannel(serialChannel));
        Assert.Empty(rtu.ValidateChannel(serialChannel));
        Assert.NotEmpty(rtu.ValidateChannel(tcpChannel));
        Assert.Empty(tcp.ValidatePoint(
            ModbusPointEntry("HR:0", DevicePointDataType.Float32, ByteOrder.BigEndian, WordOrder.HighWordFirst),
            ModbusDevice(DriverKeyCatalog.ModbusTcp)));
        Assert.Contains(
            tcp.ValidatePoint(
                ModbusPointEntry("HR:0", DevicePointDataType.Bool, ByteOrder.BigEndian, WordOrder.None),
                ModbusDevice(DriverKeyCatalog.ModbusTcp)),
            issue => issue.Path == "rawDataType");
    }

    [Fact]
    public async Task TcpRuntime_RemainsConnectingUntilFirstValidPointResponse_ThenReadsAndWrites()
    {
        var client = new FakeModbusClient
        {
            HoldingRegisters = new ushort[] { 0x0102, 0x0304 }
        };
        var factory = new FakeModbusClientFactory(client);
        var point = ModbusPoint(
            "HR:0",
            DevicePointDataType.Int32,
            isWritable: true,
            wordOrder: WordOrder.HighWordFirst);
        await using var runtime = new ModbusTcpDeviceRuntime(
            ModbusDevice(DriverKeyCatalog.ModbusTcp),
            TcpChannel(),
            new[] { point },
            new TestClock(),
            "modbus-test",
            factory);

        await runtime.StartAsync();

        Assert.Equal(DeviceConnectionState.Connecting, runtime.Status.ConnectionState);
        Assert.Equal(DeviceHealth.Degraded, runtime.Status.Health);
        Assert.Equal(0, client.HoldingReadCount);
        Assert.False(runtime.Status.IsConnected);

        var read = await runtime.ReadFreshAsync(point.PointId);

        Assert.Equal(PointQuality.Good, read.Quality);
        Assert.Equal(0x01020304, (int)read.Value!);
        Assert.Equal(DeviceConnectionState.Online, runtime.Status.ConnectionState);
        Assert.True(runtime.Status.IsConnected);
        Assert.Equal(1, client.HoldingReadCount);

        await runtime.WriteAsync(point, 0x01020304);

        Assert.Equal(new ushort[] { 0x0102, 0x0304 }, client.LastRegisterWrite);
        Assert.Equal((byte)1, client.LastUnitId);
        Assert.Equal((ushort)0, client.LastWriteAddress);
    }

    [Fact]
    public async Task TcpRuntime_InvalidatesClientAfterRequestTimeout()
    {
        var client = new FakeModbusClient { ThrowTimeoutOnHoldingRead = true };
        var factory = new FakeModbusClientFactory(client);
        var point = ModbusPoint("HR:0", DevicePointDataType.Int16);
        await using var runtime = new ModbusTcpDeviceRuntime(
            ModbusDevice(DriverKeyCatalog.ModbusTcp),
            TcpChannel(),
            new[] { point },
            new TestClock(),
            "timeout-test",
            factory);

        await runtime.StartAsync();

        await Assert.ThrowsAsync<TimeoutException>(() => runtime.ReadFreshAsync(point.PointId));

        Assert.Equal(DeviceConnectionState.Offline, runtime.Status.ConnectionState);
        Assert.Equal(1, client.DisposeCount);
        Assert.False(runtime.Status.IsConnected);
    }

    [Fact]
    public async Task RtuChannel_UsesOneSharedClientAndClosesOnlyAfterChannelDrain()
    {
        var client = new FakeModbusClient { HoldingRegisters = new ushort[] { 42 } };
        var factory = new FakeModbusClientFactory(client);
        await using var channels = new ChannelManager(new[] { SerialChannel() }, factory);
        var connection1 = channels.GetOrCreateModbusRtuConnection("channel-1");
        var connection2 = channels.GetOrCreateModbusRtuConnection("channel-1");
        Assert.Same(connection1, connection2);

        var device1 = ModbusDevice(DriverKeyCatalog.ModbusRtu, unitId: 1);
        var device2 = ModbusDevice(DriverKeyCatalog.ModbusRtu, unitId: 2);
        var point1 = ModbusPoint("HR:0", DevicePointDataType.Int16, protocol: DevicePointProtocol.ModbusRtu);
        var point2 = point1 with { PointId = "point-2", DeviceId = device2.Id };
        await using var runtime1 = new ModbusRtuDeviceRuntime(
            device1,
            SerialChannel(),
            new[] { point1 },
            new TestClock(),
            "rtu-test",
            connection1);
        await using var runtime2 = new ModbusRtuDeviceRuntime(
            device2,
            SerialChannel(),
            new[] { point2 },
            new TestClock(),
            "rtu-test",
            connection2);

        await runtime1.StartAsync();
        await runtime2.StartAsync();
        Assert.Equal(1, factory.CreateRtuCount);
        Assert.True(channels.IsSerialPortLeased("com1"));

        Assert.Equal(42, (short)(await runtime1.ReadFreshAsync(point1.PointId)).Value!);
        Assert.Equal(42, (short)(await runtime2.ReadFreshAsync(point2.PointId)).Value!);
        Assert.Equal(new byte[] { 1, 2 }, client.ReadUnitIds);

        await runtime1.StopAsync();
        Assert.True(channels.IsSerialPortLeased("COM1"));

        await channels.DrainAndStopAsync();
        Assert.False(channels.IsSerialPortLeased("COM1"));
        Assert.Equal(1, client.DisposeCount);
    }

    [Fact]
    public async Task ConnectionTester_DoesTransportOnlyAndSkipsLeasedRtuPort()
    {
        var tcpClient = new FakeModbusClient();
        var tcpFactory = new FakeModbusClientFactory(tcpClient);
        var tcpResult = await new ModbusTcpConnectionTester(tcpFactory)
            .TestAsync(ModbusDevice(DriverKeyCatalog.ModbusTcp), TcpChannel());

        Assert.True(tcpResult.Ok);
        Assert.True(tcpResult.Executed);
        Assert.Equal(DeviceConnectionTestLevel.TransportOnly, tcpResult.Level);
        Assert.Contains("尚未读取点位", tcpResult.Summary, StringComparison.Ordinal);
        Assert.Equal(0, tcpClient.HoldingReadCount);
        Assert.Equal(1, tcpClient.DisposeCount);

        var rtuFactory = new FakeModbusClientFactory(new FakeModbusClient());
        var rtuResult = await new ModbusRtuConnectionTester(
                rtuFactory,
                isSerialPortLeased: _ => true)
            .TestAsync(ModbusDevice(DriverKeyCatalog.ModbusRtu), SerialChannel());

        Assert.False(rtuResult.Ok);
        Assert.False(rtuResult.Executed);
        Assert.Contains("未重复打开", rtuResult.Summary, StringComparison.Ordinal);
        Assert.Equal(0, rtuFactory.CreateRtuCount);
    }

    private static DevicePoint ModbusPoint(
        string address,
        DevicePointDataType type,
        bool isWritable = false,
        ByteOrder byteOrder = ByteOrder.BigEndian,
        WordOrder wordOrder = WordOrder.None,
        DevicePointProtocol protocol = DevicePointProtocol.ModbusTcp)
        => new(
            Code: "P_" + address.Replace(':', '_'),
            Protocol: protocol,
            Address: address,
            DataType: type,
            IsWritable: isWritable,
            RiskLevel: WriteRiskLevel.Normal,
            RawMin: null,
            RawMax: null,
            EngMin: null,
            EngMax: null,
            Name: "测试点位",
            PointId: Guid.NewGuid().ToString("D"),
            DeviceId: "device-1",
            DriverKey: protocol == DevicePointProtocol.ModbusRtu
                ? DriverKeyCatalog.ModbusRtu
                : DriverKeyCatalog.ModbusTcp,
            DecodeOptions: new DecodeOptions { ByteOrder = byteOrder, WordOrder = wordOrder });

    private static PointsConfig.PointEntry ModbusPointEntry(
        string address,
        DevicePointDataType type,
        ByteOrder byteOrder,
        WordOrder wordOrder)
        => new()
        {
            Code = "AI.Pressure",
            Address = address,
            Protocol = "ModbusTcp",
            DataType = DevicePointTypeCatalog.ToStorage(type),
            RawDataType = DevicePointTypeCatalog.ToStorage(type),
            DecodeOptions = new DecodeOptions { ByteOrder = byteOrder, WordOrder = wordOrder }
        };

    private static DeviceConfig.DeviceEntry ModbusDevice(string driverKey, int unitId = 1)
        => new()
        {
            Id = driverKey + "-device",
            Code = "MODBUS_DEVICE",
            Name = "Modbus 测试设备",
            ChannelId = "channel-1",
            DriverKey = driverKey,
            Model = driverKey == DriverKeyCatalog.ModbusRtu
                ? "GENERIC_MODBUS_RTU"
                : "GENERIC_MODBUS_TCP",
            DeviceMode = DeviceMode.Hardware,
            ModbusUnitId = unitId,
            ModbusTcp = driverKey == DriverKeyCatalog.ModbusTcp
                ? new ModbusTcpConnectionOptions { Host = "127.0.0.1", Port = 1502 }
                : null,
            Timing = new DeviceTimingOptions
            {
                ConnectTimeoutMs = 100,
                RequestTimeoutMs = 100,
                RetryCount = 0
            }
        };

    private static ChannelEntry TcpChannel()
        => new()
        {
            Id = "channel-1",
            Code = "TCP_1",
            Name = "TCP 测试通道",
            TransportKind = ChannelTransportKind.Tcp,
            Tcp = new TcpChannelParameters { Host = "127.0.0.1", Port = 1502 }
        };

    private static ChannelEntry SerialChannel()
        => new()
        {
            Id = "channel-1",
            Code = "SERIAL_1",
            Name = "串口测试通道",
            TransportKind = ChannelTransportKind.Serial,
            Serial = new SerialChannelParameters
            {
                PortName = "COM1",
                BaudRate = 9600,
                DataBits = 8,
                Parity = "None",
                StopBits = "One"
            }
        };

    private sealed class TestClock : IClock
    {
        public DateTime UtcNow => new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);
    }

    private sealed class FakeModbusClientFactory : IModbusClientFactory
    {
        private readonly FakeModbusClient _client;

        public FakeModbusClientFactory(FakeModbusClient client) => _client = client;

        public int CreateTcpCount { get; private set; }
        public int CreateRtuCount { get; private set; }

        public Task<IModbusClient> CreateTcpAsync(
            string host,
            int port,
            TimeSpan connectTimeout,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            CreateTcpCount++;
            return Task.FromResult<IModbusClient>(_client);
        }

        public Task<IModbusClient> CreateRtuAsync(
            SerialChannelParameters serial,
            TimeSpan connectTimeout,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            CreateRtuCount++;
            return Task.FromResult<IModbusClient>(_client);
        }
    }

    private sealed class FakeModbusClient : IModbusClient
    {
        public ushort[] HoldingRegisters { get; set; } = new ushort[] { 0 };
        public bool ThrowTimeoutOnHoldingRead { get; set; }
        public int HoldingReadCount { get; private set; }
        public int DisposeCount { get; private set; }
        public byte LastUnitId { get; private set; }
        public ushort LastWriteAddress { get; private set; }
        public ushort[] LastRegisterWrite { get; private set; } = Array.Empty<ushort>();
        public List<byte> ReadUnitIds { get; } = new();

        public Task<bool[]> ReadCoilsAsync(byte unitId, ushort start, ushort count, TimeSpan requestTimeout, CancellationToken ct)
            => Task.FromResult(new bool[count]);

        public Task<bool[]> ReadDiscreteInputsAsync(byte unitId, ushort start, ushort count, TimeSpan requestTimeout, CancellationToken ct)
            => Task.FromResult(new bool[count]);

        public Task<ushort[]> ReadHoldingRegistersAsync(byte unitId, ushort start, ushort count, TimeSpan requestTimeout, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            HoldingReadCount++;
            ReadUnitIds.Add(unitId);
            if (ThrowTimeoutOnHoldingRead)
                throw new TimeoutException("fake request timeout");
            return Task.FromResult(HoldingRegisters.Length >= count
                ? HoldingRegisters
                : HoldingRegisters.Concat(new ushort[count - HoldingRegisters.Length]).ToArray());
        }

        public Task<ushort[]> ReadInputRegistersAsync(byte unitId, ushort start, ushort count, TimeSpan requestTimeout, CancellationToken ct)
            => Task.FromResult(new ushort[count]);

        public Task WriteSingleCoilAsync(byte unitId, ushort address, bool value, TimeSpan requestTimeout, CancellationToken ct)
        {
            LastUnitId = unitId;
            LastWriteAddress = address;
            return Task.CompletedTask;
        }

        public Task WriteSingleRegisterAsync(byte unitId, ushort address, ushort value, TimeSpan requestTimeout, CancellationToken ct)
        {
            LastUnitId = unitId;
            LastWriteAddress = address;
            LastRegisterWrite = new[] { value };
            return Task.CompletedTask;
        }

        public Task WriteMultipleRegistersAsync(byte unitId, ushort address, ushort[] values, TimeSpan requestTimeout, CancellationToken ct)
        {
            LastUnitId = unitId;
            LastWriteAddress = address;
            LastRegisterWrite = values.ToArray();
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }
}
