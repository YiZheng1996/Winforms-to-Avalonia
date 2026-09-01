using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Identity;

namespace XXX.TestBench.Core.Application;

/// <summary>已认证操作者上下文，携带角色权限。</summary>
public sealed class UserContext
{
    public int UserId { get; init; }
    public required string LoginName { get; init; }
    public required string DisplayName { get; init; }
    public required Role Role { get; init; }

    public bool HasPermission(PermissionCode permission) => Role.HasPermission(permission);

    public void EnsurePermission(PermissionCode permission)
    {
        if (!HasPermission(permission))
            throw new AuthorizationException($"用户 {LoginName} 缺少权限 {permission}");
    }
}
