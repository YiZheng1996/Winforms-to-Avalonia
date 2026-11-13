using S7.Net;
using S7.Net.Types;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Devices.Drivers;
using XXX.TestBench.Devices.Runtime;
using XXX.TestBench.Devices.Siemens;
using XXX.TestBench.Infrastructure.Time;

namespace XXX.TestBench.Integration.Tests;

public sealed class S7NetPlusIntegrationTests
{
    [Fact]
    public void Descriptor_IsImplementedAfterS7NetPlusRuntimeAdded()
        => Assert.True(new SiemensS7DriverDescriptor().IsImplemented);


    [Fact]
    public void Descriptor_RejectsDecimalOnlyForHardwareS7()
    {
        var descriptor = new SiemensS7DriverDescriptor();
        var point = new PointsConfig.PointEntry
        {
            Code = "AI_PRESSURE",
            Address = "DB1.DBD0",
            DataType = "Decimal",
            RawDataType = "Decimal"
        };
        var hardware = new DeviceConfig.DeviceEntry
        {
            DriverKey = DriverKeyCatalog.SiemensS7,
            Model = "S7-1200",
            DeviceMode = DeviceMode.Hardware
        };
        var simulation = new DeviceConfig.DeviceEntry
        {
            DriverKey = DriverKeyCatalog.SiemensS7,
            Model = "S7-1200",
            DeviceMode = DeviceMode.Simulation
        };

        Assert.Contains(
            descriptor.ValidatePoint(point, hardware),
            issue => issue.Message.Contains("小数", StringComparison.Ordinal));
        Assert.Empty(descriptor.ValidatePoint(point, simulation));
    }

    [Fact]
    public void S7ReadBatchPlanner_UsesPointNameInCustomerError()
    {
        var point = new DevicePoint(
            "AI_PRESSURE",
            DevicePointProtocol.SiemensS7,
            "DB1.DBD0",
            DevicePointDataType.Decimal,
            false,
            WriteRiskLevel.Normal,
            null,
            null,
            null,
            null,
            "压力",
            "",
            Guid.NewGuid().ToString("D"),
            "device-1",
            DriverKeyCatalog.SiemensS7);

        var error = Assert.Throws<DomainException>(() => S7ReadBatchPlanner.Plan(new[] { point }));

        Assert.Contains("点位“压力”", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("AI_PRESSURE", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("DB144.DBD88", S7.Net.DataType.DataBlock, 144, 88)]
    [InlineData("DB142.DBX22.3", S7.Net.DataType.DataBlock, 142, 22)]
    [InlineData("M10.1", S7.Net.DataType.Memory, 0, 10)]
    [InlineData("I0.0", S7.Net.DataType.Input, 0, 0)]
    [InlineData("Q0.0", S7.Net.DataType.Output, 0, 0)]
    public void S7NetPlus_ParsesProjectAddressForms(
        string address,
        S7.Net.DataType expectedArea,
        int expectedDb,
        int expectedStartByte)
    {
        var item = DataItem.FromAddress(address);

        Assert.Equal(expectedArea, item.DataType);
        Assert.Equal(expectedDb, item.DB);
        Assert.Equal(expectedStartByte, item.StartByteAdr);
    }

    [Fact]
    public void Codec_Float32_RoundTripsS7BigEndian()
    {
        var point = Point(DevicePointDataType.Float32, "DB1.DBD0");

        var bytes = S7NetPlusValueCodec.Encode(point, 1.5f);
        var value = S7NetPlusValueCodec.Decode(point, bytes);

        Assert.Equal(new byte[] { 0x3F, 0xC0, 0x00, 0x00 }, bytes);
        Assert.Equal(1.5f, Assert.IsType<float>(value));
    }

    [Fact]
    public void Codec_Int16_RoundTripsS7BigEndian()
    {
        var point = Point(DevicePointDataType.Int16, "DB1.DBW0");

        var bytes = S7NetPlusValueCodec.Encode(point, (short)-2);
        var value = S7NetPlusValueCodec.Decode(point, bytes);

        Assert.Equal(new byte[] { 0xFF, 0xFE }, bytes);
        Assert.Equal((short)-2, Assert.IsType<short>(value));
    }

    [Fact]
    public void Codec_HonorsExplicitByteAndWordOrder()
    {
        var point = Point(DevicePointDataType.UInt32, "DB1.DBD0");
        point = point with
        {
            DecodeOptions = new DecodeOptions
            {
                ByteOrder = ByteOrder.LittleEndian,
                WordOrder = WordOrder.LowWordFirst
            }
        };

        var bytes = S7NetPlusValueCodec.Encode(point, 0x11223344u);
        var value = S7NetPlusValueCodec.Decode(point, bytes);

        Assert.Equal(new byte[] { 0x22, 0x11, 0x44, 0x33 }, bytes);
        Assert.Equal(0x11223344u, Assert.IsType<uint>(value));
    }

    [Fact]
    public void Codec_DecodesBitAndRejectsDecimalGuess()
    {
        var bitPoint = Point(DevicePointDataType.Bool, "DB1.DBX0.3");

        Assert.True(S7NetPlusValueCodec.DecodeBit(bitPoint, 0b0000_1000, 3));
        Assert.False(S7NetPlusValueCodec.DecodeBit(bitPoint, 0b0000_0100, 3));

        var decimalPoint = Point(DevicePointDataType.Decimal, "DB1.DBD0");
        Assert.Throws<DomainException>(() => S7NetPlusValueCodec.Encode(decimalPoint, 1.25m));
    }

    [Fact]
    public async Task DeviceSession_HardwareS7RejectsLegacyDecimalBeforeNetwork()
    {
        var channel = new ChannelEntry
        {
            Id = Guid.NewGuid().ToString(),
            Code = "CH_PLC",
            Name = "PLC 通道",
            TransportKind = ChannelTransportKind.Tcp,
            Enabled = true,
            TimeoutMs = 1000,
            RetryCount = 0,
            Tcp = new TcpChannelParameters { Host = "127.0.0.1", Port = 102 }
        };
        var device = new DeviceConfig.DeviceEntry
        {
            Id = Guid.NewGuid().ToString(),
            Code = "PLC1200",
            Name = "S7-1200",
            DeviceMode = DeviceMode.Hardware,
            DriverKey = DriverKeyCatalog.SiemensS7,
            Model = "S7-1200",
            ChannelId = channel.Id,
            PollIntervalMs = 500,
            StaleAfterMs = 2000
        };
        var point = new DevicePoint(
            "AI_PRESSURE",
            DevicePointProtocol.SiemensS7,
            "DB1.DBD0",
            DevicePointDataType.Decimal,
            false,
            WriteRiskLevel.Normal,
            null,
            null,
            null,
            null,
            "压力",
            "旧 Decimal 点",
            Guid.NewGuid().ToString(),
            device.Id,
            DriverKeyCatalog.SiemensS7);
        await using var channels = new ChannelManager(new[] { channel });
        await using var session = new DeviceSession(
            device,
            channel,
            new[] { point },
            new SimulationConfig(),
            new SystemClock(),
            channels,
            "test-revision");

        var result = await session.StartAsync();

        Assert.False(result.Ok);
        Assert.Contains("小数", result.Error, StringComparison.Ordinal);
    }


    [Fact]
    public async Task HardwareS7ConnectionFailure_DoesNotFallBackToSimulation()
    {
        var channel = new ChannelEntry
        {
            Id = Guid.NewGuid().ToString(),
            Code = "CH_PLC",
            Name = "PLC 通道",
            TransportKind = ChannelTransportKind.Tcp,
            Enabled = true,
            TimeoutMs = 200,
            RetryCount = 0,
            Tcp = new TcpChannelParameters { Host = "127.0.0.1", Port = 1 }
        };
        var device = new DeviceConfig.DeviceEntry
        {
            Id = Guid.NewGuid().ToString(),
            Code = "PLC1200",
            Name = "S7-1200",
            DeviceMode = DeviceMode.Hardware,
            DriverKey = DriverKeyCatalog.SiemensS7,
            Model = "S7-1200",
            ChannelId = channel.Id,
            PollIntervalMs = 500,
            StaleAfterMs = 2000
        };
        var point = new DevicePoint(
            "AI_PRESSURE",
            DevicePointProtocol.SiemensS7,
            "DB1.DBD0",
            DevicePointDataType.Float32,
            false,
            WriteRiskLevel.Normal,
            null,
            null,
            null,
            null,
            "压力",
            "连接失败测试",
            Guid.NewGuid().ToString(),
            device.Id,
            DriverKeyCatalog.SiemensS7);
        await using var channels = new ChannelManager(new[] { channel });
        await using var session = new DeviceSession(
            device,
            channel,
            new[] { point },
            new SimulationConfig(),
            new SystemClock(),
            channels,
            "test-revision");

        var result = await session.StartAsync();

        Assert.False(result.Ok);
        Assert.Contains("连接", result.Error, StringComparison.Ordinal);
        Assert.Contains("失败", result.Error, StringComparison.Ordinal);
        // 连接拒绝必须保持为连接错误，不能被 DB 绝对地址的 TIA Portal 提示覆盖。
        Assert.DoesNotContain("优化的块访问", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("slot=", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(DeviceConnectionState.Online, session.ConnectionState);
    }

    [Fact]
    public async Task FirstReadOfDbAbsoluteAddress_WhenDisconnectedReportsConnectionFailure()
    {
        var channel = new ChannelEntry
        {
            Id = Guid.NewGuid().ToString(),
            Code = "CH_PLC",
            Name = "PLC 通道",
            TransportKind = ChannelTransportKind.Tcp,
            Tcp = new TcpChannelParameters { Host = "127.0.0.1", Port = 102 }
        };
        var device = new DeviceConfig.DeviceEntry
        {
            Id = Guid.NewGuid().ToString(),
            Code = "PLC1200",
            Name = "S7-1200",
            DeviceMode = DeviceMode.Hardware,
            DriverKey = DriverKeyCatalog.SiemensS7,
            Model = "S7-1200",
            ChannelId = channel.Id
        };
        var point = Point(DevicePointDataType.Float32, "DB1.DBD0") with { DeviceId = device.Id };

        await using var runtime = new S7NetPlusDeviceRuntime(
            device,
            channel,
            new[] { point },
            new SystemClock(),
            "test-revision");

        var error = await Assert.ThrowsAsync<DomainException>(() => runtime.ReadFreshAsync(point.PointId));

        Assert.Contains("当前不可用", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("优化的块访问", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("DeviceConnectionState", error.Message, StringComparison.Ordinal);
    }

    private static DevicePoint Point(DevicePointDataType type, string address)
        => new(
            "P1",
            DevicePointProtocol.SiemensS7,
            address,
            type,
            false,
            WriteRiskLevel.Normal,
            null,
            null,
            null,
            null,
            "测试点",
            string.Empty,
            Guid.NewGuid().ToString(),
            "device-1",
            DriverKeyCatalog.SiemensS7);
}
