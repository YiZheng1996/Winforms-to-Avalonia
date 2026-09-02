using XXX.TestBench.Core.Domain.Devices;

namespace XXX.TestBench.Core.Ports;

/// <summary>
/// 设备运行时边界。界面不得直接调用协议库写方法。
/// </summary>
public interface IDeviceRuntime : IAsyncDisposable
{
    string Name { get; }
    DeviceMode Mode { get; }
    bool IsSimulation { get; }
    DeviceRuntimeInfo Status { get; }
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
    Task<PointValue> ReadAsync(DevicePoint point, CancellationToken ct = default);
    Task<PointValue> WriteAsync(DevicePoint point, object? value, CancellationToken ct = default);
    Task<IReadOnlyList<DevicePoint>> ListPointsAsync(CancellationToken ct = default);
}
