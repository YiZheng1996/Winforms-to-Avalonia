using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using XXX.TestBench.App.Composition;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Domain.Identity;

namespace XXX.TestBench.App.ViewModels;

/// <summary>
/// 用户表格中的一行。
/// </summary>
public sealed class UserRow
{
    public required string LoginName { get; init; }
    public required string DisplayName { get; init; }
    public required string RoleName { get; init; }
    public required string StatusText { get; init; }
}

/// <summary>
/// 角色表格中的一行。
/// </summary>
public sealed class RoleRow
{
    public required string Name { get; init; }
    public required string Permissions { get; init; }
}

/// <summary>
/// 系统管理页面：只读展示用户与角色列表（第一版；写管理后续扩展）。
/// </summary>
public sealed partial class SystemManagementViewModel : PageViewModel
{
    private readonly ShellServices _services;
    private readonly UserContext _actor;

    public SystemManagementViewModel(ShellServices services, UserContext actor)
    {
        _services = services;
        _actor = actor;
    }

    public override string Title => "系统管理";

    /// <summary>
    /// 页面展示的用户列表。
    /// </summary>
    public ObservableCollection<UserRow> Users { get; } = new();

    /// <summary>
    /// 页面展示的角色列表。
    /// </summary>
    public ObservableCollection<RoleRow> Roles { get; } = new();

    /// <summary>
    /// 页面加载命令：读取用户与角色并刷新两个列表。
    /// </summary>
    [RelayCommand]
    public override async Task LoadAsync(CancellationToken ct = default)
    {
        IsBusy = true;
        try
        {
            Users.Clear();
            Roles.Clear();
            var users = await _services.UserRepository.ListUsersAsync(ct);
            var roles = await _services.UserRepository.ListRolesAsync(ct);
            foreach (var role in roles)
                Roles.Add(new RoleRow { Name = role.Name, Permissions = string.Join("、", role.Permissions.Select(p => p.ToString())) });
            foreach (var user in users)
            {
                var role = roles.FirstOrDefault(r => r.Id == user.RoleId);
                Users.Add(new UserRow
                {
                    LoginName = user.LoginName,
                    DisplayName = user.DisplayName,
                    RoleName = role?.Name ?? user.RoleId.ToString(),
                    StatusText = user.IsEnabled ? (user.MustChangePassword ? "待改密" : "正常") : "停用"
                });
            }
        }
        finally { IsBusy = false; }
    }
}