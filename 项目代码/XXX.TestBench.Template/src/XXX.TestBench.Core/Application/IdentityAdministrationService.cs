using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Core.Application;

/// <summary>
/// 用户、角色和角色权限的管理用例边界。
/// 所有写操作均在此处进行操作者授权、领域校验、事务控制和审计。
/// </summary>
public sealed class IdentityAdministrationService
{
    private const string ProtectedAdminLoginName = "admin";
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly IClock _clock;
    private readonly IAuditLog _audit;
    private readonly IUnitOfWorkFactory _unitOfWorkFactory;

    public IdentityAdministrationService(
        IUserRepository users,
        IPasswordHasher hasher,
        IClock clock,
        IAuditLog audit,
        IUnitOfWorkFactory unitOfWorkFactory)
    {
        _users = users;
        _hasher = hasher;
        _clock = clock;
        _audit = audit;
        _unitOfWorkFactory = unitOfWorkFactory;
    }

    /// <summary>
    /// 新增用户。初始密码只保存散列值，并强制首次登录改密。
    /// </summary>
    public async Task<User> CreateUserAsync(
        UserContext actor,
        string loginName,
        string displayName,
        string temporaryPassword,
        int roleId,
        CancellationToken ct = default)
    {
        await EnsurePermissionAsync(actor, PermissionCode.ManageUsers, ct);
        loginName = Normalize(loginName, "登录账号", 64);
        displayName = Normalize(displayName, "显示名称", 64);
        ValidatePassword(temporaryPassword, "初始密码");
        if (await _users.GetByLoginNameAsync(loginName, ct) is not null)
            throw new DomainException("登录账号已存在");
        if (await _users.GetRoleAsync(roleId, ct) is null)
            throw new DomainException("所选角色不存在");

        var user = new User
        {
            LoginName = loginName,
            DisplayName = displayName,
            PasswordHash = _hasher.Hash(temporaryPassword),
            MustChangePassword = true,
            IsEnabled = true,
            RoleId = roleId,
            CreatedAtUtc = _clock.UtcNow
        };
        await _users.AddAsync(user, ct);
        await _audit.WriteAsync(actor.LoginName, "UserCreated", $"user:{user.Id}", $"login:{user.LoginName};role:{roleId}", ct);
        return user;
    }

    /// <summary>
    /// 修改用户显示名称、角色和启用状态。登录账号保持不变。
    /// </summary>
    public async Task UpdateUserAsync(
        UserContext actor,
        int userId,
        string displayName,
        int roleId,
        bool isEnabled,
        CancellationToken ct = default)
    {
        await EnsurePermissionAsync(actor, PermissionCode.ManageUsers, ct);
        var user = await _users.GetByIdAsync(userId, ct) ?? throw new DomainException("用户不存在");
        var oldRole = await _users.GetRoleAsync(user.RoleId, ct) ?? throw new DomainException("用户原角色不存在");
        if (await _users.GetRoleAsync(roleId, ct) is null)
            throw new DomainException("所选角色不存在");
        if (user.Id == actor.UserId && (!isEnabled || roleId != user.RoleId))
            throw new DomainException("不能在当前会话中停用或改派当前登录账号");
        if (IsProtectedAdmin(user, oldRole) && (!isEnabled || roleId != user.RoleId))
            throw new DomainException("内置管理员账号不能停用或改派角色");

        displayName = Normalize(displayName, "显示名称", 64);
        var oldDisplayName = user.DisplayName;
        var oldRoleId = user.RoleId;
        user.DisplayName = displayName;
        user.RoleId = roleId;
        user.IsEnabled = isEnabled;
        await _users.UpdateAsync(user, ct);
        await _audit.WriteAsync(
            actor.LoginName,
            "UserUpdated",
            $"user:{user.Id}",
            $"display:{oldDisplayName}->{displayName};role:{oldRoleId}->{roleId};enabled:{isEnabled}",
            ct);
    }

    /// <summary>
    /// 重置其他用户密码，并要求其下次登录修改。
    /// </summary>
    public async Task ResetPasswordAsync(
        UserContext actor,
        int userId,
        string temporaryPassword,
        CancellationToken ct = default)
    {
        await EnsurePermissionAsync(actor, PermissionCode.ManageUsers, ct);
        if (userId == actor.UserId)
            throw new DomainException("当前账号请使用个人改密流程，不能由用户管理页重置");
        ValidatePassword(temporaryPassword, "临时密码");
        var user = await _users.GetByIdAsync(userId, ct) ?? throw new DomainException("用户不存在");
        user.PasswordHash = _hasher.Hash(temporaryPassword);
        user.MustChangePassword = true;
        user.FailedLoginCount = 0;
        user.LockedUntilUtc = null;
        await _users.UpdateAsync(user, ct);
        await _audit.WriteAsync(actor.LoginName, "UserPasswordReset", $"user:{user.Id}", user.LoginName, ct);
    }

    /// <summary>
    /// 清除账号失败次数和临时锁定状态。
    /// </summary>
    public async Task UnlockUserAsync(UserContext actor, int userId, CancellationToken ct = default)
    {
        await EnsurePermissionAsync(actor, PermissionCode.ManageUsers, ct);
        var user = await _users.GetByIdAsync(userId, ct) ?? throw new DomainException("用户不存在");
        user.FailedLoginCount = 0;
        user.LockedUntilUtc = null;
        await _users.UpdateAsync(user, ct);
        await _audit.WriteAsync(actor.LoginName, "UserUnlocked", $"user:{user.Id}", user.LoginName, ct);
    }

    /// <summary>
    /// 新增自定义角色并写入初始权限。
    /// </summary>
    public async Task<Role> CreateRoleAsync(
        UserContext actor,
        string name,
        IEnumerable<PermissionCode> permissions,
        CancellationToken ct = default)
    {
        await EnsurePermissionAsync(actor, PermissionCode.ManageRoles, ct);
        name = Normalize(name, "角色名称", 64);
        if (await _users.RoleNameExistsAsync(name, null, ct))
            throw new DomainException("角色名称已存在");
        var normalized = NormalizePermissions(permissions);
        var role = new Role { Name = name };
        await using var uow = _unitOfWorkFactory.Create();
        await uow.BeginTransactionAsync(ct);
        try
        {
            await _users.AddRoleAsync(role, ct);
            await _users.ClearRolePermissionsAsync(role.Id, ct);
            foreach (var permission in normalized)
                await _users.AddRolePermissionAsync(role.Id, permission, ct);
            await uow.CommitAsync(ct);
        }
        catch
        {
            await uow.RollbackAsync(ct);
            throw;
        }
        await _audit.WriteAsync(actor.LoginName, "RoleCreated", $"role:{role.Id}", $"name:{role.Name};permissions:{FormatPermissions(normalized)}", ct);
        role.Permissions.UnionWith(normalized);
        return role;
    }

    /// <summary>
    /// 修改自定义角色名称；内置角色名称固定。
    /// </summary>
    public async Task RenameRoleAsync(UserContext actor, int roleId, string name, CancellationToken ct = default)
    {
        await EnsurePermissionAsync(actor, PermissionCode.ManageRoles, ct);
        var role = await _users.GetRoleAsync(roleId, ct) ?? throw new DomainException("角色不存在");
        if (role.SystemKey is not null)
            throw new DomainException("内置角色名称不能修改");
        name = Normalize(name, "角色名称", 64);
        if (await _users.RoleNameExistsAsync(name, roleId, ct))
            throw new DomainException("角色名称已存在");
        var oldName = role.Name;
        await _users.RenameRoleAsync(roleId, name, ct);
        await _audit.WriteAsync(actor.LoginName, "RoleRenamed", $"role:{roleId}", $"name:{oldName}->{name}", ct);
    }

    /// <summary>
    /// 删除未被用户使用的自定义角色。
    /// </summary>
    public async Task DeleteRoleAsync(UserContext actor, int roleId, CancellationToken ct = default)
    {
        await EnsurePermissionAsync(actor, PermissionCode.ManageRoles, ct);
        var role = await _users.GetRoleAsync(roleId, ct) ?? throw new DomainException("角色不存在");
        if (role.SystemKey is not null)
            throw new DomainException("内置角色不能删除");
        var userCount = await _users.CountUsersByRoleAsync(roleId, ct);
        if (userCount > 0)
            throw new DomainException($"角色仍被 {userCount} 个用户使用，不能删除");

        await using var uow = _unitOfWorkFactory.Create();
        await uow.BeginTransactionAsync(ct);
        try
        {
            await _users.ClearRolePermissionsAsync(roleId, ct);
            await _users.DeleteRoleAsync(roleId, ct);
            await uow.CommitAsync(ct);
        }
        catch
        {
            await uow.RollbackAsync(ct);
            throw;
        }
        await _audit.WriteAsync(actor.LoginName, "RoleDeleted", $"role:{roleId}", role.Name, ct);
    }

    /// <summary>
    /// 原子替换角色权限。Administrator 固定为全权限，其余角色按目录校验依赖。
    /// </summary>
    public async Task ReplaceRolePermissionsAsync(
        UserContext actor,
        int roleId,
        IEnumerable<PermissionCode> permissions,
        CancellationToken ct = default)
    {
        await EnsurePermissionAsync(actor, PermissionCode.ManageRoles, ct);
        var role = await _users.GetRoleAsync(roleId, ct) ?? throw new DomainException("角色不存在");
        if (string.Equals(role.SystemKey, "Administrator", StringComparison.Ordinal))
            throw new DomainException("系统管理员权限固定为全部权限");

        var normalized = NormalizePermissions(permissions);
        var reserved = role.Permissions.Where(permission => !PermissionCatalog.ActiveCodes.Contains(permission));
        normalized.UnionWith(reserved);
        var before = role.Permissions.ToHashSet();
        await using var uow = _unitOfWorkFactory.Create();
        await uow.BeginTransactionAsync(ct);
        try
        {
            await _users.ClearRolePermissionsAsync(roleId, ct);
            foreach (var permission in normalized)
                await _users.AddRolePermissionAsync(roleId, permission, ct);
            await uow.CommitAsync(ct);
        }
        catch
        {
            await uow.RollbackAsync(ct);
            throw;
        }
        await _audit.WriteAsync(
            actor.LoginName,
            "RolePermissionsChanged",
            $"role:{roleId}",
            $"before:{FormatPermissions(before)};after:{FormatPermissions(normalized)}",
            ct);
    }

    private async Task EnsurePermissionAsync(UserContext actor, PermissionCode permission, CancellationToken ct)
    {
        try
        {
            actor.EnsurePermission(permission);
        }
        catch (AuthorizationException)
        {
            await _audit.WriteAsync(actor.LoginName, "AuthorizationDenied", "identity-management", permission.ToString(), ct);
            throw;
        }
    }

    private static HashSet<PermissionCode> NormalizePermissions(IEnumerable<PermissionCode> permissions)
    {
        var normalized = PermissionCatalog.Normalize(permissions);
        if (normalized.Count == 0)
            throw new DomainException("角色至少需要保留一项有效权限");
        return normalized;
    }

    private static string Normalize(string value, string label, int maxLength)
    {
        value = value?.Trim() ?? string.Empty;
        if (value.Length == 0) throw new DomainException($"{label}不能为空");
        if (value.Length > maxLength) throw new DomainException($"{label}不能超过 {maxLength} 个字符");
        return value;
    }

    private static void ValidatePassword(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new DomainException($"{label}不能为空");
    }

    private static bool IsProtectedAdmin(User user, Role role)
        => string.Equals(user.LoginName, ProtectedAdminLoginName, StringComparison.OrdinalIgnoreCase)
           && string.Equals(role.SystemKey, "Administrator", StringComparison.Ordinal);

    private static string FormatPermissions(IEnumerable<PermissionCode> permissions)
        => string.Join(",", permissions.OrderBy(permission => (int)permission).Select(PermissionCatalog.GetName));
}
