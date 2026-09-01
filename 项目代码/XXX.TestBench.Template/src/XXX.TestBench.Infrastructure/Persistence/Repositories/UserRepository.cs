using Microsoft.Data.Sqlite;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Infrastructure.Persistence.Repositories;

public sealed class UserRepository : SqliteRepositoryBase, IUserRepository
{
    public UserRepository(ISqliteConnectionFactory factory) : base(factory) { }

    public async Task<User?> GetByLoginNameAsync(string loginName, CancellationToken ct = default)
    {
        var conn = OpenConnection();
        var owns = OwnsConnection;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, login_name, display_name, password_hash, must_change_password, is_enabled, failed_login_count, locked_until_utc, role_id, created_at_utc FROM users WHERE login_name = $name";
            cmd.Parameters.AddWithValue("$name", loginName);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            return await reader.ReadAsync(ct) ? Map(reader) : null;
        }
        finally { if (owns) await conn.DisposeAsync(); }
    }

    public async Task<User?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var conn = OpenConnection();
        var owns = OwnsConnection;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, login_name, display_name, password_hash, must_change_password, is_enabled, failed_login_count, locked_until_utc, role_id, created_at_utc FROM users WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            return await reader.ReadAsync(ct) ? Map(reader) : null;
        }
        finally { if (owns) await conn.DisposeAsync(); }
    }

    public async Task<Role?> GetRoleAsync(int roleId, CancellationToken ct = default)
    {
        var conn = OpenConnection();
        var owns = OwnsConnection;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, name FROM roles WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", roleId);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return null;
            var role = new Role { Id = roleId, Name = reader.GetString(1) };
            await using var permCmd = conn.CreateCommand();
            permCmd.CommandText = "SELECT permission_code FROM role_permissions WHERE role_id = $id";
            permCmd.Parameters.AddWithValue("$id", roleId);
            await using var permReader = await permCmd.ExecuteReaderAsync(ct);
            while (await permReader.ReadAsync(ct)) role.Permissions.Add((PermissionCode)permReader.GetInt32(0));
            return role;
        }
        finally { if (owns) await conn.DisposeAsync(); }
    }

    public async Task AddAsync(User user, CancellationToken ct = default)
    {
        var conn = OpenConnection();
        var owns = OwnsConnection;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO users (login_name, display_name, password_hash, must_change_password, is_enabled, failed_login_count, locked_until_utc, role_id, created_at_utc)
                VALUES ($login, $display, $hash, $must, $enabled, $failed, $locked, $role, $created)
                """;
            cmd.Parameters.AddWithValue("$login", user.LoginName);
            cmd.Parameters.AddWithValue("$display", user.DisplayName);
            cmd.Parameters.AddWithValue("$hash", user.PasswordHash);
            cmd.Parameters.AddWithValue("$must", user.MustChangePassword ? 1 : 0);
            cmd.Parameters.AddWithValue("$enabled", user.IsEnabled ? 1 : 0);
            cmd.Parameters.AddWithValue("$failed", user.FailedLoginCount);
            cmd.Parameters.AddWithValue("$locked", (object?)user.LockedUntilUtc?.ToString("O") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$role", user.RoleId);
            cmd.Parameters.AddWithValue("$created", user.CreatedAtUtc.ToString("O"));
            await cmd.ExecuteNonQueryAsync(ct);
            user.Id = (int)(long)(await LastInsertRowIdAsync(conn, ct));
        }
        finally { if (owns) await conn.DisposeAsync(); }
    }

    public async Task UpdateAsync(User user, CancellationToken ct = default)
    {
        var conn = OpenConnection();
        var owns = OwnsConnection;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE users SET display_name=$display, password_hash=$hash, must_change_password=$must, is_enabled=$enabled,
                failed_login_count=$failed, locked_until_utc=$locked WHERE id=$id
                """;
            cmd.Parameters.AddWithValue("$display", user.DisplayName);
            cmd.Parameters.AddWithValue("$hash", user.PasswordHash);
            cmd.Parameters.AddWithValue("$must", user.MustChangePassword ? 1 : 0);
            cmd.Parameters.AddWithValue("$enabled", user.IsEnabled ? 1 : 0);
            cmd.Parameters.AddWithValue("$failed", user.FailedLoginCount);
            cmd.Parameters.AddWithValue("$locked", (object?)user.LockedUntilUtc?.ToString("O") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$id", user.Id);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally { if (owns) await conn.DisposeAsync(); }
    }

    public async Task<IReadOnlyList<User>> ListUsersAsync(CancellationToken ct = default)
    {
        var result = new List<User>();
        var conn = OpenConnection();
        var owns = OwnsConnection;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, login_name, display_name, password_hash, must_change_password, is_enabled, failed_login_count, locked_until_utc, role_id, created_at_utc FROM users ORDER BY login_name";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) result.Add(Map(reader));
            return result;
        }
        finally { if (owns) await conn.DisposeAsync(); }
    }

    public async Task<IReadOnlyList<Role>> ListRolesAsync(CancellationToken ct = default)
    {
        var result = new List<Role>();
        var conn = OpenConnection();
        var owns = OwnsConnection;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, name FROM roles ORDER BY id";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) result.Add(new Role { Id = reader.GetInt32(0), Name = reader.GetString(1) });
            return result;
        }
        finally { if (owns) await conn.DisposeAsync(); }
    }
    private static User Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        LoginName = reader.GetString(1),
        DisplayName = reader.GetString(2),
        PasswordHash = reader.GetString(3),
        MustChangePassword = reader.GetInt32(4) != 0,
        IsEnabled = reader.GetInt32(5) != 0,
        FailedLoginCount = reader.GetInt32(6),
        LockedUntilUtc = ParseNullableUtc(reader.GetValue(7)),
        RoleId = reader.GetInt32(8),
        CreatedAtUtc = ParseUtc(reader.GetString(9))
    };

    internal static async Task<long> LastInsertRowIdAsync(SqliteConnection conn, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT last_insert_rowid()";
        return (long)(await cmd.ExecuteScalarAsync(ct))!;
    }
}
