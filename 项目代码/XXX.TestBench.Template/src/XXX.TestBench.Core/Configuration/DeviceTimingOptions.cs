using XXX.TestBench.Core.Common;

namespace XXX.TestBench.Core.Configuration;

/// <summary>
/// 单台设备的请求和重试时序。
/// </summary>
public sealed class DeviceTimingOptions
{
    public int ConnectTimeoutMs { get; set; } = 3000;
    public int RequestTimeoutMs { get; set; } = 1000;
    public int RetryCount { get; set; } = 2;
    public int InterRequestDelayMs { get; set; }

    /// <summary>
    /// 校验连接、请求超时和重试间隔，并返回所有字段错误。
    /// </summary>
    public IReadOnlyList<ConfigurationIssue> Validate(string path)
    {
        var issues = new List<ConfigurationIssue>();
        if (ConnectTimeoutMs <= 0) issues.Add(new(path + ".connectTimeoutMs", "必须大于 0"));
        if (RequestTimeoutMs <= 0) issues.Add(new(path + ".requestTimeoutMs", "必须大于 0"));
        if (RetryCount < 0) issues.Add(new(path + ".retryCount", "不能小于 0"));
        if (InterRequestDelayMs < 0) issues.Add(new(path + ".interRequestDelayMs", "不能小于 0"));
        return issues;
    }
}
