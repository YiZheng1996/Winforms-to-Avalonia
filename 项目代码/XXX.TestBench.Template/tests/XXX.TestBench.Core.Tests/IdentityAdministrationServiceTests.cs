using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Identity;
using Xunit;

namespace XXX.TestBench.Core.Tests;

public sealed class IdentityAdministrationServiceTests
{
    [Fact]
    public async Task CreateRole_NormalizesDependencies_AndWritesAudit()
    {
        var context = CreateContext();

        var role = await context.Service.CreateRoleAsync(
            context.Admin,
            "报表工程师",
            new[] { PermissionCode.GenerateReports });

        Assert.Equal("报表工程师", role.Name);
        Assert.Contains(PermissionCode.GenerateReports, role.Permissions);
        Assert.Contains(PermissionCode.ViewRecords, role.Permissions);
        Assert.True(context.Uow.Last.Committed);
        Assert.Contains(context.Audit.Entries, entry => entry.Contains("RoleCreated", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReplaceRolePermissions_PreservesReservedLegacyPermission()
    {
        var context = CreateContext();
        var role = new Role { Id = 3, Name = "兼容角色" };
        role.Permissions.Add(PermissionCode.ManageRecipes);
        context.Users.Roles.Add(role);

        await context.Service.ReplaceRolePermissionsAsync(
            context.Admin,
            role.Id,
            new[] { PermissionCode.GenerateReports });

        Assert.Contains(PermissionCode.ManageRecipes, role.Permissions);
        Assert.Contains(PermissionCode.GenerateReports, role.Permissions);
        Assert.Contains(PermissionCode.ViewRecords, role.Permissions);
        Assert.True(context.Uow.Last.Committed);
    }

    [Fact]
    public async Task UserLifecycle_AssignsRole_ResetsPassword_AndProtectsAdmin()
    {
        var context = CreateContext();

        var created = await context.Service.CreateUserAsync(
            context.Admin,
            "engineer",
            "试验工程师",
            "temp-123",
            roleId: 2);

        Assert.Equal(2, created.RoleId);
        Assert.True(created.MustChangePassword);
        Assert.Equal("H:temp-123", created.PasswordHash);

        await context.Service.UpdateUserAsync(context.Admin, created.Id, "高级试验工程师", roleId: 1, isEnabled: true);
        Assert.Equal("高级试验工程师", created.DisplayName);
        Assert.Equal(1, created.RoleId);

        created.FailedLoginCount = 3;
        created.LockedUntilUtc = DateTime.UtcNow.AddMinutes(5);
        await context.Service.ResetPasswordAsync(context.Admin, created.Id, "reset-456");
        Assert.Equal("H:reset-456", created.PasswordHash);
        Assert.True(created.MustChangePassword);
        Assert.Equal(0, created.FailedLoginCount);
        Assert.Null(created.LockedUntilUtc);

        await Assert.ThrowsAsync<DomainException>(() =>
            context.Service.UpdateUserAsync(context.Admin, 1, "管理员", roleId: 2, isEnabled: false));
    }

    [Fact]
    public async Task RoleManagement_RequiresManageRolesPermission()
    {
        var context = CreateContext();
        var userManager = TestContexts.With(PermissionCode.ManageUsers);

        await Assert.ThrowsAsync<AuthorizationException>(() =>
            context.Service.CreateRoleAsync(userManager, "无权角色", new[] { PermissionCode.ViewOverview }));

        Assert.Contains(context.Audit.Entries, entry => entry.Contains("AuthorizationDenied", StringComparison.Ordinal));
    }

    private static TestContext CreateContext()
    {
        var users = new FakeUserRepository();
        var administrator = new Role { Id = 1, Name = "Administrator", SystemKey = "Administrator" };
        administrator.Permissions.UnionWith(Enum.GetValues<PermissionCode>());
        users.Roles.Add(administrator);
        users.Roles.Add(new Role { Id = 2, Name = "Operator", SystemKey = "Operator" });
        users.AddAsync(new User
        {
            Id = 1,
            LoginName = "admin",
            DisplayName = "管理员",
            PasswordHash = "H:admin",
            RoleId = administrator.Id,
            CreatedAtUtc = DateTime.UtcNow
        }).GetAwaiter().GetResult();

        var clock = new FixedClock();
        var audit = new FakeAuditLog();
        var uow = new FakeUnitOfWorkFactory();
        var service = new IdentityAdministrationService(users, new TestPasswordHasher(), clock, audit, uow);
        var actor = new UserContext
        {
            UserId = 1,
            LoginName = "admin",
            DisplayName = "管理员",
            Role = administrator
        };
        return new TestContext(service, users, audit, uow, actor);
    }

    private sealed record TestContext(
        IdentityAdministrationService Service,
        FakeUserRepository Users,
        FakeAuditLog Audit,
        FakeUnitOfWorkFactory Uow,
        UserContext Admin);

    private sealed class TestPasswordHasher : XXX.TestBench.Core.Ports.IPasswordHasher
    {
        public string Hash(string password) => "H:" + password;
        public bool Verify(string password, string hash) => hash == Hash(password);
    }
}
