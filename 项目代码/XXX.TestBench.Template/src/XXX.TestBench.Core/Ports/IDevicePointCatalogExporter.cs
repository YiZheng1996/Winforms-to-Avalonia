using XXX.TestBench.Core.Configuration;

namespace XXX.TestBench.Core.Ports;

/// <summary>
/// 当前范围点位目录导出请求。导出只承载批量维护字段，不是完整配置备份。
/// </summary>
public sealed record DevicePointCatalogExportRequest(
    string ScopeDisplayName,
    IReadOnlyList<PointsConfig.PointEntry> Points,
    IReadOnlyList<PointsConfig.PointGroupEntry> Groups,
    IReadOnlyList<DeviceConfig.DeviceEntry> Devices);

/// <summary>
/// 设备点位当前范围导出边界。实现位于 Infrastructure，Core 不依赖 Excel 库。
/// </summary>
public interface IDevicePointCatalogExporter
{
    Task ExportAsync(
        string filePath,
        DevicePointCatalogExportRequest request,
        CancellationToken ct = default);
}
