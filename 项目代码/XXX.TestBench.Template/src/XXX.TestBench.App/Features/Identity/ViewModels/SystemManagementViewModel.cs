using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XXX.TestBench.App.Composition;
using XXX.TestBench.App.Localization;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Domain.Identity;

namespace XXX.TestBench.App.ViewModels;

/// <summary>
/// 用户管理列表中的一行。
/// </summary>
public sealed class UserRow
{
    public required int Id { get; init; }
    public required string LoginName { get; init; }
    public required string DisplayName { get; init; }
    public required int RoleId { get; init; }
    public required string RoleName { get; init; }
    public required bool IsEnabled { get; init; }
    public required bool MustChangePassword { get; init; }
    public required bool IsLocked { get; init; }
    public required bool IsCurrentUser { get; init; }
    public required bool IsProtectedAdmin { get; init; }
    public required string StatusText { get; init; }
    public string PasswordStatusText => MustChangePassword ? "待改密" : "已设置";
    public bool CanToggle => !IsCurrentUser && !IsProtectedAdmin;
    public bool CanResetPassword => !IsCurrentUser;
}

/// <summary>
/// 角色管理列表中的一行。
/// </summary>
public sealed class RoleRow
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required string? SystemKey { get; init; }
    public required int UserCount { get; init; }
    public required IReadOnlySet<PermissionCode> Permissions { get; init; }
    public string DisplayName => RoleTextLocalizer.DisplayName(SystemKey, Name);
    public bool IsBuiltIn => SystemKey is not null;
    public bool IsAdministrator => string.Equals(SystemKey, "Administrator", StringComparison.Ordinal);
    public bool CanRename => !IsBuiltIn;
    public bool CanDelete => !IsBuiltIn && UserCount == 0;
    public string BuiltInText => IsBuiltIn ? "内置角色" : "自定义角色";
    public string PermissionSummary => Permissions.Count == 0
        ? "未配置有效权限"
        : string.Join("、", Permissions.OrderBy(permission => (int)permission).Select(PermissionCatalog.GetName));
}

/// <summary>
/// 权限矩阵中的一个权限选项。
/// </summary>
public sealed partial class PermissionOptionViewModel : ObservableObject
{
    private readonly Action<PermissionOptionViewModel>? _changed;
    private bool _isChecked;

    public PermissionOptionViewModel(PermissionDescriptor descriptor, bool isChecked, bool isReadOnly, Action<PermissionOptionViewModel>? changed)
    {
        Code = descriptor.Code;
        Name = descriptor.Name;
        Description = descriptor.Description;
        RequiredPermissions = descriptor.RequiredPermissions;
        IsReadOnly = isReadOnly;
        _isChecked = isChecked;
        _changed = changed;
    }

    public PermissionCode Code { get; }
    public string Name { get; }
    public string Description { get; }
    public IReadOnlyList<PermissionCode> RequiredPermissions { get; }
    public bool IsReadOnly { get; }

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (SetProperty(ref _isChecked, value))
                _changed?.Invoke(this);
        }
    }

    public void SetCheckedSilently(bool value) => SetProperty(ref _isChecked, value, nameof(IsChecked));
}

/// <summary>
/// 权限矩阵中的分组。
/// </summary>
public sealed class PermissionGroupViewModel
{
    public PermissionGroupViewModel(string name, IEnumerable<PermissionOptionViewModel> options)
    {
        Name = name;
        Options = new ObservableCollection<PermissionOptionViewModel>(options);
    }

    public string Name { get; }
    public ObservableCollection<PermissionOptionViewModel> Options { get; }
}

/// <summary>
/// 系统管理页面：用户管理与角色权限配置的统一入口。
/// 页面只负责状态和交互，实际权限校验由 IdentityAdministrationService 完成。
/// </summary>
public sealed partial class SystemManagementViewModel : PageViewModel
{
    private readonly ShellServices _services;
    private readonly UserContext _actor;
    private IReadOnlyList<UserRow> _allUsers = Array.Empty<UserRow>();

    public SystemManagementViewModel(ShellServices services, UserContext actor)
    {
        _services = services;
        _actor = actor;
        CanManageUsers = actor.HasPermission(PermissionCode.ManageUsers);
        CanManageRoles = actor.HasPermission(PermissionCode.ManageRoles);
        SelectedTabIndex = CanManageUsers ? 0 : 1;
    }

    public override string Title => "系统管理";
    public bool CanManageUsers { get; }
    public bool CanManageRoles { get; }

    public ObservableCollection<UserRow> Users { get; } = new();
    public ObservableCollection<UserRow> VisibleUsers { get; } = new();
    public ObservableCollection<RoleRow> Roles { get; } = new();
    public ObservableCollection<string> RoleFilterOptions { get; } = new();
    public IReadOnlyList<string> StatusFilterOptions { get; } = new[] { "全部状态", "正常", "待改密", "锁定", "停用" };
    public ObservableCollection<PermissionGroupViewModel> PermissionGroups { get; } = new();

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private string _keyword = string.Empty;

    [ObservableProperty]
    private string _selectedRoleFilter = "全部角色";

    [ObservableProperty]
    private string _selectedStatusFilter = "全部状态";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedUser))]
    [NotifyPropertyChangedFor(nameof(CanToggleSelectedUser))]
    [NotifyPropertyChangedFor(nameof(CanResetSelectedUser))]
    [NotifyPropertyChangedFor(nameof(IsSelectedUserLocked))]
    private UserRow? _selectedUser;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedRole))]
    [NotifyPropertyChangedFor(nameof(SelectedRoleIsAdministrator))]
    [NotifyPropertyChangedFor(nameof(CanEditSelectedRole))]
    [NotifyPropertyChangedFor(nameof(CanRenameSelectedRole))]
    [NotifyPropertyChangedFor(nameof(CanDeleteSelectedRole))]
    [NotifyPropertyChangedFor(nameof(RoleEditorHint))]
    private RoleRow? _selectedRole;

    private bool _rolePermissionsDirty;

    public bool HasSelectedUser => SelectedUser is not null;
    public bool HasVisibleUsers => VisibleUsers.Count > 0;
    public bool CanToggleSelectedUser => SelectedUser?.CanToggle == true;
    public bool CanResetSelectedUser => SelectedUser?.CanResetPassword == true;
    public bool IsSelectedUserLocked => SelectedUser?.IsLocked == true;
    public bool HasSelectedRole => SelectedRole is not null;
    public bool SelectedRoleIsAdministrator => SelectedRole?.IsAdministrator == true;
    public bool CanEditSelectedRole => CanManageRoles && SelectedRole is not null && !SelectedRole.IsAdministrator;
    public bool CanRenameSelectedRole => CanManageRoles && SelectedRole?.CanRename == true;
    public bool CanDeleteSelectedRole => CanManageRoles && SelectedRole?.CanDelete == true;
    public bool CanSaveRolePermissions => CanEditSelectedRole && _rolePermissionsDirty;
    public string RoleEditorHint => SelectedRole is null
        ? "请选择角色查看权限"
        : SelectedRoleIsAdministrator
            ? "系统管理员为系统保护角色，固定拥有全部权限"
            : "权限修改将在相关用户下次登录后生效";

    partial void OnKeywordChanged(string value) => ApplyUserFilters();
    partial void OnSelectedRoleFilterChanged(string value) => ApplyUserFilters();
    partial void OnSelectedStatusFilterChanged(string value) => ApplyUserFilters();

    partial void OnSelectedRoleChanged(RoleRow? value)
    {
        _rolePermissionsDirty = false;
        OnPropertyChanged(nameof(CanSaveRolePermissions));
        _ = LoadPermissionGroupsAsync(value);
    }

    /// <summary>
    /// 读取用户、角色及其权限，并恢复当前选择。
    /// </summary>
    [RelayCommand]
    public override async Task LoadAsync(CancellationToken ct = default)
    {
        IsBusy = true;
        try
        {
            var selectedUserId = SelectedUser?.Id;
            var selectedRoleId = SelectedRole?.Id;
            var users = await _services.UserRepository.ListUsersAsync(ct);
            var roles = await _services.UserRepository.ListRolesAsync(ct);
            var roleLookup = roles.ToDictionary(role => role.Id);
            _allUsers = users.Select(user => ToUserRow(user, roleLookup.GetValueOrDefault(user.RoleId), _actor.UserId)).ToArray();

            Users.Clear();
            foreach (var row in _allUsers) Users.Add(row);
            RebuildRoleFilters(roles);
            ApplyUserFilters();

            Roles.Clear();
            foreach (var role in roles)
            {
                var count = users.Count(user => user.RoleId == role.Id);
                Roles.Add(new RoleRow
                {
                    Id = role.Id,
                    Name = role.Name,
                    SystemKey = role.SystemKey,
                    UserCount = count,
                    Permissions = role.Permissions.ToHashSet()
                });
            }

            if (selectedUserId is not null)
                SelectedUser = VisibleUsers.FirstOrDefault(user => user.Id == selectedUserId);
            if (selectedRoleId is not null)
                SelectedRole = Roles.FirstOrDefault(role => role.Id == selectedRoleId);
            if (SelectedRole is null && Roles.Count > 0 && CanManageRoles)
                SelectedRole = Roles[0];
            if (SelectedRole is null)
                PermissionGroups.Clear();
            else
                await LoadPermissionGroupsAsync(SelectedRole);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task CreateUserFromDialogAsync(UserDialogResult result)
    {
        try
        {
            await _services.IdentityAdministration.CreateUserAsync(
                _actor, result.LoginName, result.DisplayName, result.TemporaryPassword!, result.RoleId);
            StatusMessage = "用户已创建，首次登录须修改密码";
            await LoadAsync();
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
    }

    public async Task UpdateUserFromDialogAsync(int userId, UserDialogResult result)
    {
        try
        {
            await _services.IdentityAdministration.UpdateUserAsync(
                _actor, userId, result.DisplayName, result.RoleId, result.IsEnabled);
            StatusMessage = "用户信息已保存，角色变更将在下次登录后生效";
            await LoadAsync();
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
    }

    public async Task ResetPasswordFromDialogAsync(int userId, string temporaryPassword)
    {
        try
        {
            await _services.IdentityAdministration.ResetPasswordAsync(_actor, userId, temporaryPassword);
            StatusMessage = "密码已重置，用户下次登录须修改密码";
            await LoadAsync();
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
    }

    public async Task CreateRoleFromDialogAsync(string name)
    {
        try
        {
            var role = await _services.IdentityAdministration.CreateRoleAsync(
                _actor, name, new[] { PermissionCode.ViewOverview });
            StatusMessage = "角色已创建，请配置并保存权限";
            await LoadAsync();
            SelectedRole = Roles.FirstOrDefault(item => item.Id == role.Id);
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
    }

    public async Task RenameRoleFromDialogAsync(string name)
    {
        if (SelectedRole is null) return;
        try
        {
            await _services.IdentityAdministration.RenameRoleAsync(_actor, SelectedRole.Id, name);
            StatusMessage = "角色名称已保存";
            await LoadAsync();
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
    }

    [RelayCommand]
    public async Task ToggleSelectedUserAsync()
    {
        if (SelectedUser is not { CanToggle: true } user) return;
        try
        {
            await _services.IdentityAdministration.UpdateUserAsync(
                _actor, user.Id, user.DisplayName, user.RoleId, !user.IsEnabled);
            StatusMessage = user.IsEnabled ? "用户已停用" : "用户已启用";
            await LoadAsync();
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
    }

    [RelayCommand]
    public async Task UnlockSelectedUserAsync()
    {
        if (SelectedUser is null) return;
        try
        {
            await _services.IdentityAdministration.UnlockUserAsync(_actor, SelectedUser.Id);
            StatusMessage = "用户已解锁";
            await LoadAsync();
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
    }

    [RelayCommand]
    public async Task DeleteSelectedRoleAsync()
    {
        if (SelectedRole is not { CanDelete: true } role) return;
        try
        {
            await _services.IdentityAdministration.DeleteRoleAsync(_actor, role.Id);
            StatusMessage = "角色已删除";
            SelectedRole = null;
            await LoadAsync();
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
    }

    [RelayCommand]
    public async Task SaveRolePermissionsAsync()
    {
        if (!CanSaveRolePermissions || SelectedRole is null) return;
        try
        {
            var permissions = PermissionGroups
                .SelectMany(group => group.Options)
                .Where(option => option.IsChecked)
                .Select(option => option.Code)
                .ToArray();
            await _services.IdentityAdministration.ReplaceRolePermissionsAsync(_actor, SelectedRole.Id, permissions);
            _rolePermissionsDirty = false;
            OnPropertyChanged(nameof(CanSaveRolePermissions));
            StatusMessage = "角色权限已保存，相关用户重新登录后生效";
            await LoadAsync();
        }
        catch (Exception ex) { StatusMessage = ex.Message; }
    }

    [RelayCommand]
    public async Task CancelRolePermissionsAsync()
    {
        if (SelectedRole is null) return;
        _rolePermissionsDirty = false;
        await LoadPermissionGroupsAsync(SelectedRole);
        OnPropertyChanged(nameof(CanSaveRolePermissions));
        StatusMessage = "已放弃未保存的权限调整";
    }

    [RelayCommand]
    public Task SelectAllPermissionsAsync()
    {
        if (!CanEditSelectedRole) return Task.CompletedTask;
        foreach (var option in PermissionGroups.SelectMany(group => group.Options))
            option.IsChecked = true;
        return Task.CompletedTask;
    }

    private async Task LoadPermissionGroupsAsync(RoleRow? role)
    {
        PermissionGroups.Clear();
        if (role is null || !CanManageRoles) return;
        foreach (var group in PermissionCatalog.Active.GroupBy(item => item.Group))
        {
            var options = group.Select(descriptor => new PermissionOptionViewModel(
                descriptor,
                role.Permissions.Contains(descriptor.Code),
                role.IsAdministrator,
                OnPermissionChanged));
            PermissionGroups.Add(new PermissionGroupViewModel(group.Key, options));
        }
        _rolePermissionsDirty = false;
        OnPropertyChanged(nameof(CanSaveRolePermissions));
        await Task.CompletedTask;
    }

    private void OnPermissionChanged(PermissionOptionViewModel option)
    {
        if (option.IsReadOnly) return;
        if (option.IsChecked)
        {
            foreach (var required in option.RequiredPermissions)
            {
                var requiredOption = PermissionGroups.SelectMany(group => group.Options).FirstOrDefault(item => item.Code == required);
                requiredOption?.SetCheckedSilently(true);
            }
        }
        else
        {
            foreach (var dependent in PermissionGroups.SelectMany(group => group.Options)
                         .Where(item => item.RequiredPermissions.Contains(option.Code)))
                dependent.SetCheckedSilently(false);
        }
        _rolePermissionsDirty = true;
        OnPropertyChanged(nameof(CanSaveRolePermissions));
    }

    private void RebuildRoleFilters(IReadOnlyList<Role> roles)
    {
        var current = SelectedRoleFilter;
        RoleFilterOptions.Clear();
        RoleFilterOptions.Add("全部角色");
        foreach (var role in roles) RoleFilterOptions.Add(RoleTextLocalizer.DisplayName(role));
        SelectedRoleFilter = RoleFilterOptions.Contains(current) ? current : "全部角色";
    }

    private void ApplyUserFilters()
    {
        var keyword = Keyword.Trim();
        VisibleUsers.Clear();
        foreach (var user in _allUsers.Where(user =>
                     (keyword.Length == 0 || user.LoginName.Contains(keyword, StringComparison.OrdinalIgnoreCase) || user.DisplayName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                     && (SelectedRoleFilter == "全部角色" || user.RoleName == SelectedRoleFilter)
                     && (SelectedStatusFilter == "全部状态" || user.StatusText == SelectedStatusFilter)))
            VisibleUsers.Add(user);
        OnPropertyChanged(nameof(HasVisibleUsers));
    }

    private static UserRow ToUserRow(Core.Domain.Identity.User user, Role? role, int currentUserId)
    {
        var isLocked = user.LockedUntilUtc is { } locked && locked > DateTime.UtcNow;
        var status = !user.IsEnabled ? "停用" : isLocked ? "锁定" : user.MustChangePassword ? "待改密" : "正常";
        return new UserRow
        {
            Id = user.Id,
            LoginName = user.LoginName,
            DisplayName = user.DisplayName,
            RoleId = user.RoleId,
            RoleName = role is null
                ? $"角色 {user.RoleId}"
                : RoleTextLocalizer.DisplayName(role),
            IsEnabled = user.IsEnabled,
            MustChangePassword = user.MustChangePassword,
            IsLocked = isLocked,
            IsCurrentUser = user.Id == currentUserId,
            IsProtectedAdmin = string.Equals(user.LoginName, "admin", StringComparison.OrdinalIgnoreCase)
                && string.Equals(role?.SystemKey, "Administrator", StringComparison.Ordinal),
            StatusText = status
        };
    }
}
