using XXX.TestBench.Core.Common;

namespace XXX.TestBench.Core.Configuration;

/// <summary>
/// 连续失败后的设备级降级策略。降级只阻断新鲜 I/O，不删除最后一份缓存。
/// </summary>
public sealed class DeviceDemotionOptions
{
    public bool Enabled { get; set; } = true;
    public int FailureThreshold { get; set; } = 3;
    public int DemotionPeriodMs { get; set; } = 10000;

    /// <summary>
    /// 校验降级阈值和持续时间，并返回所有字段错误。
    /// </summary>
    public IReadOnlyList<ConfigurationIssue> Validate(string path)
    {
        var issues = new List<ConfigurationIssue>();
        if (FailureThreshold <= 0) issues.Add(new(path + ".failureThreshold", "必须大于 0"));
        if (DemotionPeriodMs <= 0) issues.Add(new(path + ".demotionPeriodMs", "必须大于 0"));
        return issues;
    }
}
