using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Execution;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Infrastructure.Configuration;

/// <summary>
/// Loads a validated current revision, or creates the first revision from the four current deployment files.
/// Historical conversion is performed only by the offline upgrade tool.
/// </summary>
public sealed class DeviceConfigurationBootstrapper
{
    private readonly string _configRoot;
    private readonly IConfigStore _currentFiles;
    private readonly IDeviceConfigurationStore _versionedStore;
    private readonly IReadOnlyList<IDeviceDriverDescriptor> _drivers;
    private readonly IReadOnlyList<RequiredSignal> _requiredSignals;

    public DeviceConfigurationBootstrapper(
        string configRoot,
        IConfigStore currentFiles,
        IDeviceConfigurationStore versionedStore,
        IEnumerable<IDeviceDriverDescriptor> drivers,
        IEnumerable<RequiredSignal> requiredSignals)
    {
        _configRoot = Path.GetFullPath(configRoot);
        _currentFiles = currentFiles ?? throw new ArgumentNullException(nameof(currentFiles));
        _versionedStore = versionedStore ?? throw new ArgumentNullException(nameof(versionedStore));
        _drivers = (drivers ?? throw new ArgumentNullException(nameof(drivers))).ToArray();
        _requiredSignals = (requiredSignals ?? throw new ArgumentNullException(nameof(requiredSignals))).ToArray();
    }

    public async Task<DeviceConfigurationBootstrapResult> LoadAsync(CancellationToken ct = default)
    {
        if (HasConfigurationHistory())
        {
            var active = await _versionedStore.LoadActiveAsync(ct);
            var recoveryMessage = _versionedStore.LastRecoveryMessage;
            try
            {
                MultiDeviceConfigurationValidator.EnsureApplicationValid(active, _drivers, _requiredSignals);
            }
            catch (ConfigValidationException activeSemanticError) when (string.IsNullOrWhiteSpace(recoveryMessage))
            {
                try
                {
                    var previous = await _versionedStore.LoadPreviousAsync(ct);
                    MultiDeviceConfigurationValidator.EnsureApplicationValid(previous, _drivers, _requiredSignals);
                    active = previous;
                    recoveryMessage = $"当前生效配置应用校验失败，已只读恢复上一版本：{previous.Revision}；{activeSemanticError.Message}";
                }
                catch (Exception previousError) when (previousError is IOException or UnauthorizedAccessException
                    or JsonException or ConfigValidationException or InvalidOperationException)
                {
                    throw new ConfigValidationException(
                        $"当前生效配置应用校验失败：{activeSemanticError.Message}；上一版本也不可用：{previousError.Message}");
                }
            }
            return new DeviceConfigurationBootstrapResult(active, false, recoveryMessage)
            {
                UsedPrevious = !string.IsNullOrWhiteSpace(recoveryMessage),
                SourceRevision = active.Revision,
                RecoveryMessage = recoveryMessage
            };
        }

        var device = await _currentFiles.LoadAsync<DeviceConfig>("device.json", ct);
        var points = await _currentFiles.LoadAsync<PointsConfig>("points.json", ct);
        var simulation = await _currentFiles.LoadAsync<SimulationConfig>("simulation.json", ct);
        var bindings = await _currentFiles.LoadAsync<SignalBindingsConfig>("signal-bindings.json", ct);

        device.Validate();
        points.Validate();
        simulation.Validate();

        var seed = JsonSerializer.Serialize(new { device, points, simulation, bindings });
        var revision = "bootstrap-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed))).ToLowerInvariant();
        var snapshot = new DeviceConfigurationSnapshot
        {
            Revision = revision,
            Device = device,
            Points = points,
            Simulation = simulation,
            SignalBindings = bindings
        };

        MultiDeviceConfigurationValidator.EnsureApplicationValid(snapshot, _drivers, _requiredSignals);
        await _versionedStore.StageAsync(snapshot, ct);
        await _versionedStore.CommitActiveAsync(snapshot.Revision, ct);
        return new DeviceConfigurationBootstrapResult(snapshot, true, "已完成当前版本设备配置初始化")
        {
            SourceRevision = snapshot.Revision
        };
    }

    private bool HasConfigurationHistory()
    {
        var deviceConfigRoot = Path.Combine(_configRoot, "device-config");
        if (File.Exists(Path.Combine(deviceConfigRoot, "active.json"))
            || File.Exists(Path.Combine(deviceConfigRoot, "active.previous.json")))
            return true;

        var revisionsRoot = Path.Combine(deviceConfigRoot, "revisions");
        return Directory.Exists(revisionsRoot) && Directory.EnumerateFileSystemEntries(revisionsRoot).Any();
    }
}

public sealed record DeviceConfigurationBootstrapResult(
    DeviceConfigurationSnapshot Snapshot,
    bool WasMigrated,
    string? Message)
{
    public bool UsedPrevious { get; init; }
    public string? SourceRevision { get; init; }
    public string? RecoveryMessage { get; init; }
}
