using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace XXX.TestBench.App.ViewModels;

/// <summary>
/// 用户新增/编辑弹窗的提交结果。
/// </summary>
public sealed record UserDialogResult(
    string LoginName,
    string DisplayName,
    int RoleId,
    bool IsEnabled,
    string? TemporaryPassword);

/// <summary>
/// 用户弹窗中的角色选项。
/// </summary>
public sealed record RoleOption(int Id, string Name);

/// <summary>
/// 用户新增/编辑表单。
/// </summary>
public sealed partial class UserManagementDialogViewModel : ObservableObject
{
    public UserManagementDialogViewModel(bool isEdit, IEnumerable<RoleOption> roles, UserRow? current)
    {
        IsEdit = isEdit;
        DialogTitle = isEdit ? "编辑用户" : "新增用户";
        DialogSubtitle = isEdit
            ? "登录账号不可修改；角色修改将在下次登录后生效"
            : "设置初始密码后，用户首次登录必须修改密码";
        foreach (var role in roles) RoleOptions.Add(role);

        if (current is null)
        {
            IsEnabled = true;
            CanChangeStatus = true;
            return;
        }

        LoginName = current.LoginName;
        DisplayName = current.DisplayName;
        SelectedRole = RoleOptions.FirstOrDefault(role => role.Id == current.RoleId);
        IsEnabled = current.IsEnabled;
        CanChangeStatus = !current.IsCurrentUser && !current.IsProtectedAdmin;
    }

    public bool IsEdit { get; }
    public string DialogTitle { get; }
    public string DialogSubtitle { get; }
    public bool CanEditLoginName => !IsEdit;
    public bool ShowPasswordFields => !IsEdit;
    public bool CanChangeStatus { get; }
    public ObservableCollection<RoleOption> RoleOptions { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _loginName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _displayName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private RoleOption? _selectedRole;

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _temporaryPassword = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _confirmPassword = string.Empty;

    private string _validationMessage = string.Empty;
    public string ValidationMessage { get => _validationMessage; private set => SetProperty(ref _validationMessage, value); }

    public bool CanSave => !string.IsNullOrWhiteSpace(LoginName)
        && !string.IsNullOrWhiteSpace(DisplayName)
        && SelectedRole is not null
        && (IsEdit || (!string.IsNullOrWhiteSpace(TemporaryPassword) && TemporaryPassword == ConfirmPassword));

    public bool TryBuildResult(out UserDialogResult result)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(LoginName)) errors.Add("登录账号不能为空");
        if (string.IsNullOrWhiteSpace(DisplayName)) errors.Add("显示名称不能为空");
        if (SelectedRole is null) errors.Add("请选择角色");
        if (!IsEdit && string.IsNullOrWhiteSpace(TemporaryPassword)) errors.Add("初始密码不能为空");
        if (!IsEdit && TemporaryPassword != ConfirmPassword) errors.Add("两次密码输入不一致");
        if (errors.Count > 0)
        {
            ValidationMessage = string.Join("；", errors);
            result = null!;
            return false;
        }

        ValidationMessage = string.Empty;
        result = new UserDialogResult(
            LoginName.Trim(), DisplayName.Trim(), SelectedRole!.Id, IsEnabled,
            IsEdit ? null : TemporaryPassword);
        return true;
    }
}

/// <summary>
/// 角色名称新增/编辑表单。
/// </summary>
public sealed partial class RoleNameDialogViewModel : ObservableObject
{
    public RoleNameDialogViewModel(bool isEdit, string? name = null)
    {
        IsEdit = isEdit;
        DialogTitle = isEdit ? "修改角色名称" : "新增自定义角色";
        DialogSubtitle = isEdit ? "角色权限保持不变" : "创建后可在右侧权限矩阵中配置权限";
        Name = name ?? string.Empty;
    }

    public bool IsEdit { get; }
    public string DialogTitle { get; }
    public string DialogSubtitle { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _name = string.Empty;

    private string _validationMessage = string.Empty;
    public string ValidationMessage { get => _validationMessage; private set => SetProperty(ref _validationMessage, value); }
    public bool CanSave => !string.IsNullOrWhiteSpace(Name);

    public bool TryBuildResult(out string name)
    {
        name = Name.Trim();
        if (name.Length == 0)
        {
            ValidationMessage = "角色名称不能为空";
            return false;
        }
        ValidationMessage = string.Empty;
        return true;
    }
}

/// <summary>
/// 重置密码表单。
/// </summary>
public sealed partial class ResetPasswordDialogViewModel : ObservableObject
{
    public ResetPasswordDialogViewModel(string userName)
    {
        UserName = userName;
        DialogTitle = "重置用户密码";
        DialogSubtitle = $"为“{userName}”设置临时密码，用户下次登录必须修改";
    }

    public string UserName { get; }
    public string DialogTitle { get; }
    public string DialogSubtitle { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _temporaryPassword = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _confirmPassword = string.Empty;

    private string _validationMessage = string.Empty;
    public string ValidationMessage { get => _validationMessage; private set => SetProperty(ref _validationMessage, value); }
    public bool CanSave => !string.IsNullOrWhiteSpace(TemporaryPassword) && TemporaryPassword == ConfirmPassword;

    public bool TryBuildResult(out string password)
    {
        password = TemporaryPassword;
        if (string.IsNullOrWhiteSpace(password))
        {
            ValidationMessage = "临时密码不能为空";
            return false;
        }
        if (password != ConfirmPassword)
        {
            ValidationMessage = "两次密码输入不一致";
            return false;
        }
        ValidationMessage = string.Empty;
        return true;
    }
}

/// <summary>
/// 通用确认弹窗模型。
/// </summary>
public sealed class ConfirmDialogViewModel
{
    public ConfirmDialogViewModel(string title, string message, string confirmText = "确认")
    {
        Title = title;
        Message = message;
        ConfirmText = confirmText;
    }

    public string Title { get; }
    public string Message { get; }
    public string ConfirmText { get; }
}
