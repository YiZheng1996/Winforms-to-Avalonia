namespace XXX.TestBench.Core.Domain.Reports;

/// <summary>
/// PDF 附件状态，独立于 XLSX 产品结论。
/// 数值是持久化合同，已经发布后不得重排。
/// </summary>
public enum PdfState
{
    NotRequested = 0,
    Pending = 1,
    Generating = 2,
    Completed = 3,
    Failed = 4
}
