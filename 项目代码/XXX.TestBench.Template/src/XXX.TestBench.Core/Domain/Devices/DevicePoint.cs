namespace XXX.TestBench.Core.Domain.Devices;

/// <summary>
/// 点位定义（来自 points.json）。
/// </summary>
public sealed record DevicePoint(
    string Code,
    string Protocol,
    string Address,
    string DataType,
    string Unit,
    bool IsWritable,
    WriteRiskLevel RiskLevel,
    decimal? RawMin,
    decimal? RawMax,
    decimal? EngMin,
    decimal? EngMax);
