using XXX.TestBench.Core.Domain.Identity;

namespace XXX.TestBench.Core.Ports;

/// <summary>
/// 用户与角色数据仓库接口。
/// </summary>
public interface IUserRepository
{
    /// <summary>
    /// 按登录名读取用户。
    /// </summary>
    Task<User?> GetByLoginNameAsync(string loginName, CancellationToken ct = default);
    /// <summary>
    /// 按编号读取用户。
    /// </summary>
    Task<User?> GetByIdAsync(int id, CancellationToken ct = default);
    /// <summary>
    /// 按编号读取角色及其权限。
    /// </summary>
    Task<Role?> GetRoleAsync(int roleId, CancellationToken ct = default);
    /// <summary>
    /// 读取全部用户。
    /// </summary>
    Task<IReadOnlyList<User>> ListUsersAsync(CancellationToken ct = default);
    /// <summary>
    /// 读取全部角色。
    /// </summary>
    Task<IReadOnlyList<Role>> ListRolesAsync(CancellationToken ct = default);
    /// <summary>
    /// 新增用户。
    /// </summary>
    Task AddAsync(User user, CancellationToken ct = default);
    /// <summary>
    /// 更新用户信息。
    /// </summary>
    Task UpdateAsync(User user, CancellationToken ct = default);

    /// <summary>
    /// 检查角色名称是否已被其他角色使用。
    /// </summary>
    Task<bool> RoleNameExistsAsync(string name, int? excludingRoleId = null, CancellationToken ct = default);

    /// <summary>
    /// 新增角色并回填编号。
    /// </summary>
    Task AddRoleAsync(Role role, CancellationToken ct = default);

    /// <summary>
    /// 修改角色显示名称。
    /// </summary>
    Task RenameRoleAsync(int roleId, string name, CancellationToken ct = default);

    /// <summary>
    /// 删除角色。调用方需先完成内置角色和引用关系校验。
    /// </summary>
    Task DeleteRoleAsync(int roleId, CancellationToken ct = default);

    /// <summary>
    /// 删除角色当前全部权限。
    /// </summary>
    Task ClearRolePermissionsAsync(int roleId, CancellationToken ct = default);

    /// <summary>
    /// 添加角色权限。
    /// </summary>
    Task AddRolePermissionAsync(int roleId, PermissionCode permission, CancellationToken ct = default);

    /// <summary>
    /// 返回关联到指定角色的用户数。
    /// </summary>
    Task<int> CountUsersByRoleAsync(int roleId, CancellationToken ct = default);
}
