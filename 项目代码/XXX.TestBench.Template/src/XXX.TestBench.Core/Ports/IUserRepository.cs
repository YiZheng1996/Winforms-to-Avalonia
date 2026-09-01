using XXX.TestBench.Core.Domain.Identity;

namespace XXX.TestBench.Core.Ports;

public interface IUserRepository
{
    Task<User?> GetByLoginNameAsync(string loginName, CancellationToken ct = default);
    Task<User?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Role?> GetRoleAsync(int roleId, CancellationToken ct = default);
    Task<IReadOnlyList<User>> ListUsersAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Role>> ListRolesAsync(CancellationToken ct = default);
    Task AddAsync(User user, CancellationToken ct = default);
    Task UpdateAsync(User user, CancellationToken ct = default);
}
