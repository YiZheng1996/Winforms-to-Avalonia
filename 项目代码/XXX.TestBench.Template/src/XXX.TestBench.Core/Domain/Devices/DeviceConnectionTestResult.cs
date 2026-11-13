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
}
