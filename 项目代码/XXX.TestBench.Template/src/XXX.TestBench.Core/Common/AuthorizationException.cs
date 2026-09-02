namespace XXX.TestBench.Core.Common;

/// <summary>
/// 会话无权限或已失效时抛出。UI 隐藏/禁用不是安全边界。
/// </summary>
public sealed class AuthorizationException : DomainException
{
    public AuthorizationException(string message) : base(message) { }
}
