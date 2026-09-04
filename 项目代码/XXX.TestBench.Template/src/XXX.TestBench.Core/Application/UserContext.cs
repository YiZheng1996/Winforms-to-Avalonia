using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Identity;

namespace XXX.TestBench.Core.Application;

/// <summary>
/// 已认证操作者上下文，携带角色权限。
/// </summary>
public sealed class UserContext
{
    /// <summary>
    /// 用户编号。
    /// </summary>
    public int UserId { get; init; }
    /// <summary>
    /// 登录名。
    /// </summary>
    public required string LoginName { get; init; }
    /// <summary>
    /// 显示姓名。
    /// </summary>
    public required string DisplayName { get; init; }
    /// <summary>
    /// 当前角色及其权限。
    /// </summary>
    public required Role Role { get; init; }

    /// <summary>
    /// 判断是否拥有指定权限。
    /// </summary>
    public bool HasPermission(PermissionCode permission) => Role.HasPermission(permission);

    /// <summary>
    /// 校验权限，缺少时抛出异常。
    /// </summary>
    public void EnsurePermission(PermissionCode permission)
    {
        if (!HasPermission(permission))
            throw new AuthorizationException($"用户 {LoginName} 缺少权限 {permission}");
    }
}
