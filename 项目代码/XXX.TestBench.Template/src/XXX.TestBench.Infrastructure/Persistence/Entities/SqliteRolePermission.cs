using FreeSql.DataAnnotations;

namespace XXX.TestBench.Infrastructure.Persistence.Entities;

/// <summary>
/// 角色权限关联表的 SQLite 映射实体。
/// </summary>
[Table(Name = "role_permissions")]
internal sealed class SqliteRolePermission
{
    /// <summary>
    /// 角色编号，与权限编码组成联合主键的一部分。
    /// </summary>
    [Column(Name = "role_id", IsPrimary = true)]
    public int RoleId { get; set; }

    /// <summary>
    /// 权限编码（枚举的整数值），与角色编号组成联合主键的一部分。
    /// </summary>
    [Column(Name = "permission_code", IsPrimary = true)]
    public int PermissionCode { get; set; }
}
