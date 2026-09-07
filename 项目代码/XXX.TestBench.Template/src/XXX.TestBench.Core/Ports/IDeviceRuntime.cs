using XXX.TestBench.Core.Configuration;
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

    /// <summary>
    /// 当前完整配置快照的生效版本。旧运行时返回空字符串，表示尚未接入版本门面。
    /// </summary>
    string ActiveRevision => string.Empty;

    /// <summary>
    /// 当前运行时所使用的项目级信号绑定。旧兼容运行时没有绑定快照。
    /// </summary>
    SignalBindingsConfig SignalBindings => new();

    /// <summary>
    /// 强制从目标设备读取一次新鲜样本，不能用旧缓存冒充回读。
    /// </summary>
    async Task<PointValue> ReadFreshAsync(string pointId, CancellationToken ct = default)
    {
        var point = (await ListPointsAsync(ct)).FirstOrDefault(item =>
            string.Equals(item.PointId, pointId, StringComparison.OrdinalIgnoreCase));
        if (point is null)
            throw new Common.DomainException($"点位 {pointId} 不在当前运行时");
        return await ReadAsync(point, ct);
    }

    /// <summary>
    /// 返回单台设备状态；单设备兼容运行时使用自身状态。
    /// </summary>
    DeviceRuntimeInfo GetDeviceStatus(string deviceId) => Status with { DeviceId = deviceId };

    /// <summary>
    /// 返回当前全部设备状态。
    /// </summary>
    IReadOnlyList<DeviceRuntimeInfo> ListDeviceStatuses() => new[] { Status };

    /// <summary>
    /// 按稳定 PointId 获取当前点位定义。
    /// </summary>
    DevicePoint? GetPoint(string pointId)
        => ListPointsAsync().GetAwaiter().GetResult().FirstOrDefault(point =>
            string.Equals(point.PointId, pointId, StringComparison.OrdinalIgnoreCase));
}
