namespace XXX.TestBench.Core.Domain.Devices;

/// <summary>
/// 点位数据质量。
/// </summary>
public enum PointQuality
{
    /// <summary>
    /// 未知。
    /// </summary>
    Unknown = 0,
    /// <summary>
    /// 良好。
    /// </summary>
    Good = 1,
    /// <summary>
    /// 数据陈旧。
    /// </summary>
    Stale = 2,
    /// <summary>
    /// 数据无效。
    /// </summary>
    Bad = 3
}
