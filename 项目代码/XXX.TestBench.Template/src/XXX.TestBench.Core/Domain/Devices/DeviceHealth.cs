namespace XXX.TestBench.Core.Domain.Devices;

/// <summary>
/// 设备整体健康状态。
/// </summary>
public enum DeviceHealth
{
    /// <summary>
    /// 未知，尚未检测。
    /// </summary>
    Unknown = 0,
    /// <summary>
    /// 正常。
    /// </summary>
    Healthy = 1,
    /// <summary>
    /// 降级运行。
    /// </summary>
    Degraded = 2,
    /// <summary>
    /// 故障。
    /// </summary>
    Faulted = 3,
    /// <summary>
    /// 通信断开。
    /// </summary>
    Disconnected = 4
}
