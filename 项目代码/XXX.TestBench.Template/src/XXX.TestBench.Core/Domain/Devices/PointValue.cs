namespace XXX.TestBench.Core.Domain.Devices;

/// <summary>
/// 点位实时值。
/// </summary>
public sealed record PointValue(
    string PointCode,
    string Address,
    PointQuality Quality,
    object? Value,
    DateTime TimestampUtc,
    string PointId = "",
    string DeviceId = "",
    long ConnectionGeneration = 0,
    string Revision = "",
    /// <summary>
    /// 协议解码后的原始值。Value 始终表示工程值；没有量程时两者通常相同。
    /// </summary>
    object? RawValue = null);
