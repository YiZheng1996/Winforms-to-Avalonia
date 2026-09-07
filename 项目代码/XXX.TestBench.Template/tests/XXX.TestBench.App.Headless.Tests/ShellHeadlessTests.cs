using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using XXX.TestBench.App;
using XXX.TestBench.App.Composition;
using XXX.TestBench.App.ViewModels;
using XXX.TestBench.App.Views;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(XXX.TestBench.App.Headless.Tests.HeadlessTestAppBuilder))]

namespace XXX.TestBench.App.Headless.Tests;

public static class HeadlessTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public class ShellHeadlessTests
{
    private static (string ConfigRoot, string DataRoot) CreateTempConfig()
    {
        var root = Path.Combine(Path.GetTempPath(), "testbench-headless-" + Guid.NewGuid().ToString("N")[..8]);
        var configRoot = Path.Combine(root, "config");
        var dataRoot = Path.Combine(root, "data");
        Directory.CreateDirectory(configRoot);
        Directory.CreateDirectory(dataRoot);
        File.WriteAllText(Path.Combine(configRoot, "app.json"), """
        {
          "schemaVersion": 1,
          "systemName": "XXX试验台",
          "brand": "XXX.TestBench.Template",
          "language": "zh-CN",
          "modules": { "processMonitor": true, "deviceCalibration": true },
          "defaultPaths": { "database": "testbench.db", "reportOutput": "reports", "reportTemplates": "assets/report-templates" }
        }
        """);
        File.WriteAllText(Path.Combine(configRoot, "device.json"), """
        {
          "schemaVersion": 1,
          "deviceMode": "Simulation",
          "pollIntervalMs": 500,
          "timeoutMs": 1000,
          "devices": [ { "name": "SampleDevice", "protocol": "Simulation", "address": "sim://sample", "enabled": true } ]
        }
        """);
        File.WriteAllText(Path.Combine(configRoot, "points.json"), """
        {
          "schemaVersion": 1,
          "points": [
            { "code": "AI_Pressure", "protocol": "Simulation", "address": "sim.pressure", "dataType": "Decimal", "unit": "MPa", "isWritable": false, "riskLevel": "Normal" }
          ]
        }
        """);
        File.WriteAllText(Path.Combine(configRoot, "simulation.json"), """
        { "schemaVersion": 1, "initialValues": { "sim.pressure": 0.0 }, "changeRules": [], "faultInjectionScenarios": [] }
        """);
        return (configRoot, dataRoot);
    }

    /// <summary>
    /// 单个会话内完成外壳端到端验证：故障横幅、模式徽标、登录与导航。
    /// 无头测试框架在该环境存在挂起问题（启动守卫已缓解但仍偶发），因此把多个断言合并为一个用例。
    /// </summary>
    [AvaloniaFact]
    public async Task Shell_EndToEnd_FaultBanner_ModeBadge_LoginAndNavigate()
    {
        var faulted = new ShellViewModel(null, isFaulted: true, faultMessage: "配置无效");

        var faultWindow = new MainWindow { DataContext = faulted };
        faultWindow.Show();
        Assert.True(faultWindow.FindControl<Border>("FaultBanner")?.IsVisible);
        faultWindow.Close();

        var (configRoot, dataRoot) = CreateTempConfig();

        var composition = AppComposition.Create(configRoot, dataRoot);

        Assert.Null(composition.StartupError);
        var shell = composition.Shell!;

        var loginWindow = new LoginWindow(shell);
        loginWindow.Show();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);
        var loginView = loginWindow.GetVisualDescendants().OfType<LoginView>().Single();
        var loginNameBox = loginView.FindControl<TextBox>("LoginNameBox");
        Assert.NotNull(loginNameBox);
        Assert.Same(loginNameBox, loginWindow.FocusManager?.GetFocusedElement());
        loginWindow.Close();

        var window = new MainWindow { DataContext = shell };
        window.Show();

        var modeText = window.FindControl<TextBlock>("ModeTextBlock");
        Assert.Equal("仿真模式", modeText?.Text);
        Assert.False(window.FindControl<Border>("FaultBanner")?.IsVisible);

        shell.LoginName = "admin";

        shell.Password = "admin123";
        await shell.LoginAsync();
        Assert.True(shell.MustChangePassword);
        shell.NewPassword = "newpass123";
        shell.ConfirmPassword = "newpass123";
        await shell.ChangePasswordAsync();

        Assert.True(shell.IsAuthenticated);

        Assert.Equal(9, shell.NavItems.Count);
        Assert.NotNull(shell.CurrentPage);
        Assert.Equal("运行总览", shell.CurrentPage!.Title);

        var devicePointNav = shell.NavItems.Single(item => item.Title == "设备点位");
        await shell.NavigateCommand.ExecuteAsync(devicePointNav);
        window.UpdateLayout();
        var devicePointViewModel = Assert.IsType<DevicePointManagementViewModel>(devicePointNav.Page);
        Assert.Single(devicePointViewModel.TreeNodes);
        Assert.Equal("设备与通道", devicePointViewModel.TreeNodes[0].Code);
        var devicePointView = window.GetVisualDescendants().OfType<DevicePointManagementView>().Single();
        Assert.NotNull(devicePointView.GetVisualDescendants().OfType<TreeView>().SingleOrDefault());
        await shell.NavigateCommand.ExecuteAsync(shell.NavItems[0]);
        window.UpdateLayout();

        VerifyResponsiveLayout(window, 1440, 900, "main-1440x900.png");

        VerifyResponsiveLayout(window, 1680, 945, "main-1680x945.png");
        VerifyResponsiveLayout(window, 1920, 1080, "main-1920x1080.png");
        VerifyResponsiveLayout(window, 1152, 720, "main-1440x900-at-125dpi.png");
        await VerifyStatusBarOnEveryPage(window, shell);

        window.Close();
    }

    private static void VerifyResponsiveLayout(MainWindow window, double width, double height, string captureName)
    {
        window.WindowState = WindowState.Normal;
        window.Width = width;
        window.Height = height;

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        Assert.True(frame.PixelSize.Width > 0);
        Assert.True(frame.PixelSize.Height > 0);

        var shellRoot = window.FindControl<Grid>("ShellRoot");
        var topHeader = window.FindControl<Border>("TopHeader");
        var productBar = window.FindControl<Border>("ProductInfoBar");
        var workspace = window.FindControl<Border>("WorkspaceRegion");
        var bottomBar = window.FindControl<Border>("BottomControlBar");
        var statusBar = window.FindControl<Border>("StatusBar");
        var exitButton = window.FindControl<Button>("ExitButton");
        var overview = window.GetVisualDescendants().OfType<OverviewView>().Single();
        var schematic = overview.FindControl<Border>("PipeSchematicHost");
        var taskPanel = overview.FindControl<Border>("PointPanel");
        var measurementPanel = overview.FindControl<Border>("MeasurementPanel");

        Assert.NotNull(shellRoot);
        Assert.NotNull(topHeader);
        Assert.NotNull(productBar);
        Assert.NotNull(workspace);
        Assert.NotNull(bottomBar);
        Assert.NotNull(statusBar);
        Assert.NotNull(exitButton);
        Assert.NotNull(schematic);
        Assert.NotNull(taskPanel);
        Assert.NotNull(measurementPanel);
        Assert.True(shellRoot.Bounds.Width > 900);
        Assert.InRange(topHeader.Bounds.Height, 76, 80);
        Assert.True(productBar.Bounds.Height >= 68);
        Assert.True(workspace.Bounds.Height > 350);
        Assert.InRange(bottomBar.Bounds.Height, 74, 78);
        Assert.True(statusBar.IsVisible);
        Assert.InRange(statusBar.Bounds.Height, 46, 50);
        Assert.InRange(exitButton.Bounds.Height, 46, 50);
        Assert.True(statusBar.Bounds.Top >= workspace.Bounds.Bottom - 1);
        Assert.True(statusBar.Bounds.Bottom >= shellRoot.Bounds.Height - 1);
        Assert.True(schematic.Bounds.Width > 300);
        Assert.True(schematic.Bounds.Height > 300);
        Assert.InRange(taskPanel.Bounds.Width, 190, 249);
        Assert.InRange(measurementPanel.Bounds.Width, 210, 281);

        var captureDirectory = Environment.GetEnvironmentVariable("TESTBENCH_UI_CAPTURE_DIR");
        if (!string.IsNullOrWhiteSpace(captureDirectory))
        {
            Directory.CreateDirectory(captureDirectory);
            frame.Save(Path.Combine(captureDirectory, captureName));
        }
    }

    private static async Task VerifyStatusBarOnEveryPage(MainWindow window, ShellViewModel shell)
    {
        var shellRoot = window.FindControl<Grid>("ShellRoot");
        var workspace = window.FindControl<Border>("WorkspaceRegion");
        var statusBar = window.FindControl<Border>("StatusBar");

        Assert.NotNull(shellRoot);
        Assert.NotNull(workspace);
        Assert.NotNull(statusBar);

        foreach (var item in shell.NavItems)
        {
            await shell.NavigateCommand.ExecuteAsync(item);
            window.UpdateLayout();

            Assert.True(statusBar.IsVisible, $"状态条在“{item.DisplayTitle}”页面不可见。");
            Assert.True(statusBar.Bounds.Top >= workspace.Bounds.Bottom - 1,
                $"状态条未位于“{item.DisplayTitle}”页面底部。");
            Assert.True(statusBar.Bounds.Bottom >= shellRoot.Bounds.Height - 1,
                $"状态条未贴合“{item.DisplayTitle}”页面底边。");
        }

        await shell.NavigateCommand.ExecuteAsync(shell.NavItems[0]);
        window.UpdateLayout();
    }

}
