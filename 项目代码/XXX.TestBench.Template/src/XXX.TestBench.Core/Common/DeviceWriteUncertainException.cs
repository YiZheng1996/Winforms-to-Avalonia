namespace XXX.TestBench.Core.Common;

/// <summary>
/// 写入请求可能已经发送或设备状态不明；调用方不得自动重试。
/// </summary>
public sealed class DeviceWriteUncertainException : DomainException
{
    public DeviceWriteUncertainException(string message) : base(message) { }
}
