using XXX.TestBench.Core.Common;

namespace XXX.TestBench.Infrastructure.Time;

/// <summary>
/// 系统时钟实现，直接取当前时间。
/// </summary>
public sealed class SystemClock : IClock
{
    /// <summary>
    /// 当前协调世界时。
    /// </summary>
    public DateTime UtcNow => DateTime.UtcNow;
}
