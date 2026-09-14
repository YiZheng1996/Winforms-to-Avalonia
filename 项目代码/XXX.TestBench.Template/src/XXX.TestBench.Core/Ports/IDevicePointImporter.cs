using XXX.TestBench.Core.Configuration;

namespace XXX.TestBench.Core.Ports;

/// <summary>
/// 外部点位文件导入边界。实现位于 Infrastructure，Core 不依赖 Excel/CSV 解析库。
/// </summary>
public interface IDevicePointImporter
{
    /// <summary>
    /// 读取点位文件并转换为可用的配置点位。
    /// </summary>
    Task<DevicePointImportResult> ImportAsync(string filePath, CancellationToken ct = default);
}

/// <summary>
/// 导入过程中某一行发现的问题。
/// </summary>
public sealed record DevicePointImportIssue(int RowNumber, string Message);

/// <summary>
/// 导入文件中成功解析的点位及其原始行号。行号只用于预览和诊断，不参与点位身份匹配。
/// </summary>
public sealed record DevicePointImportRow(int RowNumber, PointsConfig.PointEntry Entry);

/// <summary>
/// 点位导入结果，包含可用的点位与问题清单。
/// </summary>
public sealed record DevicePointImportResult(
    IReadOnlyList<PointsConfig.PointEntry> Points,
    IReadOnlyList<DevicePointImportIssue> Issues,
    IReadOnlyList<DevicePointImportRow>? ParsedRows = null)
{
    public bool IsValid => Points.Count > 0 && Issues.Count == 0;

    /// <summary>
    /// 返回带原始行号的解析结果；兼容旧调用方构造的两参数结果。
    /// </summary>
    public IReadOnlyList<DevicePointImportRow> RowsWithNumbers
        => ParsedRows
            ?? Points.Select((entry, index) => new DevicePointImportRow(index + 2, entry)).ToList();
}
