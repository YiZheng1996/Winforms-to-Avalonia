using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Infrastructure.Persistence.Repositories;

/// <summary>
/// 用户与角色数据的数据库实现。
/// </summary>
public sealed class UserRepository : SqliteRepositoryBase, IUserRepository
{
    /// <summary>
    /// 创建用户仓库。
    /// </summary>
    public UserRepository(ISqliteConnectionFactory factory) : base(factory) { }

    /// <summary>
    /// 按登录名读取用户。
    /// </summary>
    public async Task<User?> GetByLoginNameAsync(string loginName, CancellationToken ct = default)
    {
        var row = await QuerySingleAsync<UserRow>(UserSelect + " WHERE login_name=@loginName", new { loginName }, ct);
        return row is null ? null : Map(row);
    }

    /// <summary>
    /// 按编号读取用户。
    /// </summary>
    public async Task<User?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var row = await QuerySingleAsync<UserRow>(UserSelect + " WHERE id=@id", new { id }, ct);
        return row is null ? null : Map(row);
    }

    /// <summary>
    /// 读取角色并附带其全部权限。
    /// </summary>
    public async Task<Role?> GetRoleAsync(int roleId, CancellationToken ct = default)
    {
        var role = await QuerySingleAsync<RoleRow>(
            "SELECT id AS Id, name AS Name FROM roles WHERE id=@roleId", new { roleId }, ct);
        if (role is null) return null;

        var permissions = await QueryAsync<PermissionRow>(
            "SELECT permission_code AS PermissionCode FROM role_permissions WHERE role_id=@roleId",
            new { roleId }, ct);
        var result = new Role { Id = role.Id, Name = role.Name };
        foreach (var permission in permissions)
            result.Permissions.Add((PermissionCode)permission.PermissionCode);
        return result;
    }

    /// <summary>
    /// 新增用户并回填新编号。
    /// </summary>
    public async Task AddAsync(User user, CancellationToken ct = default)
    {
        var id = await ExecuteInsertAndGetIdAsync("""
            INSERT INTO users (login_name, display_name, password_hash, must_change_password, is_enabled, failed_login_count, locked_until_utc, role_id, created_at_utc)
            VALUES (@loginName, @displayName, @passwordHash, @mustChangePassword, @isEnabled, @failedLoginCount, @lockedUntilUtc, @roleId, @createdAtUtc)
            """, new
        {
            loginName = user.LoginName,
            displayName = user.DisplayName,
            passwordHash = user.PasswordHash,
            mustChangePassword = user.MustChangePassword ? 1 : 0,
            isEnabled = user.IsEnabled ? 1 : 0,
            failedLoginCount = user.FailedLoginCount,
            lockedUntilUtc = DbValue(user.LockedUntilUtc?.ToString("O")),
            roleId = user.RoleId,
            createdAtUtc = user.CreatedAtUtc.ToString("O")
        }, ct);
        user.Id = (int)id;
    }

    /// <summary>
    /// 更新用户基本信息。
    /// </summary>
    public Task UpdateAsync(User user, CancellationToken ct = default) => ExecuteAsync("""
        UPDATE users SET display_name=@displayName, password_hash=@passwordHash, must_change_password=@mustChangePassword, is_enabled=@isEnabled,
        failed_login_count=@failedLoginCount, locked_until_utc=@lockedUntilUtc WHERE id=@id
        """, new
    {
        displayName = user.DisplayName,
        passwordHash = user.PasswordHash,
        mustChangePassword = user.MustChangePassword ? 1 : 0,
        isEnabled = user.IsEnabled ? 1 : 0,
        failedLoginCount = user.FailedLoginCount,
        lockedUntilUtc = DbValue(user.LockedUntilUtc?.ToString("O")),
        id = user.Id
    }, ct);

    /// <summary>
    /// 读取全部用户。
    /// </summary>
    public async Task<IReadOnlyList<User>> ListUsersAsync(CancellationToken ct = default)
    {
        var rows = await QueryAsync<UserRow>(UserSelect + " ORDER BY created_at_utc DESC, id DESC", null, ct);
        return rows.Select(Map).ToList();
    }

    /// <summary>
    /// 读取全部角色。
    /// </summary>
    public async Task<IReadOnlyList<Role>> ListRolesAsync(CancellationToken ct = default)
    {
        var rows = await QueryAsync<RoleRow>("SELECT id AS Id, name AS Name FROM roles ORDER BY id", null, ct);
        return rows.Select(row => new Role { Id = row.Id, Name = row.Name }).ToList();
    }

    /// <summary>
    /// 把查询结果转换为用户对象。
    /// </summary>
    private static User Map(UserRow row) => new()
    {
        Id = row.Id,
        LoginName = row.LoginName,
        DisplayName = row.DisplayName,
        PasswordHash = row.PasswordHash,
        MustChangePassword = row.MustChangePassword != 0,
        IsEnabled = row.IsEnabled != 0,
        FailedLoginCount = row.FailedLoginCount,
        LockedUntilUtc = ParseNullableUtc(row.LockedUntilUtc),
        RoleId = row.RoleId,
        CreatedAtUtc = ParseUtc(row.CreatedAtUtc)
    };

    /// <summary>
    /// 用户表常用查询字段。
    /// </summary>
    private const string UserSelect = "SELECT id AS Id, login_name AS LoginName, display_name AS DisplayName, password_hash AS PasswordHash, must_change_password AS MustChangePassword, is_enabled AS IsEnabled, failed_login_count AS FailedLoginCount, locked_until_utc AS LockedUntilUtc, role_id AS RoleId, created_at_utc AS CreatedAtUtc FROM users";

    private sealed class UserRow
    {
        public int Id { get; set; }
        public string LoginName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public int MustChangePassword { get; set; }
        public int IsEnabled { get; set; }
        public int FailedLoginCount { get; set; }
        public string? LockedUntilUtc { get; set; }
        public int RoleId { get; set; }
        public string CreatedAtUtc { get; set; } = string.Empty;
    }

    private sealed class RoleRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class PermissionRow
    {
        public int PermissionCode { get; set; }
    }
}
