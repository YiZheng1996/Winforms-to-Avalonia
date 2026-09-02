namespace XXX.TestBench.Core.Domain.Tasks;

/// <summary>
/// 本次产品标识字段。字段集合待阶段 1 确认；第一版均为可选。
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
