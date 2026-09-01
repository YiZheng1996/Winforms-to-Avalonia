namespace XXX.TestBench.Core.Common;

/// <summary>时间来源，便于测试固定时间。</summary>
public interface IClock
{
    DateTime UtcNow { get; }
}
