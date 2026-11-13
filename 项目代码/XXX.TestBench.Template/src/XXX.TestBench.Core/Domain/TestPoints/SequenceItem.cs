namespace XXX.TestBench.Core.Domain.TestPoints;

/// <summary>冻结的项点身份、顺序及输出合同版本，不随主数据修改而改变。</summary>
public sealed record SequenceItem(
    int PointId,
    string Name,
    string ExecutorCode,
    string ResultKind,
    int SortOrder,
    long PointRowVersion = 0,
    int OutputContractVersion = 1);
