namespace XXX.TestBench.Core.Application;

/// <summary>
/// UI-independent application state and safety gate for the test bench.
/// It does not write devices, show dialogs, access files, or reference a UI framework.
/// </summary>
public sealed class TestBenchStateMachine
{
    private readonly object _sync = new();
    private BenchState _state = BenchState.Starting;
    private bool _authenticated;
    private string? _productId;
    private GatewayReadinessSnapshot _gateway = GatewayReadinessSnapshot.Unknown;
    private AutomaticTestContext? _activeTest;
    private StopReason? _pendingStopReason;

    public BenchStateSnapshot Snapshot
    {
        get
        {
            lock (_sync)
                return CreateSnapshot();
        }
    }

    public CoreOperationResult Initialize(bool configurationAvailable, bool databaseAvailable)
    {
        lock (_sync)
        {
            if (_state != BenchState.Starting)
                return Reject(CoreDiagnosticCode.InvalidTransition, "应用已经完成初始化。");

            if (!configurationAvailable || !databaseAvailable)
            {
                _state = BenchState.Faulted;
                return Reject(CoreDiagnosticCode.ConfigurationUnavailable, "配置或数据库不可用。");
            }

            _state = BenchState.Unauthenticated;
            return CoreOperationResult.Success(CreateSnapshot());
        }
    }

    public CoreOperationResult Login(bool authenticated)
    {
        lock (_sync)
        {
            if (_state != BenchState.Unauthenticated)
                return Reject(CoreDiagnosticCode.InvalidTransition, "当前状态不允许登录。");

            if (!authenticated)
                return Reject(CoreDiagnosticCode.AuthenticationFailed, "用户认证失败。");

            _authenticated = true;
            _state = BenchState.Ready;
            return CoreOperationResult.Success(CreateSnapshot());
        }
    }

    public CoreOperationResult UpdateGateway(GatewayReadinessSnapshot gateway)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        lock (_sync)
        {
            _gateway = gateway;

            if (_state == BenchState.AutomaticRunning && !CanContinueAutomaticTest())
            {
                _pendingStopReason = ResolveSafetyStopReason();
                _state = BenchState.Stopping;
                return new CoreOperationResult(
                    true,
                    CreateSnapshot(),
                    new CoreDiagnostic(
                        CoreDiagnosticCode.StopRequested,
                        DiagnosticSeverity.Warning,
                        $"自动试验已请求安全停止：{_pendingStopReason}。"));
            }

            return CoreOperationResult.Success(CreateSnapshot());
        }
    }

    public CoreOperationResult SelectProduct(string productId)
    {
        lock (_sync)
        {
            if (_state is not (BenchState.Ready or BenchState.Manual))
                return Reject(CoreDiagnosticCode.InvalidTransition, "当前状态不允许选择产品。");
            if (string.IsNullOrWhiteSpace(productId) || productId.Length > 128)
                return Reject(CoreDiagnosticCode.ProductNotSelected, "产品标识不能为空且长度不能超过 128 个字符。");

            _productId = productId.Trim();
            return CoreOperationResult.Success(CreateSnapshot());
        }
    }

    public CoreOperationResult EnterManualMode()
    {
        lock (_sync)
        {
            if (_state != BenchState.Ready)
                return Reject(CoreDiagnosticCode.InvalidTransition, "当前状态不允许进入手动模式。");
            if (!_gateway.IsReadyForControl)
                return Reject(CoreDiagnosticCode.GatewayNotReady, "Gateway 关键点尚未全部达到 Good。");
            if (_gateway.Test00.Value != TestMode.Manual)
                return Reject(CoreDiagnosticCode.TestModeUnavailable, "PLC Test00 当前不是手动模式。");

            _state = BenchState.Manual;
            return CoreOperationResult.Success(CreateSnapshot());
        }
    }

    public CoreOperationResult LeaveManualMode()
    {
        lock (_sync)
        {
            if (_state != BenchState.Manual)
                return Reject(CoreDiagnosticCode.InvalidTransition, "当前状态不在手动模式。");

            _state = BenchState.Ready;
            return CoreOperationResult.Success(CreateSnapshot());
        }
    }

    public CoreOperationResult StartAutomaticTest()
    {
        lock (_sync)
        {
            if (_state == BenchState.AutomaticRunning)
                return Reject(CoreDiagnosticCode.TestAlreadyRunning, "自动试验已经在运行。");
            if (_state != BenchState.Ready)
                return Reject(CoreDiagnosticCode.InvalidTransition, "当前状态不允许启动自动试验。");
            if (string.IsNullOrWhiteSpace(_productId))
                return Reject(CoreDiagnosticCode.ProductNotSelected, "启动自动试验前必须选择产品。");
            if (!_gateway.SessionValid || !_gateway.CriticalPointsGood)
                return Reject(CoreDiagnosticCode.GatewayNotReady, "Gateway 会话或关键点质量不可用。");
            if (!_gateway.DI00.IsGood)
                return Reject(CoreDiagnosticCode.SafetySignalInvalid, "DI00 质量不是 Good，禁止启动自动试验。");
            if (_gateway.DI00.Value != true)
                return Reject(CoreDiagnosticCode.SafetyInterlockOpen, "DI00 为 false，安全联锁未满足。");
            if (!_gateway.Test00.IsGood)
                return Reject(CoreDiagnosticCode.TestModeUnavailable, "Test00 质量不是 Good，无法确认自动模式。");
            if (_gateway.Test00.Value != TestMode.Automatic)
                return Reject(CoreDiagnosticCode.ManualModeActive, "Test00 当前为手动模式，禁止启动自动试验。");

            _activeTest = new AutomaticTestContext(
                Guid.NewGuid(),
                _productId,
                TestMode.Automatic,
                _gateway.ConnectionGeneration,
                DateTimeOffset.UtcNow);
            _pendingStopReason = null;
            _state = BenchState.AutomaticRunning;
            return CoreOperationResult.Success(CreateSnapshot());
        }
    }

    public CoreOperationResult RequestStop(StopReason reason)
    {
        lock (_sync)
        {
            if (_state != BenchState.AutomaticRunning)
                return Reject(CoreDiagnosticCode.InvalidTransition, "当前没有运行中的自动试验。");
            if (!Enum.IsDefined(reason))
                return Reject(CoreDiagnosticCode.StopRequested, "停止原因无效。");

            _pendingStopReason = reason;
            _state = BenchState.Stopping;
            return new CoreOperationResult(
                true,
                CreateSnapshot(),
                new CoreDiagnostic(CoreDiagnosticCode.StopRequested, DiagnosticSeverity.Warning, $"已请求停止：{reason}。"));
        }
    }

    public CoreOperationResult CompleteStopping(bool safeCleanupSucceeded)
    {
        lock (_sync)
        {
            if (_state != BenchState.Stopping)
                return Reject(CoreDiagnosticCode.InvalidTransition, "当前没有等待收尾的停止流程。");

            if (!safeCleanupSucceeded)
            {
                _state = BenchState.Faulted;
                return Reject(CoreDiagnosticCode.SafeCleanupFailed, "安全收尾失败，系统进入故障状态。");
            }

            _activeTest = null;
            _pendingStopReason = null;
            _state = BenchState.Ready;
            return CoreOperationResult.Success(CreateSnapshot());
        }
    }

    public CoreOperationResult CompleteAutomaticTest(bool succeeded)
    {
        lock (_sync)
        {
            if (_state != BenchState.AutomaticRunning)
                return Reject(CoreDiagnosticCode.InvalidTransition, "当前没有运行中的自动试验。");

            _state = succeeded ? BenchState.Completed : BenchState.Faulted;
            var diagnostic = succeeded
                ? null
                : new CoreDiagnostic(CoreDiagnosticCode.AutomaticTestFailed, DiagnosticSeverity.Error, "自动试验失败。");
            return new CoreOperationResult(succeeded, CreateSnapshot(), diagnostic);
        }
    }

    public CoreOperationResult ReturnToReady()
    {
        lock (_sync)
        {
            if (_state != BenchState.Completed)
                return Reject(CoreDiagnosticCode.InvalidTransition, "只有已完成状态可以返回 Ready。");
            if (!_authenticated || !_gateway.SessionValid || !_gateway.CriticalPointsGood)
                return Reject(CoreDiagnosticCode.GatewayNotReady, "Gateway 尚未恢复，不能返回 Ready。");

            _activeTest = null;
            _state = BenchState.Ready;
            return CoreOperationResult.Success(CreateSnapshot());
        }
    }

    public CoreOperationResult RecoverFault()
    {
        lock (_sync)
        {
            if (_state != BenchState.Faulted)
                return Reject(CoreDiagnosticCode.InvalidTransition, "当前不是故障状态。");
            if (!_authenticated || !_gateway.SessionValid || !_gateway.CriticalPointsGood)
                return Reject(CoreDiagnosticCode.FaultRecoveryUnavailable, "认证或 Gateway 关键点尚未恢复。");

            _activeTest = null;
            _pendingStopReason = null;
            _state = BenchState.Ready;
            return CoreOperationResult.Success(CreateSnapshot());
        }
    }

    private bool CanContinueAutomaticTest() =>
        _gateway.SessionValid &&
        _gateway.CriticalPointsGood &&
        _gateway.DI00.IsGood &&
        _gateway.DI00.Value == true &&
        _gateway.Test00.IsGood &&
        _gateway.Test00.Value == TestMode.Automatic;

    private StopReason ResolveSafetyStopReason()
    {
        if (!_gateway.SessionValid || !_gateway.CriticalPointsGood)
            return StopReason.GatewayDisconnected;
        if (!_gateway.DI00.IsGood)
            return StopReason.SafetyInputInvalid;
        if (_gateway.DI00.Value != true)
            return StopReason.SafetyInputFalse;
        return StopReason.GatewayDisconnected;
    }

    private CoreOperationResult Reject(CoreDiagnosticCode code, string message)
    {
        return CoreOperationResult.Failure(CreateSnapshot(), code, message);
    }

    private BenchStateSnapshot CreateSnapshot() => new(
        _state,
        _authenticated,
        _productId,
        _gateway,
        _activeTest,
        _pendingStopReason);
}
