using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Execution;
using XXX.TestBench.Core.Ports;
using XXX.TestBench.Devices.Drivers;
using XXX.TestBench.Devices.Executors;
using XXX.TestBench.Infrastructure.Configuration;

namespace XXX.TestBench.Integration.Tests;

public sealed class DeviceConfigurationBootstrapperTests
{
    [Fact]
    public async Task FirstLoadInitializesCurrentFilesAndNextLoadPrefersActiveRevision()
    {
        using var env = TestEnv.Create();
        var files = new JsonConfigStore(env.ConfigRoot);
        await using var store = new DeviceConfigurationStore(env.ConfigRoot);
        var bootstrapper = CreateBootstrapper(env.ConfigRoot, files, store);

        var first = await bootstrapper.LoadAsync();

        Assert.True(first.WasMigrated);
        Assert.True(File.Exists(Path.Combine(env.ConfigRoot, "device-config", "active.json")));
        Assert.Equal(DeviceConfig.CurrentSchemaVersion, first.Snapshot.Device.SchemaVersion);
        Assert.Equal(PointsConfig.CurrentSchemaVersion, first.Snapshot.Points.SchemaVersion);
        Assert.Equal(SimulationConfig.CurrentSchemaVersion, first.Snapshot.Simulation.SchemaVersion);
        var firstRevision = first.Snapshot.Revision;

        await File.WriteAllTextAsync(Path.Combine(env.ConfigRoot, "device.json"), "not used after active revision");
        var second = await bootstrapper.LoadAsync();

        Assert.False(second.WasMigrated);
        Assert.Equal(firstRevision, second.Snapshot.Revision);
        Assert.Equal("AI_Pressure", Assert.Single(second.Snapshot.Points.Points, point => point.Code == "AI_Pressure").Code);
    }

    [Fact]
    public async Task InvalidActiveSemanticConfiguration_UsesValidatedPreviousWithoutCommitting()
    {
        using var env = TestEnv.Create();
        var files = new JsonConfigStore(env.ConfigRoot);
        await using var store = new DeviceConfigurationStore(env.ConfigRoot);
        var first = await env.LoadSnapshotAsync();
        first.Revision = "previous-good";
        await store.StageAsync(first);
        await store.CommitActiveAsync(first.Revision);

        var invalidActive = await env.LoadSnapshotAsync();
        invalidActive.Revision = "active-unknown-binding";
        invalidActive.SignalBindings.Bindings["UNKNOWN_SIGNAL"] = invalidActive.Points.Points[0].Id;
        await store.StageAsync(invalidActive);
        await store.CommitActiveAsync(invalidActive.Revision);
        var activePath = Path.Combine(env.ConfigRoot, "device-config", "active.json");
        var invalidActivePointer = await File.ReadAllBytesAsync(activePath);

        var bootstrapper = CreateBootstrapper(env.ConfigRoot, files, store);
        var recovered = await bootstrapper.LoadAsync();

        Assert.True(recovered.UsedPrevious);
        Assert.Equal(first.Revision, recovered.SourceRevision);
        Assert.Contains("应用校验失败", recovered.RecoveryMessage, StringComparison.Ordinal);
        Assert.Equal(invalidActivePointer, await File.ReadAllBytesAsync(activePath));
    }

    [Fact]
    public async Task InvalidHistoryDoesNotRebuildFromCurrentDeploymentFiles()
    {
        using var env = TestEnv.Create();
        var files = new JsonConfigStore(env.ConfigRoot);
        await using var store = new DeviceConfigurationStore(env.ConfigRoot);
        var historyRoot = Path.Combine(env.ConfigRoot, "device-config");
        Directory.CreateDirectory(historyRoot);
        await File.WriteAllTextAsync(Path.Combine(historyRoot, "active.json"), "{}\n");

        var bootstrapper = CreateBootstrapper(env.ConfigRoot, files, store);
        await Assert.ThrowsAsync<ConfigValidationException>(() => bootstrapper.LoadAsync());

        Assert.False(Directory.Exists(Path.Combine(historyRoot, "revisions")));
        Assert.Equal("{}\n", await File.ReadAllTextAsync(Path.Combine(historyRoot, "active.json")));
    }

    [Fact]
    public async Task OldDeploymentSchemaIsRejectedWithoutInitializingRevision()
    {
        using var env = TestEnv.Create();
        var devicePath = Path.Combine(env.ConfigRoot, "device.json");
        var json = await File.ReadAllTextAsync(devicePath);
        await File.WriteAllTextAsync(devicePath, json.Replace("\"schemaVersion\": 5", "\"schemaVersion\": 3", StringComparison.Ordinal));
        var files = new JsonConfigStore(env.ConfigRoot);
        await using var store = new DeviceConfigurationStore(env.ConfigRoot);

        var bootstrapper = CreateBootstrapper(env.ConfigRoot, files, store);

        var error = await Assert.ThrowsAnyAsync<Exception>(() => bootstrapper.LoadAsync());
        Assert.Contains("请使用离线升级工具", error.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(env.ConfigRoot, "device-config", "revisions")));
    }

    private static DeviceConfigurationBootstrapper CreateBootstrapper(
        string configRoot,
        JsonConfigStore files,
        IDeviceConfigurationStore store)
    {
        var executors = new ExecutorFactory();
        var requiredSignals = executors.Codes.SelectMany(code => executors.Get(code).RequiredSignals).ToArray();
        return new DeviceConfigurationBootstrapper(
            configRoot,
            files,
            store,
            DriverRegistry.CreateDefault().Descriptors,
            requiredSignals);
    }
}
