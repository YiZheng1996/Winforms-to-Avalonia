using XXX.TestBench.Core.Domain.Devices;

namespace XXX.TestBench.Core.Ports;

/// <summary>
/// 设备运行时边界。界面不得直接调用协议库写方法。
/// </summary>
public interface IDeviceRuntime : IAsyncDisposable
{
    /// <summary>
    /// 设备名称。
    /// </summary>
    string Name { get; }
    /// <summary>
    /// 运行模式。
    /// </summary>
    DeviceMode Mode { get; }
    /// <summary>
    /// 是否模拟运行。
    /// </summary>
    bool IsSimulation { get; }
    /// <summary>
    /// 当前运行状态。
    /// </summary>
    DeviceRuntimeInfo Status { get; }
    /// <summary>
    /// 启动设备通信。
    /// </summary>
    Task StartAsync(CancellationToken ct = default);
    /// <summary>
    /// 停止设备通信。
    /// </summary>
    Task StopAsync(CancellationToken ct = default);
    /// <summary>
    /// 读取一个点位值。
    /// </summary>
    Task<PointValue> ReadAsync(DevicePoint point, CancellationToken ct = default);
    /// <summary>
    /// 写入一个点位值。
    /// </summary>
    Task<PointValue> WriteAsync(DevicePoint point, object? value, CancellationToken ct = default);
    /// <summary>
    /// 列出设备支持的全部点位。
    /// </summary>
    Task<IReadOnlyList<DevicePoint>> ListPointsAsync(CancellationToken ct = default);
}
