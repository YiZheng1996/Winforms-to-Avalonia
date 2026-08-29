using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using XXX.TestBench.Avalonia;
using XXX.TestBench.Avalonia.ViewModels;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Gateway.Domain;
using XXX.TestBench.Gateway.Infrastructure;

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

using var session = HeadlessUnitTestSession.StartNew(typeof(XXX.TestBench.Avalonia.App));
await session.Dispatch(async () =>
{
    var settings = new GatewaySettingsDocument();
    var runtime = new ReadOnlyGatewayRuntime(
        settings.Gateway,
        new SimulatedS7ReadOnlyTransport(),
        new SimulatedModbusReadOnlyTransport(),
        isSimulated: true);
    try
    {
        using var viewModel = new MainWindowViewModel(
            new TestBenchStateMachine(),
            runtime,
            log: null,
            configPath: "headless-test",
            transportMode: "OfflineSimulation");

        await viewModel.StartAsync();
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var input = window.FindControl<TextBox>("ProductIdInput");
        var login = window.FindControl<Button>("LoginButton");
        var refresh = window.FindControl<Button>("RefreshButton");
        Assert(input is not null && login is not null && refresh is not null, "主窗口关键控件未加载。");
        Assert(window.MinWidth == 960 && window.MinHeight == 560, "主窗口最小尺寸合同错误。");
        Assert(input!.Text == "B11-Offline-Demo", "产品输入框初始绑定错误。");
        Assert(AutomationProperties.GetName(input) == "产品标识输入框", "产品输入框无可访问名称。");
        Assert(login!.IsEnabled, "离线仿真登录按钮未启用。");
        Assert(!refresh!.IsEnabled, "运行中的刷新命令不应保持可重入。");
        Assert(window.GetVisualDescendants().OfType<TextBlock>().Any(x => x.Text == "1"), "连接代次没有通过实际绑定显示。");

        input.Text = "B11-Headless";
        Assert(viewModel.ProductId == "B11-Headless", "产品输入框双向绑定未生效。");
        login.Command?.Execute(null);
        Assert(viewModel.StatusText == "就绪", "Headless 登录命令未驱动 Core 状态。");
        Assert(!viewModel.CanStartAutomaticTest, "Test00 未接入时 Headless UI 错误开放自动试验。");

        window.Close();
    }
    finally
    {
        await runtime.DisposeAsync();
    }
}, CancellationToken.None);

Console.WriteLine("PASS avalonia-headless xaml=loaded binding=two-way command=core-gated layout=window");
