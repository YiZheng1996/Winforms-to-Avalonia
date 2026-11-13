using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Devices;
using XXX.TestBench.Infrastructure.Configuration;
using XXX.TestBench.Infrastructure.Identity;
using XXX.TestBench.Infrastructure.Logging;
using XXX.TestBench.Infrastructure.Persistence;
using XXX.TestBench.Infrastructure.Time;
using Xunit;

namespace XXX.TestBench.Integration.Tests;

public class DeviceModeControllerIntegrationTests
{
    [Fact]
    public async Task HardwareInitFailure_KeepsHardwareMode_Faulted_NoFallback()
    {
        using var env = TestEnv.Create("Hardware");
        var snapshot = await env.LoadSnapshotAsync();

        var dbFactory = new SqliteConnectionFactory(env.DbPath);
        await new SqliteDatabase(dbFactory, new Pbkdf2PasswordHasher())
            .CreateNewAsync(Guid.NewGuid().ToString("D").ToLowerInvariant(), "admin123");
        var factory = new DeviceRuntimeFactory(
            snapshot, new SystemClock(), requiredSignals: TestEnv.RegisteredRequiredSignals());
        var audit = new SqliteAuditLog(dbFactory);
        var controller = new DeviceModeController(factory, new FileLogger(Path.Combine(env.DataRoot, "logs")), audit);

        var result = await controller.InitializeAsync();

        Assert.False(result.Ok);
        Assert.Equal(DeviceMode.Hardware, controller.CurrentMode);
        Assert.Equal(DeviceHealth.Faulted, controller.Health);
        Assert.NotNull(controller.Runtime);
        Assert.False(controller.Runtime!.ActivationReport.AllOnline);
        Assert.NotEmpty(controller.Runtime.ActivationReport.Issues);
    }

    [Fact]
    public async Task SimulationInit_Succeeds_FromDeviceConfiguration()
    {
        using var env = TestEnv.Create();
        var snapshot = await env.LoadSnapshotAsync();

        var dbFactory = new SqliteConnectionFactory(env.DbPath);
        await new SqliteDatabase(dbFactory, new Pbkdf2PasswordHasher())
            .CreateNewAsync(Guid.NewGuid().ToString("D").ToLowerInvariant(), "admin123");
        var factory = new DeviceRuntimeFactory(
            snapshot, new SystemClock(), requiredSignals: TestEnv.RegisteredRequiredSignals());
        var audit = new SqliteAuditLog(dbFactory);
        var controller = new DeviceModeController(factory, new FileLogger(Path.Combine(env.DataRoot, "logs")), audit);

        var result = await controller.InitializeAsync();
        Assert.True(result.Ok);
        Assert.Equal(DeviceHealth.Healthy, controller.Health);
        Assert.True(controller.Runtime!.IsSimulation);
    }
}
