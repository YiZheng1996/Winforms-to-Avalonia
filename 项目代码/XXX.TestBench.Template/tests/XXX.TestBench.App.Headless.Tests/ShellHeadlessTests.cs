using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using XXX.TestBench.App;
using XXX.TestBench.App.ViewModels;
using XXX.TestBench.Core.Domain.Devices;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(App))]

namespace XXX.TestBench.App.Headless.Tests;

public class ShellHeadlessTests
{
    [AvaloniaFact]
    public void Shell_Simulation_ShowsModeBadge_AndNoFaultBanner()
    {
        var vm = new ShellViewModel("测试系统", "0.1.0", DeviceMode.Simulation, "data");
        var window = new MainWindow { DataContext = vm };
        window.Show();

        var modeText = window.FindControl<TextBlock>("ModeTextBlock");
        Assert.Equal("Simulation（仿真）", modeText?.Text);

        var banner = window.FindControl<Border>("FaultBanner");
        Assert.False(banner?.IsVisible);

        window.Close();
    }

    [AvaloniaFact]
    public void Shell_Faulted_ShowsFaultBanner()
    {
        var vm = new ShellViewModel("测试系统", "0.1.0", DeviceMode.Simulation, "data", isFaulted: true, faultMessage: "配置无效");
        var window = new MainWindow { DataContext = vm };
        window.Show();

        var banner = window.FindControl<Border>("FaultBanner");
        Assert.True(banner?.IsVisible);

        var faultText = window.FindControl<TextBlock>("ModeTextBlock");
        Assert.NotNull(faultText);

        window.Close();
    }
}
