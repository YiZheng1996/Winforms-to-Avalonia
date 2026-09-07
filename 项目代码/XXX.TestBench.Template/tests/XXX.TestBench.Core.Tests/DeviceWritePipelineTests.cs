using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Ports;
using Xunit;

namespace XXX.TestBench.Core.Tests;

public class DeviceWritePipelineTests
{
    private static readonly DevicePoint WritablePoint = new("DO_Start", DevicePointProtocol.Simulation, "sim.start", DevicePointDataType.Boolean, "", true, WriteRiskLevel.Normal, null, null, null, null);
    private static readonly DevicePoint HighRiskPoint = new("DO_Stop", DevicePointProtocol.Simulation, "sim.stop", DevicePointDataType.Boolean, "", true, WriteRiskLevel.HighRisk, null, null, null, null);
    private static readonly DevicePoint ReadOnlyPoint = new("AI_Pressure", DevicePointProtocol.Simulation, "sim.pressure", DevicePointDataType.Decimal, "MPa", false, WriteRiskLevel.Normal, null, null, null, null);

    private static (DeviceWritePipeline Pipeline, FakeAuditLog Audit) Create()
    {
        var audit = new FakeAuditLog();
        var logger = new SilentLogger();
        return (new DeviceWritePipeline(audit, logger), audit);
    }

    private sealed class SilentLogger : IAppLogger
    {
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, Exception? exception = null) { }
    }

    private static WriteCommand Cmd(UserContext actor, DevicePoint point, object? value, DeviceMode mode, IDeviceRuntime runtime,
        bool activeRun = true, bool confirmed = true) => new(actor, point, value, mode, runtime, activeRun, confirmed);

    [Fact]
    public async Task Write_SimulationMode_WritesSimulationState()
    {
        var (pipeline, _) = Create();
        var runtime = new FakeRuntime(isSimulation: true);
        var result = await pipeline.ExecuteAsync(Cmd(TestContexts.With(PermissionCode.ManualControl), WritablePoint, true, DeviceMode.Simulation, runtime));

        Assert.Equal(PointQuality.Good, result.Quality);
        Assert.Single(runtime.Writes);
    }

    [Fact]
    public async Task Write_SimulationMode_CannotWriteHardwareRuntime()
    {
        var (pipeline, _) = Create();
        var runtime = new FakeRuntime(isSimulation: false);
        await Assert.ThrowsAsync<DomainException>(() =>
            pipeline.ExecuteAsync(Cmd(TestContexts.With(PermissionCode.ManualControl), WritablePoint, true, DeviceMode.Simulation, runtime)));
    }

    [Fact]
    public async Task Write_HardwareMode_CannotWriteSimulationRuntime()
    {
        var (pipeline, _) = Create();
        var runtime = new FakeRuntime(isSimulation: true);
        await Assert.ThrowsAsync<DomainException>(() =>
            pipeline.ExecuteAsync(Cmd(TestContexts.With(PermissionCode.ManualControl), WritablePoint, true, DeviceMode.Hardware, runtime)));
    }

    [Fact]
    public async Task Write_RequiresPermission()
    {
        var (pipeline, _) = Create();
        var runtime = new FakeRuntime(isSimulation: true);
        await Assert.ThrowsAsync<AuthorizationException>(() =>
            pipeline.ExecuteAsync(Cmd(TestContexts.With(PermissionCode.ViewOverview), WritablePoint, true, DeviceMode.Simulation, runtime)));
    }

    [Fact]
    public async Task Write_RequiresConnectedHealthyRuntime()
    {
        var (pipeline, _) = Create();
        var runtime = new FakeRuntime(isSimulation: true, health: DeviceHealth.Faulted, connected: false);
        await Assert.ThrowsAsync<DomainException>(() =>
            pipeline.ExecuteAsync(Cmd(TestContexts.With(PermissionCode.ManualControl), WritablePoint, true, DeviceMode.Simulation, runtime)));
    }

    [Fact]
    public async Task Write_RequiresActiveRun()
    {
        var (pipeline, _) = Create();
        var runtime = new FakeRuntime(isSimulation: true);
        await Assert.ThrowsAsync<DomainException>(() =>
            pipeline.ExecuteAsync(Cmd(TestContexts.With(PermissionCode.ManualControl), WritablePoint, true, DeviceMode.Simulation, runtime, activeRun: false)));
    }

    [Fact]
    public async Task HighRiskWrite_RequiresConfirmation_AndCalibratePermission()
    {
        var (pipeline, audit) = Create();
        var runtime = new FakeRuntime(isSimulation: true);

        await Assert.ThrowsAsync<DomainException>(() =>
            pipeline.ExecuteAsync(Cmd(TestContexts.With(PermissionCode.CalibrateDevices), HighRiskPoint, true, DeviceMode.Simulation, runtime, confirmed: false)));
        await Assert.ThrowsAsync<AuthorizationException>(() =>
            pipeline.ExecuteAsync(Cmd(TestContexts.With(PermissionCode.ManualControl), HighRiskPoint, true, DeviceMode.Simulation, runtime, confirmed: true)));

        var ok = await pipeline.ExecuteAsync(Cmd(TestContexts.With(PermissionCode.CalibrateDevices), HighRiskPoint, true, DeviceMode.Simulation, runtime, confirmed: true));
        Assert.Equal(PointQuality.Good, ok.Quality);
    }

    [Fact]
    public async Task Write_ReadOnlyPoint_IsRejected()
    {
        var (pipeline, _) = Create();
        var runtime = new FakeRuntime(isSimulation: true);
        await Assert.ThrowsAsync<DomainException>(() =>
            pipeline.ExecuteAsync(Cmd(TestContexts.With(PermissionCode.ManualControl), ReadOnlyPoint, 1.0m, DeviceMode.Simulation, runtime)));
    }

    [Fact]
    public async Task HardwareWrite_PerformsAudit()
    {
        var (pipeline, audit) = Create();
        var runtime = new FakeRuntime(isSimulation: false);
        var result = await pipeline.ExecuteAsync(Cmd(TestContexts.With(PermissionCode.ManualControl), WritablePoint, true, DeviceMode.Hardware, runtime));

        Assert.Equal(PointQuality.Good, result.Quality);
        Assert.Contains(audit.Entries, e => e.Contains("DeviceWrite"));
    }

    [Fact]
    public async Task HardwareWrite_ReadbackMismatch_IsRejected()
    {
        var (pipeline, _) = Create();
        var runtime = new FakeRuntime(isSimulation: false, readOverride: _ => "999");
        await Assert.ThrowsAsync<DomainException>(() =>
            pipeline.ExecuteAsync(Cmd(TestContexts.With(PermissionCode.ManualControl), WritablePoint, true, DeviceMode.Hardware, runtime)));
    }

    [Fact]
    public async Task V2Write_UsesCurrentPointById_AndFreshReadback()
    {
        var (pipeline, _) = Create();
        var current = V2Point();
        var runtime = new IdentityRuntime(current);
        var staleDefinition = current with { IsWritable = false, Address = "sim.old", Revision = current.Revision };

        var result = await pipeline.ExecuteAsync(Cmd(
            TestContexts.With(PermissionCode.ManualControl), staleDefinition, 3.5m,
            DeviceMode.Simulation, runtime));

        Assert.Equal(PointQuality.Good, result.Quality);
        Assert.Equal(1, runtime.WriteCount);
        Assert.Equal(1, runtime.FreshReadCount);
        Assert.Equal(3.5m, runtime.LastWritten);
    }

    [Fact]
    public async Task V2Write_RejectsExpiredDefinitionBeforeDriverCall()
    {
        var (pipeline, _) = Create();
        var current = V2Point();
        var runtime = new IdentityRuntime(current);
        var expired = current with { Revision = "old-revision" };

        await Assert.ThrowsAsync<DomainException>(() => pipeline.ExecuteAsync(Cmd(
            TestContexts.With(PermissionCode.ManualControl), expired, 1m,
            DeviceMode.Simulation, runtime)));

        Assert.Equal(0, runtime.WriteCount);
    }

    [Fact]
    public async Task V2Write_ConvertsEngineeringValueToRawValue()
    {
        var (pipeline, _) = Create();
        var current = V2Point() with
        {
            DataType = DevicePointDataType.Int16,
            RawMin = 0,
            RawMax = 100,
            EngMin = 0,
            EngMax = 10
        };
        var runtime = new IdentityRuntime(current);

        await pipeline.ExecuteAsync(Cmd(
            TestContexts.With(PermissionCode.ManualControl), current, 5m,
            DeviceMode.Simulation, runtime));

        Assert.Equal((short)50, runtime.LastWritten);
    }

    [Fact]
    public async Task V2Write_CancelledAfterSend_IsUncertainAndAudited()
    {
        var (pipeline, audit) = Create();
        var current = V2Point();
        var runtime = new IdentityRuntime(current) { CancelDuringWrite = true };

        await Assert.ThrowsAsync<DeviceWriteUncertainException>(() => pipeline.ExecuteAsync(Cmd(
            TestContexts.With(PermissionCode.ManualControl), current, 1m,
            DeviceMode.Simulation, runtime)));

        Assert.Contains(audit.Entries, entry => entry.Contains("DeviceWriteUncertain", StringComparison.Ordinal));
    }

    private static DevicePoint V2Point()
        => new(
            "P_V2",
            DevicePointProtocol.Simulation,
            "sim.p2",
            DevicePointDataType.Decimal,
            "MPa",
            true,
            WriteRiskLevel.Normal,
            null,
            null,
            null,
            null,
            Name: "P2",
            PointId: "30000000-0000-5000-8000-000000000002",
            DeviceId: "20000000-0000-5000-8000-000000000002",
            DriverKey: DriverKeyCatalog.Simulation,
            Revision: "current-revision");

    private sealed class IdentityRuntime(DevicePoint current) : IDeviceRuntime
    {
        private object? _value;

        public bool CancelDuringWrite { get; set; }
        public int WriteCount { get; private set; }
        public int FreshReadCount { get; private set; }
        public object? LastWritten { get; private set; }
        public string Name => "identity-runtime";
        public DeviceMode Mode => DeviceMode.Simulation;
        public bool IsSimulation => true;
        public DeviceRuntimeInfo Status => new(Name, "simulation", "sim://identity", true,
            DeviceHealth.Healthy, true, null, current.DeviceId, "channel-2", current.Revision, 1, DeviceConnectionState.Online);
        public string ActiveRevision => current.Revision;
        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task<IReadOnlyList<DevicePoint>> ListPointsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DevicePoint>>(new[] { current });
        public Task<PointValue> ReadAsync(DevicePoint point, CancellationToken ct = default)
            => Task.FromResult(Value(_value));
        public Task<PointValue> ReadFreshAsync(string pointId, CancellationToken ct = default)
        {
            FreshReadCount++;
            return Task.FromResult(Value(_value));
        }
        public Task<PointValue> WriteAsync(DevicePoint point, object? value, CancellationToken ct = default)
        {
            WriteCount++;
            LastWritten = value;
            _value = value;
            if (CancelDuringWrite) throw new OperationCanceledException("模拟写入超时");
            return Task.FromResult(Value(value));
        }
        public DevicePoint? GetPoint(string pointId)
            => string.Equals(pointId, current.PointId, StringComparison.OrdinalIgnoreCase) ? current : null;
        public DeviceRuntimeInfo GetDeviceStatus(string deviceId) => Status with { DeviceId = deviceId };

        private PointValue Value(object? value)
            => new(current.Code, current.Address, PointQuality.Good, value, DateTime.UtcNow,
                current.PointId, current.DeviceId, 1, current.Revision);
    }
}
