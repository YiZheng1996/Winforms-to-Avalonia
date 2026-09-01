namespace XXX.TestBench.Core.Domain.Identity;

public sealed class Role
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public HashSet<PermissionCode> Permissions { get; } = new();

    public bool HasPermission(PermissionCode permission) => Permissions.Contains(permission);
}
