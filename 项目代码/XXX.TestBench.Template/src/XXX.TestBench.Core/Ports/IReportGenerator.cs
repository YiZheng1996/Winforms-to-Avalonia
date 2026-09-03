namespace XXX.TestBench.Core.Ports;

public sealed record ReportItemRow(int SortOrder, string ItemName, string SummaryValue, string ResultText, string State);

/// <summary>
/// 报表数据快照（由已保存的 TestRecord/TestItemResult 生成，Excel 不作为输入源）。
/// </summary>
public sealed record ReportData(
    int RecordId,
    string TaskNumber,
    string ProductNumber,
    string ProductModelCode,
    string FlowText,
    string DeviceMode,
    string OperatorName,
    DateTime StartedAtUtc,
    string Conclusion,
    IReadOnlyList<ReportItemRow> Items);

public interface IReportGenerator
{
    /// <summary>
    /// 从报表数据生成 xlsx；templatePath 存在时加载模板，否则使用标准版式。返回输出文件绝对路径。
    /// </summary>
    Task<string> GenerateAsync(ReportData data, string templatePath, string outputDirectory, CancellationToken ct = default);
}
