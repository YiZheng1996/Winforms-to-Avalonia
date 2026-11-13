using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Execution;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Core.Tests;

public sealed class ProcessSignalBindingTests
{
    [Fact]
    public void MissingProcessBindings_DoNotMakeUnrelatedConfigurationInvalid()
    {
        var snapshot = CreateSnapshot(
            new PointsConfig.PointEntry
            {
                Id = "10000000-0000-0000-0000-000000000001",
                Code = "PRESSURE",
                Name = "压力",
                Protocol = "SiemensS7",
                RawDataType = "Float32",
                DataType = "Float32",
                DeviceId = "20000000-0000-0000-0000-000000000001",
                GroupId = "30000000-0000-0000-0000-000000000001",
                Address = "DB1.DBD0"
            });

        var issues = MultiDeviceConfigurationValidator.Validate(snapshot, requiredSignals: Array.Empty<RequiredSignal>());

        Assert.DoesNotContain(issues, issue => issue.Path.StartsWith("signal-bindings", StringComparison.Ordinal));
    }

    [Fact]
    public void PressureSetpoint_RequiresNumericWritablePointAndEngineeringRange()
    {
        var point = new PointsConfig.PointEntry
        {
            Id = "10000000-0000-0000-0000-000000000001",
            Code = "SETPOINT",
            Name = "调压目标",
            Protocol = "SiemensS7",
            RawDataType = "Boolean",
            DataType = "Boolean",
            IsWritable = false,
            DeviceId = "20000000-0000-0000-0000-000000000001",
            GroupId = "30000000-0000-0000-0000-000000000001",
            Address = "DB1.DBX0.0"
        };
        var snapshot = CreateSnapshot(point);
        snapshot.SignalBindings.Bindings[ProcessSignalCatalog.PressureSetpoint] = point.Id;

        var issues = MultiDeviceConfigurationValidator.Validate(snapshot, requiredSignals: Array.Empty<RequiredSignal>());

        Assert.Contains(issues, issue => issue.Message.Contains("类型不匹配", StringComparison.Ordinal));
        Assert.Contains(issues, issue => issue.Message.Contains("可写", StringComparison.Ordinal));
        Assert.Contains(issues, issue => issue.Message.Contains("工程量程", StringComparison.Ordinal));
    }

    [Fact]
    public void SnapshotReader_RejectsStaleRevisionAndKeepsUnboundStateExplicit()
    {
        var now = new DateTime(2026, 9, 16, 8, 0, 0, DateTimeKind.Utc);
        var point = new DevicePoint(
            "主管压力",
            DevicePointProtocol.SiemensS7,
            "DB1.DBD0",
            DevicePointDataType.Float32,
            false,
            WriteRiskLevel.Normal,
            null,
            null,
            0,
            1,
            "主管压力",
            PointId: "10000000-0000-0000-0000-000000000001",
            DeviceId: "20000000-0000-0000-0000-000000000001",
            Revision: "r1");
        var runtime = new SnapshotRuntime(
            point,
            new PointValue(
                point.Code,
                point.Address,
                PointQuality.Good,
                0.42f,
                now.AddSeconds(-1),
                point.PointId,
                point.DeviceId,
                7,
                "old-revision"),
            ProcessSignalCatalog.MainPressure);

        var snapshot = new ProcessSnapshotReader().Capture(runtime, now);

        Assert.Equal(ProcessDataState.InvalidBinding, snapshot[ProcessSignalCatalog.MainPressure].State);
        Assert.Equal(ProcessDataState.Unbound, snapshot[ProcessSignalCatalog.SafetyDoor].State);
    }

    private static DeviceConfigurationSnapshot CreateSnapshot(PointsConfig.PointEntry point)
    {
        var channel = new ChannelEntry
        {
            Id = "10000000-0000-0000-0000-000000000010",
            Code = "CH_SIM",
            Name = "仿真通道",
            TransportKind = ChannelTransportKind.Tcp,
            Tcp = new TcpChannelParameters { Host = "127.0.0.1", Port = 102 }
        };
        return new DeviceConfigurationSnapshot
        {
            Revision = "r1",
            Device = new DeviceConfig
            {
                SchemaVersion = DeviceConfig.CurrentSchemaVersion,
                Channels = new List<ChannelEntry> { channel },
                Devices = new List<DeviceConfig.DeviceEntry>
                {
                    new()
                    {
                        Id = "20000000-0000-0000-0000-000000000001",
                        Code = "DEV_SIM",
                        Name = "仿真设备",
                        ChannelId = channel.Id,
                        DriverKey = DriverKeyCatalog.SiemensS7,
                        Protocol = "SiemensS7",
                        Address = "127.0.0.1",
                        Model = "S7-1500",
                        DeviceMode = DeviceMode.Simulation,
                        PollIntervalMs = 500,
                        StaleAfterMs = 1500
                    }
                }
            },
            Points = new PointsConfig
            {
                SchemaVersion = PointsConfig.CurrentSchemaVersion,
                Groups = new List<PointsConfig.PointGroupEntry>
                {
                    new()
                    {
                        Id = "30000000-0000-0000-0000-000000000001",
                        DeviceId = "20000000-0000-0000-0000-000000000001",
                        Code = "DEFAULT",
                        Name = "未分组"
                    }
                },
                Points = new List<PointsConfig.PointEntry> { point }
            },
            Simulation = new SimulationConfig
            {
                SchemaVersion = SimulationConfig.CurrentSchemaVersion,
                InitialValuesByPointId = new Dictionary<string, object?> { [point.Id] = 0f }
            },
            SignalBindings = new SignalBindingsConfig()
        };
    }

    private sealed class SnapshotRuntime : IDeviceRuntime
    {
        private readonly DevicePoint _point;
        private readonly PointValue _value;
        private readonly string _signalKey;

        public SnapshotRuntime(DevicePoint point, PointValue value, string signalKey)
        {
            _point = point;
            _value = value;
            _signalKey = signalKey;
        }

        public string Name => "测试运行时";
        public DeviceMode Mode => DeviceMode.Simulation;
        public bool IsSimulation => true;
        public DeviceRuntimeInfo Status => GetDeviceStatus(_point.DeviceId);
        public string ActiveRevision => "r1";
        public SignalBindingsConfig SignalBindings => new()
        {
            Bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [_signalKey] = _point.PointId
            }
        };
        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task<PointValue> ReadAsync(DevicePoint point, CancellationToken ct = default)
            => Task.FromResult(_value);
        public Task<PointValue> WriteAsync(DevicePoint point, object? value, CancellationToken ct = default)
            => Task.FromResult(_value);
        public Task<IReadOnlyList<DevicePoint>> ListPointsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DevicePoint>>(new[] { _point });
        public bool TryGetCachedValue(string pointId, out PointValue value)
        {
            value = _value;
            return string.Equals(pointId, _point.PointId, StringComparison.OrdinalIgnoreCase);
        }
        public DevicePoint? GetPoint(string pointId)
            => string.Equals(pointId, _point.PointId, StringComparison.OrdinalIgnoreCase) ? _point : null;
        public DeviceRuntimeInfo GetDeviceStatus(string deviceId)
            => new(
                "仿真设备",
                "S7",
                "sim://1",
                true,
                DeviceHealth.Healthy,
                true,
                null,
                deviceId,
                ConnectionState: DeviceConnectionState.Online,
                Revision: "r1",
                ConnectionGeneration: 7,
                Mode: DeviceMode.Simulation);
    }
}
