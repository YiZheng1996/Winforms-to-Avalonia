namespace XXX.TestBench.Core.Domain.Reports;

/// <summary>
/// 报表用途；过程记录不得被解释为产品合格判定。
/// </summary>
public enum ReportPurpose
{
    ProductEvaluation = 0,
    ProcessOnly = 1
}
