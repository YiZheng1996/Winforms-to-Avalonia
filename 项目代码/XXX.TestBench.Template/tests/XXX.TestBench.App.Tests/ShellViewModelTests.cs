using XXX.TestBench.App.ViewModels;
using Xunit;

namespace XXX.TestBench.App.Tests;

public class ShellViewModelTests
{
    [Fact]
    public async Task AdminLogin_ForcesPasswordChange_ThenEntersWithAllPages()
    {
        using var harness = AppTestHarness.Create();
        var shell = new ShellViewModel(harness.Services);

        Assert.False(shell.IsAuthenticated);
        Assert.Equal("未登录", shell.CurrentUserText);

        shell.LoginName = "admin";
        shell.Password = "admin123";
        await shell.LoginAsync();

        Assert.True(shell.MustChangePassword);
        Assert.False(shell.IsAuthenticated);

        shell.NewPassword = "newpass123";
        shell.ConfirmPassword = "newpass123";
        await shell.ChangePasswordAsync();

        Assert.True(shell.IsAuthenticated);
        Assert.False(shell.MustChangePassword);
        Assert.NotEmpty(shell.NavItems);
        Assert.Equal(9, shell.NavItems.Count); // Administrator 全权限（任务管理页已移除；设备点位已独立为菜单页）
        Assert.NotNull(shell.CurrentPage);
        Assert.Equal("运行总览", shell.CurrentPage!.Title);
        Assert.NotEqual("未登录", shell.CurrentUserText);
    }

    [Fact]
    public async Task OperatorLogin_FiltersNavigationByPermission()
    {
        using var harness = AppTestHarness.Create();
        await harness.AddOperatorUserAsync("op1", "op123456");
        var shell = new ShellViewModel(harness.Services);

        shell.LoginName = "op1";
        shell.Password = "op123456";
        await shell.LoginAsync();

        Assert.True(shell.IsAuthenticated);
        Assert.DoesNotContain(shell.NavItems, n => n.Title == "参数管理");
        Assert.DoesNotContain(shell.NavItems, n => n.Title == "系统管理");
        Assert.DoesNotContain(shell.NavItems, n => n.Title == "设备点位");
        Assert.DoesNotContain(shell.NavItems, n => n.Title == "任务管理");
        Assert.Contains(shell.NavItems, n => n.Title == "运行总览");
        Assert.Contains(shell.NavItems, n => n.Title == "试验执行");
        Assert.Equal(4, shell.NavItems.Count);
    }

    [Fact]
    public async Task Logout_ResetsState()
    {
        using var harness = AppTestHarness.Create();
        var shell = new ShellViewModel(harness.Services);
        shell.LoginName = "admin";
        shell.Password = "admin123";
        await shell.LoginAsync();
        shell.NewPassword = "newpass123";
        shell.ConfirmPassword = "newpass123";
        await shell.ChangePasswordAsync();
        Assert.True(shell.IsAuthenticated);

        await shell.LogoutAsync();

        Assert.False(shell.IsAuthenticated);
        Assert.Empty(shell.NavItems);
        Assert.Null(shell.CurrentPage);
        Assert.Equal("未登录", shell.CurrentUserText);
    }

    [Fact]
    public void FaultedShell_ShowsDiagnostic()
    {
        var shell = new ShellViewModel(null, isFaulted: true, faultMessage: "app.json schemaVersion=99 不受支持");

        Assert.True(shell.IsFaulted);
        Assert.Equal("故障", shell.ConnectionStatusText);
        Assert.Contains("schemaVersion", shell.FaultMessage);
    }
}
