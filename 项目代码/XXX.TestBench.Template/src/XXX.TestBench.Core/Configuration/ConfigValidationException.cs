using XXX.TestBench.Core.Common;

namespace XXX.TestBench.Core.Configuration;

/// <summary>配置缺失、schemaVersion 不支持或字段非法时抛出，启动进入 Faulted 状态。</summary>
public sealed class ConfigValidationException : DomainException
{
    public ConfigValidationException(string message) : base(message) { }
}
