namespace XXX.TestBench.Core.Domain.Devices;

/// <summary>
/// 设备运行时状态快照。
/// </summary>
public sealed record DeviceRuntimeInfo(
    string Name,
    string Protocol,
    string Address,
    bool IsSimulation,
    DeviceHealth Health,
    bool IsConnected,
    string? LastError,
    string DeviceId = "",
    string ChannelId = "",
    string Revision = "",
    long ConnectionGeneration = 0,
    DeviceConnectionState ConnectionState = DeviceConnectionState.Unknown);
