namespace XXX.TestBench.Core.Domain.Devices;

/// <summary>
/// 连接测试所处的层级。TransportOnly 只证明传输层可建立，不能替代点位读验证。
/// </summary>
public enum DeviceConnectionTestLevel
{
    TransportOnly = 1,
    ProtocolRead = 2
}

/// <summary>
/// 候选设备的连接测试结果。
/// </summary>
public sealed record DeviceConnectionTestResult(
    bool Ok,
    bool Executed,
    string Endpoint,
    TimeSpan Elapsed,
    string DriverKey,
    DeviceConnectionTestLevel Level,
    string Summary,
    int? NegotiatedPduSize,
    string? Error)
{
    /// <summary>
    /// 兼容已有 S7 测试调用方的旧构造方式；新代码应填写执行层级和摘要。
    /// </summary>
    public DeviceConnectionTestResult(
        bool ok,
        string endpoint,
        TimeSpan elapsed,
        int? negotiatedPduSize,
        string? error)
        : this(
            ok,
            Executed: true,
            endpoint,
            elapsed,
            DriverKeyCatalog.SiemensS7,
            DeviceConnectionTestLevel.TransportOnly,
            ok ? "TCP/S7 会话建立成功" : error ?? "连接测试失败",
            negotiatedPduSize,
            error)
    {
    }
}
