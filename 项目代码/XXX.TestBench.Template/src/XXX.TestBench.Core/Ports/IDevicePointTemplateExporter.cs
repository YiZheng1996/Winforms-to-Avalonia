namespace XXX.TestBench.Core.Ports;

/// <summary>
/// 设备点位导入模板导出边界。实现位于 Infrastructure，Core 不依赖 Excel 库。
/// </summary>
public interface IDevicePointTemplateExporter
{
    /// <summary>
    /// 生成固定中文列名的设备点位 Excel 模板。
    /// </summary>
    Task ExportAsync(string filePath, CancellationToken ct = default);

    /// <summary>
    /// 按当前设备、通信方式或页面范围生成固定中文列名的设备点位 Excel 模板。
    /// </summary>
    Task ExportAsync(
        string filePath,
        DevicePointTemplateExportRequest request,
        CancellationToken ct = default);
}
