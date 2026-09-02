namespace XXX.TestBench.Core.Domain.Devices;

/// <summary>
/// 点位实时值。
/// </summary>
public sealed record PointValue(
    string PointCode,
    string Address,
    PointQuality Quality,
    object? Value,
    DateTime TimestampUtc);
