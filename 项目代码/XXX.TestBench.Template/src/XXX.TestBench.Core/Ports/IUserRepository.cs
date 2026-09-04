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
}
