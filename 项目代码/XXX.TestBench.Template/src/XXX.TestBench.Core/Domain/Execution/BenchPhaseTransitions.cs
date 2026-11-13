namespace XXX.TestBench.Core.Domain.Execution;

/// <summary>
/// 持久操作状态允许的阶段转移。Idle 由不存在操作标记表示。
/// </summary>
public static class BenchPhaseTransitions
{
    public static bool CanTransition(BenchPhase current, BenchPhase next) => current switch
    {
        BenchPhase.Preparing => next is BenchPhase.SelfChecking or BenchPhase.Stopping or BenchPhase.RecoveryRequired,
        BenchPhase.SelfChecking => next is BenchPhase.Ready or BenchPhase.Preparing or BenchPhase.Stopping or BenchPhase.RecoveryRequired,
        BenchPhase.Ready => next is BenchPhase.Running or BenchPhase.Preparing or BenchPhase.Stopping or BenchPhase.RecoveryRequired,
        BenchPhase.Running => next is BenchPhase.Stopping or BenchPhase.Finalizing or BenchPhase.RecoveryRequired,
        BenchPhase.Stopping => next is BenchPhase.Finalizing or BenchPhase.RecoveryRequired,
        BenchPhase.Finalizing => next == BenchPhase.RecoveryRequired,
        BenchPhase.Maintaining => next is BenchPhase.Stopping or BenchPhase.RecoveryRequired,
        BenchPhase.RecoveryRequired => next == BenchPhase.RecoveryRequired,
        _ => false
    };

    public static void EnsureTransition(BenchPhase current, BenchPhase next)
    {
        if (!CanTransition(current, next))
            throw new InvalidOperationException($"不允许台架阶段从 {current} 转为 {next}");
    }
}
