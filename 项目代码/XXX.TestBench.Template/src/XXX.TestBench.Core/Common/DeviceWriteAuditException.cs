namespace XXX.TestBench.Core.Common;

/// <summary>
/// 写入已经发出，但审计记录未能可靠落库；禁止自动重发。
/// </summary>
public sealed class DeviceWriteAuditException : DomainException
{
    public DeviceWriteAuditException(string message) : base(message) { }
}
