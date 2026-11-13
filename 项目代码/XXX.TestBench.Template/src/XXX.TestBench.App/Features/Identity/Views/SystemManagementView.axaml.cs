using Avalonia.Controls;
using Avalonia.Interactivity;
using XXX.TestBench.App.ViewModels;

namespace XXX.TestBench.App.Views;

/// <summary>
/// 系统管理页面：负责打开用户、角色和危险操作弹窗。
/// </summary>
public partial class SystemManagementView : UserControl
{
    public SystemManagementView() => InitializeComponent();

    private async void OnAddUserClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SystemManagementViewModel viewModel || GetOwner() is not { } owner)
            return;
        var dialog = new UserManagementDialogWindow
        {
            DataContext = new UserManagementDialogViewModel(
                false,
                viewModel.Roles.Select(role => new RoleOption(role.Id, role.DisplayName)),
                null)
        };
        var result = await ShowDialogAsync<UserDialogResult>(owner, dialog);
        if (result is not null)
            await viewModel.CreateUserFromDialogAsync(result);
    }

    private async void OnEditUserClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SystemManagementViewModel viewModel
            || viewModel.SelectedUser is not { } user
            || GetOwner() is not { } owner)
            return;
        var dialog = new UserManagementDialogWindow
        {
            DataContext = new UserManagementDialogViewModel(
                true,
                viewModel.Roles.Select(role => new RoleOption(role.Id, role.DisplayName)),
                user)
        };
        var result = await ShowDialogAsync<UserDialogResult>(owner, dialog);
        if (result is not null)
            await viewModel.UpdateUserFromDialogAsync(user.Id, result);
    }

    private async void OnToggleUserClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SystemManagementViewModel viewModel
            || viewModel.SelectedUser is not { CanToggle: true } user
            || await ConfirmAsync($"{(user.IsEnabled ? "停用" : "启用")}用户", $"确定要{(user.IsEnabled ? "停用" : "启用")}用户“{user.LoginName}”吗？", user.IsEnabled ? "确认停用" : "确认启用") is not true)
            return;
        await viewModel.ToggleSelectedUserAsync();
    }

    private async void OnUnlockUserClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SystemManagementViewModel viewModel || viewModel.SelectedUser is null)
            return;
        await viewModel.UnlockSelectedUserAsync();
    }

    private async void OnResetPasswordClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SystemManagementViewModel viewModel
            || viewModel.SelectedUser is not { CanResetPassword: true } user
            || GetOwner() is not { } owner)
            return;
        var dialog = new ResetPasswordDialogWindow
        {
            DataContext = new ResetPasswordDialogViewModel(user.LoginName)
        };
        var result = await ShowDialogAsync<string>(owner, dialog);
        if (result is not null)
            await viewModel.ResetPasswordFromDialogAsync(user.Id, result);
    }

    private async void OnAddRoleClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SystemManagementViewModel viewModel || GetOwner() is not { } owner)
            return;
        var dialog = new RoleNameDialogWindow { DataContext = new RoleNameDialogViewModel(false) };
        var result = await ShowDialogAsync<string>(owner, dialog);
        if (result is not null)
            await viewModel.CreateRoleFromDialogAsync(result);
    }

    private async void OnRenameRoleClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SystemManagementViewModel viewModel
            || viewModel.SelectedRole is not { CanRename: true } role
            || GetOwner() is not { } owner)
            return;
        var dialog = new RoleNameDialogWindow { DataContext = new RoleNameDialogViewModel(true, role.Name) };
        var result = await ShowDialogAsync<string>(owner, dialog);
        if (result is not null)
            await viewModel.RenameRoleFromDialogAsync(result);
    }

    private async void OnDeleteRoleClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SystemManagementViewModel viewModel
            || viewModel.SelectedRole is not { CanDelete: true } role
            || await ConfirmAsync("删除角色", $"角色“{role.Name}”当前没有关联用户，确定删除吗？", "确认删除") is not true)
            return;
        await viewModel.DeleteSelectedRoleAsync();
    }

    private Window? GetOwner() => TopLevel.GetTopLevel(this) as Window;

    private async Task<bool?> ConfirmAsync(string title, string message, string confirmText)
    {
        if (GetOwner() is not { } owner) return false;
        var dialog = new ConfirmDialogWindow
        {
            DataContext = new ConfirmDialogViewModel(title, message, confirmText)
        };
        return await ShowDialogAsync<bool>(owner, dialog);
    }

    private static async Task<TResult?> ShowDialogAsync<TResult>(Window owner, Window dialog)
    {
        if (owner is not MainWindow mainWindow)
            throw new InvalidOperationException("SystemManagementView 必须由 MainWindow 承载，才能显示弹窗遮罩。");
        return await mainWindow.ShowDialogWithOverlayAsync<TResult>(dialog);
    }

}
