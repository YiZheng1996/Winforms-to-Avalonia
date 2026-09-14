using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Devices;
using XXX.TestBench.Devices.Runtime;
using Xunit;

namespace XXX.TestBench.Integration.Tests;

public sealed class MultiDeviceRuntimeTests
{
    [Fact]
    public async Task SamePhysicalAddressOnTwoDevices_InPerDeviceSimulationMode_IsRoutedByPointId()
    {
        var snapshot = CreateSnapshot();
        await using var runtime = new MultiDeviceRuntime(snapshot, new TestClock());

        await runtime.StartAsync();
        Assert.Equal(DeviceHealth.Healthy, runtime.Status.Health);

        var point1 = runtime.GetPoint(snapshot.Points.Points[0].Id)!;
        var point2 = runtime.GetPoint(snapshot.Points.Points[1].Id)!;
        var first = await runtime.ReadFreshAsync(point1.PointId);
        var second = await runtime.ReadFreshAsync(point2.PointId);

        Assert.Equal(1d, Convert.ToDouble(first.Value));
        Assert.Equal(2d, Convert.ToDouble(second.Value));
        Assert.Equal(point1.DeviceId, first.DeviceId);
        Assert.Equal(point2.DeviceId, second.DeviceId);

        await runtime.WriteAsync(point1, 5d);
        var secondAfterWrite = await runtime.ReadFreshAsync(point2.PointId);
        Assert.Equal(2d, Convert.ToDouble(secondAfterWrite.Value));

        var staleObject = point1 with { PointId = string.Empty };
        await Assert.ThrowsAsync<XXX.TestBench.Core.Common.DomainException>(
            () => runtime.ReadAsync(staleObject));

        await runtime.StopAsync();
        await runtime.StopAsync();
    }

    [Fact]
    public async Task SharedChannelSerializesRequests()
    {
        var snapshot = CreateSnapshot();
        // 该用例只验证匿名通道操作的串行门；关闭设备固定轮询，
        // 避免两个 TCP 设备各自的后台轮询干扰 MaxInFlight 指标。
        foreach (var device in snapshot.Device.Devices)
            device.ScanMode = DeviceScanMode.OnDemand;
        await using var runtime = new MultiDeviceRuntime(snapshot, new TestClock());
        await runtime.StartAsync();

        var concurrent = 0;
        var observedMaximum = 0;
        var tasks = Enumerable.Range(0, 8)
            .Select(_ => runtime.Channels.ExecuteAsync(
                snapshot.Device.Channels[0].Id,
                async ct =>
                {
                    var current = Interlocked.Increment(ref concurrent);
                    InterlockedMax(ref observedMaximum, current);
                    await Task.Delay(10, ct);
                    Interlocked.Decrement(ref concurrent);
                    return true;
                }))
            .ToArray();

        await Task.WhenAll(tasks);

        Assert.Equal(1, observedMaximum);
        Assert.Equal(1, runtime.Channels.Sessions.Single().MaxInFlight);
    }

    [Fact]
    public async Task FactoryCarriesSignalBindingsIntoV2Runtime()
    {
        var snapshot = CreateSnapshot();
        var point = snapshot.Points.Points[0];
        snapshot.SignalBindings.Bindings["AI_Pressure"] = point.Id;

        var factory = new DeviceRuntimeFactory(
            snapshot.Device,
            snapshot.Points,
            snapshot.Simulation,
            new TestClock(),
            snapshot.Revision,
            snapshot.SignalBindings);
        await using var runtime = await factory.CreateAsync();

        Assert.Equal(point.Id, runtime.SignalBindings.Bindings["AI_Pressure"]);
        Assert.Equal(snapshot.Revision, runtime.ActiveRevision);
    }

    private static DeviceConfigurationSnapshot CreateSnapshot()
    {
        var channel = new ChannelEntry
        {
            Id = Guid.NewGuid().ToString("D"),
            Code = "PLC_SHARED",
            Name = "PLC 网络通道",
            TransportKind = ChannelTransportKind.Tcp,
            Tcp = new TcpChannelParameters { Host = "127.0.0.1", Port = 102 }
        };
        var device1 = CreateDevice("PLC1", channel.Id);
        var device2 = CreateDevice("PLC2", channel.Id);
        var group1 = CreateGroup(device1.Id, "DEFAULT");
        var group2 = CreateGroup(device2.Id, "DEFAULT");
        var point1 = CreatePoint("AI_PRESSURE_1", device1.Id, group1.Id);
        var point2 = CreatePoint("AI_PRESSURE_2", device2.Id, group2.Id);
        var simulation = new SimulationConfig
        {
            SchemaVersion = SimulationConfig.CurrentSchemaVersion
        };
        simulation.InitialValuesByPointId[point1.Id] = 1d;
        simulation.InitialValuesByPointId[point2.Id] = 2d;

        return new DeviceConfigurationSnapshot
        {
            Revision = "runtime-test",
            Device = new DeviceConfig
            {
                SchemaVersion = DeviceConfig.CurrentSchemaVersion,
                Channels = new List<ChannelEntry> { channel },
                Devices = new List<DeviceConfig.DeviceEntry> { device1, device2 }
            },
            Points = new PointsConfig
            {
                SchemaVersion = PointsConfig.CurrentSchemaVersion,
                Groups = new List<PointsConfig.PointGroupEntry> { group1, group2 },
                Points = new List<PointsConfig.PointEntry> { point1, point2 }
            },
            Simulation = simulation,
            SignalBindings = new SignalBindingsConfig()
        };
    }

    private static DeviceConfig.DeviceEntry CreateDevice(string code, string channelId)
        => new()
        {
            Id = Guid.NewGuid().ToString("D"),
            Code = code,
            Name = code,
            ChannelId = channelId,
            DriverKey = DriverKeyCatalog.SiemensS7,
            Model = "S7-1500",
            DeviceMode = DeviceMode.Simulation,
            PollIntervalMs = 500,
            StaleAfterMs = 2000
        };

    private static PointsConfig.PointGroupEntry CreateGroup(string deviceId, string code)
        => new()
        {
            Id = Guid.NewGuid().ToString("D"),
            DeviceId = deviceId,
            Code = code,
            Name = "未分组"
        };

    private static PointsConfig.PointEntry CreatePoint(string code, string deviceId, string groupId)
        => new()
        {
            Id = Guid.NewGuid().ToString("D"),
            Code = code,
            Name = code,
            DeviceId = deviceId,
            GroupId = groupId,
            Address = "DB1.DBD0",
            Protocol = "SiemensS7",
            DataType = "Float32",
            RawDataType = "Float32",
            IsWritable = true
        };

    private static void InterlockedMax(ref int target, int value)
    {
        while (value > Volatile.Read(ref target)
               && Interlocked.CompareExchange(ref target, value, Volatile.Read(ref target)) != Volatile.Read(ref target))
        {
        }
    }

    private sealed class TestClock : XXX.TestBench.Core.Common.IClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }
}
