namespace XXX.TestBench.Core.Application;

public enum BenchState
{
    Starting,
    Unauthenticated,
    Ready,
    Manual,
    AutomaticRunning,
    Stopping,
    Completed,
    Faulted
}

public enum TestMode
{
    Manual,
    Automatic
}

public enum StopReason
{
    OperatorRequested,
    CancellationRequested,
    SafetyInputFalse,
    SafetyInputInvalid,
    GatewayDisconnected,
    EmergencyStop,
    TestFailed
}

public enum CoreQuality
{
    Unknown,
    Good,
    Uncertain,
    Bad
}

public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public enum CoreDiagnosticCode
{
    None,
    InvalidTransition,
    ConfigurationUnavailable,
    AuthenticationFailed,
    ProductNotSelected,
    GatewayNotReady,
    SafetySignalInvalid,
    SafetyInterlockOpen,
    TestModeUnavailable,
    ManualModeActive,
    TestAlreadyRunning,
    StopRequested,
    SafeCleanupFailed,
    AutomaticTestFailed,
    FaultRecoveryUnavailable
}

public sealed record CoreDiagnostic(
    CoreDiagnosticCode Code,
    DiagnosticSeverity Severity,
    string Message);

public sealed record CoreSignal<T>(
    T? Value,
    CoreQuality Quality,
    DateTimeOffset Timestamp,
    long ConnectionGeneration)
{
    public bool IsGood => Quality == CoreQuality.Good;
}

public sealed record GatewayReadinessSnapshot(
    bool SessionValid,
    bool CriticalPointsGood,
    CoreSignal<bool?> DI00,
    CoreSignal<TestMode?> Test00,
    long ConnectionGeneration,
    bool IsSimulated)
{
    public static GatewayReadinessSnapshot Unknown { get; } = new(
        SessionValid: false,
        CriticalPointsGood: false,
        new CoreSignal<bool?>(null, CoreQuality.Unknown, DateTimeOffset.UtcNow, 0),
        new CoreSignal<TestMode?>(null, CoreQuality.Unknown, DateTimeOffset.UtcNow, 0),
        ConnectionGeneration: 0,
        IsSimulated: false);

    public bool IsReadyForControl =>
        SessionValid && CriticalPointsGood && DI00.IsGood && Test00.IsGood;
}

public sealed record AutomaticTestContext(
    Guid RunId,
    string ProductId,
    TestMode Mode,
    long ConnectionGeneration,
    DateTimeOffset StartedAt);

public sealed record BenchStateSnapshot(
    BenchState State,
    bool IsAuthenticated,
    string? ProductId,
    GatewayReadinessSnapshot Gateway,
    AutomaticTestContext? ActiveTest,
    StopReason? PendingStopReason);

public sealed record CoreOperationResult(
    bool Succeeded,
    BenchStateSnapshot Snapshot,
    CoreDiagnostic? Diagnostic = null)
{
    public static CoreOperationResult Success(BenchStateSnapshot snapshot) =>
        new(true, snapshot);

    public static CoreOperationResult Failure(
        BenchStateSnapshot snapshot,
        CoreDiagnosticCode code,
        string message,
        DiagnosticSeverity severity = DiagnosticSeverity.Error) =>
        new(false, snapshot, new CoreDiagnostic(code, severity, message));
}
