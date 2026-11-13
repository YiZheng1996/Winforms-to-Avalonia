using XXX.TestBench.Core.Common;

namespace XXX.TestBench.Core.Configuration;

/// <summary>
/// 设备采集策略。FixedInterval 由设备会话后台轮询，OnDemand 只在明确请求时访问设备。
/// </summary>
public enum DeviceScanMode
{
    FixedInterval = 1,
    OnDemand = 2
}

/// <summary>
/// 单台设备的请求和重试时序。
/// </summary>
public sealed class DeviceTimingOptions
{
    public int ConnectTimeoutMs { get; set; } = 3000;
    public int RequestTimeoutMs { get; set; } = 1000;
    public int RetryCount { get; set; } = 2;
    public int InterRequestDelayMs { get; set; }

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

/// <summary>
/// 连续失败后的设备级降级策略。降级只阻断新鲜 I/O，不删除最后一份缓存。
/// </summary>
public sealed class DeviceDemotionOptions
{
    public bool Enabled { get; set; } = true;
    public int FailureThreshold { get; set; } = 3;
    public int DemotionPeriodMs { get; set; } = 10000;
    /// <summary>
    /// 旧配置兼容字段。当前运行时固定在降级期间禁止写入，不再由客户配置。
    /// </summary>
    public bool DiscardWritesWhileDemoted { get; set; } = true;

    public IReadOnlyList<ConfigurationIssue> Validate(string path)
    {
        var issues = new List<ConfigurationIssue>();
        if (FailureThreshold <= 0) issues.Add(new(path + ".failureThreshold", "必须大于 0"));
        if (DemotionPeriodMs <= 0) issues.Add(new(path + ".demotionPeriodMs", "必须大于 0"));
        return issues;
    }
}

/// <summary>
/// Siemens S7 连接端点。硬件端点归属于设备，不再从共享通道借用目标地址。
/// </summary>
public sealed class SiemensS7ConnectionOptions
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 102;
    public int Rack { get; set; }
    public int Slot { get; set; }

    public IReadOnlyList<ConfigurationIssue> Validate(string path)
    {
        var issues = new List<ConfigurationIssue>();
        if (string.IsNullOrWhiteSpace(Host)) issues.Add(new(path + ".host", "不能为空"));
        if (Port is < 1 or > 65535) issues.Add(new(path + ".port", "必须在 1-65535 范围内"));
        if (Rack is < 0 or > short.MaxValue) issues.Add(new(path + ".rack", $"必须在 0-{short.MaxValue} 范围内"));
        if (Slot is < 0 or > short.MaxValue) issues.Add(new(path + ".slot", $"必须在 0-{short.MaxValue} 范围内"));
        return issues;
    }
}

/// <summary>
/// Modbus TCP 的设备专属端点。
///
/// TCP 通道只负责调度和生命周期，不再持有远端目标地址；因此 Modbus TCP
/// 和 S7 一样，把 Host/Port 放在设备项中，避免同一 TCP 通道上的设备互相串用地址。
/// </summary>
public sealed class ModbusTcpConnectionOptions
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 502;

    public IReadOnlyList<ConfigurationIssue> Validate(string path)
    {
        var issues = new List<ConfigurationIssue>();
        if (string.IsNullOrWhiteSpace(Host))
            issues.Add(new(path + ".host", "不能为空"));
        if (Port is < 1 or > 65535)
            issues.Add(new(path + ".port", "必须在 1-65535 范围内"));
        return issues;
    }
}
