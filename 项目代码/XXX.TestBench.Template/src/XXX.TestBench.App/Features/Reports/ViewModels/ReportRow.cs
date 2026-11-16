using XXX.TestBench.Core.Domain.Reports;

namespace XXX.TestBench.App.ViewModels;

/// <summary>
/// 报表列表中的一行。
/// </summary>
public sealed class ReportRow
{
    /// <summary>当前持久状态，用于失败重试入口。</summary>
    public ReportStatus Status { get; init; }
    /// <summary>同次试验的报表修订序号。</summary>
    public int? RevisionNo { get; init; }
    /// <summary>生成失败的持久原因。</summary>
    public string? Error { get; init; }
    /// <summary>
    /// 报表编号。
    /// </summary>
    public required int Id { get; init; }
    /// <summary>
    /// 报表状态文字。
    /// </summary>
    public required string StatusText { get; init; }
    /// <summary>
    /// 输出文件路径。
    /// </summary>
    public required string OutputPath { get; init; }
    /// <summary>
    /// 创建时间文字。
    /// </summary>
    public required string CreatedAt { get; init; }
}
