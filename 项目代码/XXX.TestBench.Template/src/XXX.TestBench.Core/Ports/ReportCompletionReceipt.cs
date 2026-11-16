namespace XXX.TestBench.Core.Ports;

/// <summary>与 XLSX 一起不可变归档的计算回执，支持发布成功而数据库提交中断后的补关联。</summary>
public sealed record ReportCompletionReceipt(
    int SchemaVersion,
    string AttemptToken,
    string DatabaseInstanceId,
    DateTime CompletedAtUtc,
    ReportCalculationResult Calculation);
