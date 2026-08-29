using XXX.TestBench.Core.Application;

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static GatewayReadinessSnapshot Gateway(
    bool di00,
    CoreQuality diQuality = CoreQuality.Good,
    TestMode testMode = TestMode.Automatic,
    CoreQuality testQuality = CoreQuality.Good,
    bool sessionValid = true,
    bool criticalPointsGood = true,
    long generation = 1)
{
    var now = DateTimeOffset.UtcNow;
    return new GatewayReadinessSnapshot(
        sessionValid,
        criticalPointsGood,
        new CoreSignal<bool?>(di00, diQuality, now, generation),
        new CoreSignal<TestMode?>(testMode, testQuality, now, generation),
        generation,
        IsSimulated: true);
}

var machine = new TestBenchStateMachine();
Assert(machine.Snapshot.State == BenchState.Starting, "初始状态错误。");
Assert(machine.Initialize(configurationAvailable: true, databaseAvailable: true).Succeeded, "初始化失败。");
Assert(machine.Login(authenticated: true).Succeeded, "登录失败。");
Assert(machine.SelectProduct("B11").Succeeded, "产品选择失败。");

// Unknown/Bad/false DI00 must be rejected distinctly from a valid true signal.
machine.UpdateGateway(Gateway(true, diQuality: CoreQuality.Unknown));
var unknownStart = machine.StartAutomaticTest();
Assert(!unknownStart.Succeeded && unknownStart.Diagnostic?.Code == CoreDiagnosticCode.SafetySignalInvalid, "DI00 Unknown 未被拒绝。");

machine.UpdateGateway(Gateway(false));
var falseStart = machine.StartAutomaticTest();
Assert(!falseStart.Succeeded && falseStart.Diagnostic?.Code == CoreDiagnosticCode.SafetyInterlockOpen, "DI00 false 未被拒绝。");

machine.UpdateGateway(Gateway(true, testMode: TestMode.Manual));
var manualStart = machine.StartAutomaticTest();
Assert(!manualStart.Succeeded && manualStart.Diagnostic?.Code == CoreDiagnosticCode.ManualModeActive, "Test00 手动模式未被拒绝。");

machine.UpdateGateway(Gateway(true));
var start = machine.StartAutomaticTest();
Assert(start.Succeeded && start.Snapshot.State == BenchState.AutomaticRunning, "自动试验未进入运行状态。");
Assert(start.Snapshot.ActiveTest is not null && start.Snapshot.ActiveTest.ProductId == "B11", "自动试验上下文错误。");

// A safety input falling during a run requests Stopping; it never completes the run.
var safetyStop = machine.UpdateGateway(Gateway(false, generation: 1));
Assert(safetyStop.Succeeded && safetyStop.Snapshot.State == BenchState.Stopping, "运行中 DI00=false 未触发停止。");
Assert(safetyStop.Snapshot.PendingStopReason == StopReason.SafetyInputFalse, "停止原因未区分为 DI00=false。");
Assert(machine.CompleteStopping(safeCleanupSucceeded: true).Snapshot.State == BenchState.Ready, "安全收尾后未返回 Ready。");

machine.UpdateGateway(Gateway(true, generation: 2));
Assert(machine.StartAutomaticTest().Succeeded, "重连后无法重新启动自动试验。");
var disconnected = machine.UpdateGateway(Gateway(true, sessionValid: false, criticalPointsGood: false, generation: 3));
Assert(disconnected.Snapshot.State == BenchState.Stopping, "Gateway 断线未触发停止。");
Assert(disconnected.Snapshot.PendingStopReason == StopReason.GatewayDisconnected, "断线停止原因错误。");
Assert(!machine.CompleteStopping(safeCleanupSucceeded: false).Succeeded && machine.Snapshot.State == BenchState.Faulted, "安全收尾失败未进入 Faulted。");

Console.WriteLine("PASS core-state-machine init=login product=di00-unknown-false test00-manual disconnect stop cleanup-fault");
