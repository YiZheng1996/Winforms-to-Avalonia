using System.Collections.ObjectModel;
using XXX.TestBench.App.Composition;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Domain.Identity;

namespace XXX.TestBench.App.ViewModels;

public sealed class UserRow
{
    public required string LoginName { get; init; }
    public required string DisplayName { get; init; }
    public required string RoleName { get; init; }
    public required string StatusText { get; init; }
}

public sealed class RoleRow
{
    public required string Name { get; init; }
    public required string Permissions { get; init; }
}

/// <summary>系统管理：用户/角色只读列表（第一版；写管理后续扩展）。</summary>
public sealed class SystemManagementViewModel : PageViewModel
{
    private readonly ShellServices _services;
    private readonly UserContext _actor;

    public SystemManagementViewModel(ShellServices services, UserContext actor)
    {
        _services = services;
        _actor = actor;
        LoadCommand = new RelayCommand(() => LoadAsync());
    }

    public override string Title => "系统管理";

    public ObservableCollection<UserRow> Users { get; } = new();
    public ObservableCollection<RoleRow> Roles { get; } = new();

    public RelayCommand LoadCommand { get; }

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
