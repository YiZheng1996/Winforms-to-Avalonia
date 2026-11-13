namespace XXX.TestBench.Core.Common;

/// <summary>
/// 必需业务信号未绑定、设备不可用或样本不满足新鲜度要求。
/// </summary>
public sealed class SignalDependencyException : DomainException
{
    public SignalDependencyException(string message) : base(message) { }
}
