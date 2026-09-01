using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Ports;
using Xunit;

namespace XXX.TestBench.Core.Tests;

public class DeviceWritePipelineTests
{
    private static readonly DevicePoint WritablePoint = new("DO_Start", "Simulation", "sim.start", "Boolean", "", true, WriteRiskLevel.Normal, null, null, null, null);
    private static readonly DevicePoint HighRiskPoint = new("DO_Stop", "Simulation", "sim.stop", "Boolean", "", true, WriteRiskLevel.HighRisk, null, null, null, null);
    private static readonly DevicePoint ReadOnlyPoint = new("AI_Pressure", "Simulation", "sim.pressure", "Decimal", "MPa", false, WriteRiskLevel.Normal, null, null, null, null);

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
}
