using XXX.TestBench.Avalonia.ViewModels;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Gateway.Domain;
using XXX.TestBench.Gateway.Infrastructure;

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

var settings = new GatewaySettingsDocument();
await using var runtime = new ReadOnlyGatewayRuntime(
    settings.Gateway,
    new SimulatedS7ReadOnlyTransport(),
    new SimulatedModbusReadOnlyTransport(),
    isSimulated: true);
using var viewModel = new MainWindowViewModel(
    new TestBenchStateMachine(),
    runtime,
    log: null,
    configPath: "offline-test",
    transportMode: "OfflineSimulation");

await viewModel.StartAsync();
Assert(viewModel.StatusText == "待登录", "UI 启动后没有停在待登录状态。");
Assert(viewModel.Points.Count == 5, "UI 没有显示 P2 五点。");
Assert(viewModel.Points.Single(x => x.PointId == "SMART.PLC.AI.MAI00").DisplayValue == "42.5", "UI 没有显示 S7 AI00 仿真值。");
Assert(viewModel.ConnectionText.Contains("仿真", StringComparison.Ordinal), "UI 没有明确显示仿真来源。");
Assert(viewModel.WritesText.Contains("禁用", StringComparison.Ordinal), "UI 没有显示写入禁用状态。");

viewModel.LoginCommand.Execute(null);
Assert(viewModel.StatusText == "就绪", "仿真会话登录后没有进入 Ready。");
viewModel.ProductId = "B11-UI-Test";
viewModel.SelectProductCommand.Execute(null);
Assert(!viewModel.CanStartAutomaticTest, "Test00 未接入时 UI 错误开放自动试验。");
Assert(viewModel.SafetyText.Contains("Test00 尚未接入", StringComparison.Ordinal), "UI 没有解释控制按钮禁用原因。");

Console.WriteLine("PASS avalonia-vm startup=refresh=demo-login=product-selection test00-gated writes=disabled");
