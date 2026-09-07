namespace XXX.TestBench.Core.Domain.Devices;

/// <summary>
/// 单设备连接状态；DeviceHealth 继续作为旧调用方的聚合兼容字段。
/// </summary>
public enum DeviceConnectionState
{
    Unknown = 0,
    Disabled = 1,
    Connecting = 2,
    Online = 3,
    Degraded = 4,
    Offline = 5,
    Faulted = 6
}
