using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Ports;
using XXX.TestBench.Infrastructure.Persistence;
using XXX.TestBench.Infrastructure.Persistence.Entities;

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
        var row = await RunDbAsync(() => Select<SqliteUser>()
            .Where(x => x.LoginName == loginName)
            .ToOne(), ct);
        return row is null ? null : Map(row);
    }

    /// <summary>
    /// 按编号读取用户。
    /// </summary>
    public async Task<User?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var row = await RunDbAsync(() => Select<SqliteUser>()
            .Where(x => x.Id == id)
            .ToOne(), ct);
        return row is null ? null : Map(row);
    }

    /// <summary>
    /// 读取角色并附带其全部权限。
    /// </summary>
    public async Task<Role?> GetRoleAsync(int roleId, CancellationToken ct = default)
    {
        var role = await RunDbAsync(() => Select<SqliteRole>()
            .Where(x => x.Id == roleId)
            .ToOne(), ct);
        if (role is null) return null;

        var permissions = await RunDbAsync(() => Select<SqliteRolePermission>()
            .Where(x => x.RoleId == roleId)
            .ToList(), ct);
        var result = new Role { Id = role.Id, Name = role.Name, SystemKey = role.SystemKey };
        foreach (var permission in permissions)
            result.Permissions.Add((PermissionCode)permission.PermissionCode);
        return result;
    }

    /// <summary>
    /// 新增用户并回填新编号。
    /// </summary>
    public async Task AddAsync(User user, CancellationToken ct = default)
    {
        var id = await InsertIdentityAsync(new SqliteUser
        {
            LoginName = user.LoginName,
            DisplayName = user.DisplayName,
            PasswordHash = user.PasswordHash,
            MustChangePassword = user.MustChangePassword ? 1 : 0,
            IsEnabled = user.IsEnabled ? 1 : 0,
            FailedLoginCount = user.FailedLoginCount,
            LockedUntilUtc = user.LockedUntilUtc?.ToString("O"),
            RoleId = user.RoleId,
            CreatedAtUtc = user.CreatedAtUtc.ToString("O")
        }, ct);
        user.Id = checked((int)id);
    }

    /// <summary>
    /// 更新用户基本信息、角色和账号状态。
    /// </summary>
    public Task UpdateAsync(User user, CancellationToken ct = default)
        => RunDbAsync(() =>
        {
            Update<SqliteUser>()
                .Where(x => x.Id == user.Id)
                .Set(x => x.DisplayName, user.DisplayName)
                .Set(x => x.PasswordHash, user.PasswordHash)
                .Set(x => x.MustChangePassword, user.MustChangePassword ? 1 : 0)
                .Set(x => x.IsEnabled, user.IsEnabled ? 1 : 0)
                .Set(x => x.FailedLoginCount, user.FailedLoginCount)
                .Set(x => x.LockedUntilUtc, user.LockedUntilUtc?.ToString("O"))
                .Set(x => x.RoleId, user.RoleId)
                .ExecuteAffrows();
        }, ct);

    /// <summary>
    /// 读取全部用户。
    /// </summary>
    public async Task<IReadOnlyList<User>> ListUsersAsync(CancellationToken ct = default)
    {
        var rows = await RunDbAsync(() => Select<SqliteUser>()
            .OrderByDescending(x => x.CreatedAtUtc)
            .OrderByDescending(x => x.Id)
            .ToList(), ct);
        return rows.Select(Map).ToList();
    }

    /// <summary>
    /// 读取全部角色及权限。
    /// </summary>
    public async Task<IReadOnlyList<Role>> ListRolesAsync(CancellationToken ct = default)
    {
        var rows = await RunDbAsync(() => Select<SqliteRole>()
            .OrderBy(x => x.Id)
            .ToList(), ct);
        var result = rows.ToDictionary(row => row.Id, row => new Role
        {
            Id = row.Id,
            Name = row.Name,
            SystemKey = row.SystemKey
        });
        if (result.Count == 0) return Array.Empty<Role>();

        var permissions = await RunDbAsync(() => Select<SqliteRolePermission>().ToList(), ct);
        foreach (var permission in permissions)
        {
            if (result.TryGetValue(permission.RoleId, out var role))
                role.Permissions.Add((PermissionCode)permission.PermissionCode);
        }
        return result.Values.OrderBy(role => role.Id).ToList();
    }

    /// <inheritdoc />
    public Task<bool> RoleNameExistsAsync(string name, int? excludingRoleId = null, CancellationToken ct = default)
    {
        var normalizedName = name.ToLowerInvariant();
        return RunDbAsync(() =>
        {
            var query = Select<SqliteRole>()
                .Where(x => x.Name.ToLower() == normalizedName);
            if (excludingRoleId is int roleId)
                query = query.Where(x => x.Id != roleId);
            return query.Any();
        }, ct);
    }

    /// <inheritdoc />
    public async Task AddRoleAsync(Role role, CancellationToken ct = default)
    {
        var id = await InsertIdentityAsync(new SqliteRole
        {
            Name = role.Name,
            SystemKey = role.SystemKey
        }, ct);
        role.Id = checked((int)id);
    }

    /// <inheritdoc />
    public Task RenameRoleAsync(int roleId, string name, CancellationToken ct = default)
        => RunDbAsync(() =>
        {
            Update<SqliteRole>()
                .Where(x => x.Id == roleId)
                .Set(x => x.Name, name)
                .ExecuteAffrows();
        }, ct);

    /// <inheritdoc />
    public Task DeleteRoleAsync(int roleId, CancellationToken ct = default)
        => RunDbAsync(() => Delete<SqliteRole>()
            .Where(x => x.Id == roleId)
            .ExecuteAffrows(), ct);

    /// <inheritdoc />
    public Task ClearRolePermissionsAsync(int roleId, CancellationToken ct = default)
        => RunDbAsync(() => Delete<SqliteRolePermission>()
            .Where(x => x.RoleId == roleId)
            .ExecuteAffrows(), ct);

    /// <inheritdoc />
    public Task AddRolePermissionAsync(int roleId, PermissionCode permission, CancellationToken ct = default)
        => InsertAsync(new SqliteRolePermission
        {
            RoleId = roleId,
            PermissionCode = (int)permission
        }, ct);

    /// <inheritdoc />
    public Task<int> CountUsersByRoleAsync(int roleId, CancellationToken ct = default)
        => RunDbAsync(() => checked((int)Select<SqliteUser>()
            .Where(x => x.RoleId == roleId)
            .Count()), ct);

    /// <summary>
    /// 把查询结果转换为用户对象。
    /// </summary>
    private static User Map(SqliteUser row) => new()
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
}
