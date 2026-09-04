namespace XXX.TestBench.Core.Domain.Records;

/// <summary>
/// 本次产品标识字段：产品编号、批次号、工位号与备注，第一版均为可选，但至少填写一项。
/// </summary>
public sealed record ProductIdentity(
    string? ProductNumber,
    string? BatchNumber,
    string? StationNumber,
    string? Remark)
{
    public bool HasAnyValue =>
        !string.IsNullOrWhiteSpace(ProductNumber) ||
        !string.IsNullOrWhiteSpace(BatchNumber) ||
        !string.IsNullOrWhiteSpace(StationNumber) ||
        !string.IsNullOrWhiteSpace(Remark);
}
