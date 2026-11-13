namespace XXX.TestBench.Core.Configuration;

/// <summary>
/// 设备采集策略。FixedInterval 由设备会话后台轮询，OnDemand 只在明确请求时访问设备。
/// </summary>
public enum DeviceScanMode
{
    FixedInterval = 1,
    OnDemand = 2
}
