namespace XXX.TestBench.Core.Domain.Execution;

/// <summary>
/// 台架互斥操作阶段；数值是持久化合同，已经发布后不得重排。
/// </summary>
public enum BenchPhase
{
    Idle = 0,
    Preparing = 1,
    SelfChecking = 2,
    Ready = 3,
    Running = 4,
    Stopping = 5,
    Finalizing = 6,
    Maintaining = 7,
    RecoveryRequired = 8
}
