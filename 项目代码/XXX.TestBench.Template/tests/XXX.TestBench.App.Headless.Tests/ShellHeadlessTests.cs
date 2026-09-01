using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using XXX.TestBench.App;
using XXX.TestBench.App.Composition;
using XXX.TestBench.App.ViewModels;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(App))]

namespace XXX.TestBench.App.Headless.Tests;

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
          "systemName": "测试系统",
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

    /// <summary>单个会话内完成外壳端到端 Headless 验证：故障横幅、模式徽标、登录与导航（避免多测试会话隔离问题）。</summary>
    [AvaloniaFact]
    public async Task Shell_EndToEnd_FaultBanner_ModeBadge_LoginAndNavigate()
    {
        // 1) 故障外壳显示诊断横幅
        var faulted = new ShellViewModel(null, isFaulted: true, faultMessage: "配置无效");
        var faultWindow = new MainWindow { DataContext = faulted };
        faultWindow.Show();
        Assert.True(faultWindow.FindControl<Border>("FaultBanner")?.IsVisible);
        faultWindow.Close();

        // 2) 组合根（Simulation）模式徽标与登录导航
        var (configRoot, dataRoot) = CreateTempConfig();
        var composition = AppComposition.Create(configRoot, dataRoot);
        Assert.Null(composition.StartupError);
        var shell = composition.Shell!;
        var window = new MainWindow { DataContext = shell };
        window.Show();

        var modeText = window.FindControl<TextBlock>("ModeTextBlock");
        Assert.Equal("Simulation（仿真）", modeText?.Text);
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
        window.Close();
    }
}
