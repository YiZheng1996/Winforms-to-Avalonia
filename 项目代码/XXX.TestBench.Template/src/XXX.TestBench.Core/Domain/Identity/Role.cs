namespace XXX.TestBench.Core.Domain.Identity;

/// <summary>
/// 角色及其权限定义。
/// </summary>
public sealed class Role
{
    /// <summary>
    /// 角色编号。
    /// </summary>
    public int Id { get; set; }
    /// <summary>
    /// 内置角色标识；自定义角色为空。该值用于保护内置角色，不作为显示名称使用。
    /// </summary>
    public string? SystemKey { get; set; }
    /// <summary>
    /// 角色名称。
    /// </summary>
    public required string Name { get; set; }
    /// <summary>
    /// 该角色拥有的权限集合。
    /// </summary>
    public HashSet<PermissionCode> Permissions { get; } = new();

    /// <summary>
    /// 判断角色是否拥有指定权限。
    /// </summary>
    public bool HasPermission(PermissionCode permission) => Permissions.Contains(permission);
}
