using System.Text.Json;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Infrastructure.Configuration;

/// <summary>
/// 应用启动时加载完整设备配置：优先读取 active 指针，没有生效版本时才迁移旧四文件。
/// </summary>
public sealed class DeviceConfigurationBootstrapper
{
    private readonly string _configRoot;
    private readonly IConfigStore _legacyStore;
    private readonly IDeviceConfigurationStore _versionedStore;

    public DeviceConfigurationBootstrapper(
        string configRoot,
        IConfigStore legacyStore,
        IDeviceConfigurationStore versionedStore)
    {
        _configRoot = Path.GetFullPath(configRoot);
        _legacyStore = legacyStore;
        _versionedStore = versionedStore;
    }

    /// <summary>
    /// 返回启动快照；迁移只在首次没有 active 指针时执行，并立即以完整 revision 生效。
    /// </summary>
    public async Task<DeviceConfigurationBootstrapResult> LoadAsync(CancellationToken ct = default)
    {
        var activePath = Path.Combine(_configRoot, "device-config", "active.json");
        if (File.Exists(activePath))
        {
            var active = await _versionedStore.LoadActiveAsync(ct);
            return await MigrateActiveIfNeededAsync(active, ct);
        }

        var legacyDevice = await _legacyStore.LoadAsync<DeviceConfig>("device.json", ct);
        var legacyPoints = await _legacyStore.LoadAsync<PointsConfig>("points.json", ct);
        var legacySimulation = await _legacyStore.LoadAsync<SimulationConfig>("simulation.json", ct);
        if (legacyDevice.SchemaVersion == DeviceConfig.CurrentSchemaVersion
            && (legacyPoints.SchemaVersion == PointsConfig.PreviousSchemaVersion
                || legacyPoints.SchemaVersion == PointsConfig.CurrentSchemaVersion)
            && legacySimulation.SchemaVersion == SimulationConfig.CurrentSchemaVersion)
            return await LoadV2FilesAsync(ct);
        if (legacyDevice.SchemaVersion != DeviceConfig.LegacySchemaVersion
            || legacyPoints.SchemaVersion != PointsConfig.LegacySchemaVersion
            || legacySimulation.SchemaVersion != SimulationConfig.LegacySchemaVersion)
            throw new ConfigValidationException("设备配置文件版本不一致，不能在启动时猜测迁移方式");
        legacyDevice.Validate();
        legacyPoints.Validate();
        legacySimulation.Validate();

        var migration = DeviceConfigurationMigrator.Migrate(legacyDevice, legacyPoints, legacySimulation);
        if (!migration.IsValid)
            throw new ConfigValidationException("旧版设备配置迁移失败：" + string.Join("；", migration.Issues));

        await _versionedStore.StageAsync(migration.Candidate, ct);
        await _versionedStore.CommitActiveAsync(migration.Candidate.Revision, ct);
        return new DeviceConfigurationBootstrapResult(
            migration.Candidate,
            true,
            "已将旧版 device.json、points.json、simulation.json 迁移为完整设备配置版本");
    }

    /// <summary>
    /// 当配置目录已经由外部工具写成 v2 四文件、但尚未建立 active 指针时，按内容生成稳定 revision。
    /// </summary>
    public async Task<DeviceConfigurationBootstrapResult> LoadV2FilesAsync(CancellationToken ct = default)
    {
        var activePath = Path.Combine(_configRoot, "device-config", "active.json");
        if (File.Exists(activePath))
            return new DeviceConfigurationBootstrapResult(await _versionedStore.LoadActiveAsync(ct), false, null);

        var device = await _legacyStore.LoadAsync<DeviceConfig>("device.json", ct);
        var points = await _legacyStore.LoadAsync<PointsConfig>("points.json", ct);
        var simulation = await _legacyStore.LoadAsync<SimulationConfig>("simulation.json", ct);
        var bindings = await TryLoadBindingsAsync(ct);
        var seed = JsonSerializer.Serialize(new { device, points, simulation, bindings });
        var snapshot = new DeviceConfigurationSnapshot
        {
            Revision = "bootstrap-" + DeviceConfigurationMigrator.StableId(seed)[..8],
            Device = device,
            Points = points,
            Simulation = simulation,
            SignalBindings = bindings
        };
        MultiDeviceConfigurationValidator.EnsureValid(snapshot);
        var result = await MigrateActiveIfNeededAsync(snapshot, ct);
        if (result.WasMigrated)
            return result with { Message = "已将 v2 配置文件建立为完整生效版本，并生成默认点位分组" };

        await _versionedStore.StageAsync(snapshot, ct);
        await _versionedStore.CommitActiveAsync(snapshot.Revision, ct);
        return new DeviceConfigurationBootstrapResult(snapshot, true, "已将当前配置文件建立为完整生效版本");
    }

    private async Task<DeviceConfigurationBootstrapResult> MigrateActiveIfNeededAsync(
        DeviceConfigurationSnapshot active,
        CancellationToken ct)
    {
        if (active.Points.SchemaVersion == PointsConfig.CurrentSchemaVersion)
            return new DeviceConfigurationBootstrapResult(active, false, null);

        var migration = DeviceConfigurationMigrator.MigrateToV3(active);
        if (!migration.IsValid)
            throw new ConfigValidationException("旧版点位配置迁移失败：" + string.Join("；", migration.Issues));

        await _versionedStore.StageAsync(migration.Candidate, ct);
        await _versionedStore.CommitActiveAsync(migration.Candidate.Revision, ct);
        return new DeviceConfigurationBootstrapResult(
            migration.Candidate,
            true,
            "已将 v2 点位配置迁移为 v3，并为每个设备生成 DEFAULT/未分组");
    }

    private async Task<SignalBindingsConfig> TryLoadBindingsAsync(CancellationToken ct)
    {
        try { return await _legacyStore.LoadAsync<SignalBindingsConfig>("signal-bindings.json", ct); }
        catch (ConfigValidationException) when (!File.Exists(Path.Combine(_configRoot, "signal-bindings.json")))
        {
            return new SignalBindingsConfig();
        }
    }
}

public sealed record DeviceConfigurationBootstrapResult(
    DeviceConfigurationSnapshot Snapshot,
    bool WasMigrated,
    string? Message);
