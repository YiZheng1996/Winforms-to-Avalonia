using XXX.TestBench.App.ViewModels;
using XXX.TestBench.Core.Domain.Identity;
using Xunit;

namespace XXX.TestBench.App.Tests;

public sealed class SystemManagementViewModelTests
{
    [Fact]
    public async Task RoleAndUserManagement_PersistsRolePermissionsAndUserAssignment()
    {
        using var harness = AppTestHarness.Create();
        var actor = await harness.AdminActorAsync();
        var viewModel = new SystemManagementViewModel(harness.Services, actor);

        await viewModel.LoadAsync();
        Assert.True(viewModel.CanManageUsers);
        Assert.True(viewModel.CanManageRoles);
        Assert.Equal(3, viewModel.Roles.Count);
        Assert.Equal("系统管理员", viewModel.Roles.Single(role => role.SystemKey == "Administrator").DisplayName);
        Assert.Equal("试验操作员", viewModel.Roles.Single(role => role.SystemKey == "Operator").DisplayName);
        Assert.Equal("设备维护员", viewModel.Roles.Single(role => role.SystemKey == "Maintenance").DisplayName);
        Assert.Contains("系统管理员", viewModel.RoleFilterOptions);

        await viewModel.CreateRoleFromDialogAsync("报表工程师");
        var customRole = viewModel.Roles.Single(role => role.Name == "报表工程师");
        viewModel.SelectedRole = customRole;
        var reportPermission = viewModel.PermissionGroups
            .SelectMany(group => group.Options)
            .Single(option => option.Code == PermissionCode.GenerateReports);
        reportPermission.IsChecked = true;
        await viewModel.SaveRolePermissionsCommand.ExecuteAsync(null);

        var persistedRole = await harness.Services.UserRepository.GetRoleAsync(customRole.Id);
        Assert.NotNull(persistedRole);
        Assert.Contains(PermissionCode.GenerateReports, persistedRole!.Permissions);
        Assert.Contains(PermissionCode.ViewRecords, persistedRole.Permissions);

        await viewModel.CreateUserFromDialogAsync(new UserDialogResult(
            "report-user", "报表用户", customRole.Id, true, "temporary-123"));
        var user = await harness.Services.UserRepository.GetByLoginNameAsync("report-user");
        Assert.NotNull(user);
        Assert.Equal(customRole.Id, user!.RoleId);
        Assert.True(user.MustChangePassword);
    }

    [Fact]
    public async Task UserRoleUpdate_IsPersistedForNextContextBuild()
    {
        using var harness = AppTestHarness.Create();
        var actor = await harness.AdminActorAsync();
        var roles = await harness.Services.UserRepository.ListRolesAsync();
        var operatorRole = roles.Single(role => role.SystemKey == "Operator");

        var user = await harness.Services.IdentityAdministration.CreateUserAsync(
            actor, "role-change", "待改派用户", "temporary-123", operatorRole.Id);
        await harness.Services.IdentityAdministration.UpdateUserAsync(
            actor, user.Id, "已改派用户", actor.Role.Id, isEnabled: true);

        var persisted = await harness.Services.UserRepository.GetByIdAsync(user.Id);
        Assert.NotNull(persisted);
        Assert.Equal(actor.Role.Id, persisted!.RoleId);

        var login = await harness.Services.Authentication.LoginAsync("role-change", "temporary-123");
        Assert.True(login.Success);
        var context = await harness.Services.Authentication.BuildUserContextAsync(login.Session!.Token);
        Assert.Equal(actor.Role.Id, context.Role.Id);
        Assert.Contains(PermissionCode.ManageRoles, context.Role.Permissions);
    }
}
