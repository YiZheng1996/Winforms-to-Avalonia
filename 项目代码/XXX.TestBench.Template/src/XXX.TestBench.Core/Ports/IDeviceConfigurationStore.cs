using XXX.TestBench.Core.Configuration;

namespace XXX.TestBench.Core.Ports;

/// <summary>
/// 完整设备配置快照的版本化存储边界。
/// </summary>
public interface IDeviceConfigurationStore
{
    Task<DeviceConfigurationSnapshot> LoadActiveAsync(CancellationToken ct = default);
    Task<DeviceConfigurationSnapshot> LoadRevisionAsync(string revision, CancellationToken ct = default);
    Task StageAsync(DeviceConfigurationSnapshot snapshot, CancellationToken ct = default);
    Task CommitActiveAsync(string revision, CancellationToken ct = default);
}
