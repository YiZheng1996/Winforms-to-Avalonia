namespace XXX.TestBench.Core.Domain.Execution;

/// <summary>
/// 数据库中的单台架持久占用状态。Idle 行可保留，活动操作必须带有稳定 OperationId。
/// </summary>
public sealed record BenchControlState(
    string BenchId,
    string OwnerNodeId,
    Guid? OperationId,
    BenchPhase Phase,
    bool RequiresRecovery,
    IReadOnlyList<string> RecoveryReasons,
    long RowVersion,
    DateTime UpdatedAtUtc,
    string? LastSafeStopJson,
    string? LastEvidenceKey)
{
    private static readonly HashSet<string> KnownRecoveryReasons = new(StringComparer.Ordinal)
    {
        RecoveryReasonCode.SafetyUnconfirmed,
        RecoveryReasonCode.InterruptedOperation,
        RecoveryReasonCode.PersistencePending
    };

    public bool IsIdle => OperationId is null && Phase == BenchPhase.Idle && !RequiresRecovery;

    /// <summary>
    /// 检查持久状态是否符合 v9 台架控制表合同；损坏状态必须阻止设备启用。
    /// </summary>
    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(BenchId) || string.IsNullOrWhiteSpace(OwnerNodeId))
            throw new InvalidDataException("台架控制状态缺少 BenchId 或 OwnerNodeId。");
        if (!Enum.IsDefined(Phase) || RowVersion < 0)
            throw new InvalidDataException("台架控制状态包含未知阶段或无效 RowVersion。");
        if (UpdatedAtUtc.Kind != DateTimeKind.Utc)
            throw new InvalidDataException("台架控制状态的更新时间必须是 UTC。");
        if (RecoveryReasons is null)
            throw new InvalidDataException("台架控制状态缺少恢复原因集合。");

        if (OperationId == Guid.Empty)
            throw new InvalidDataException("台架控制状态不能使用空 OperationId。");
        if (OperationId is null)
        {
            if (Phase != BenchPhase.Idle || RequiresRecovery || RecoveryReasons.Count != 0)
                throw new InvalidDataException("没有 OperationId 的台架控制状态必须为无恢复标记的 Idle。");
        }
        else if (Phase == BenchPhase.Idle)
        {
            throw new InvalidDataException("活动台架控制状态不能使用 Idle 阶段。");
        }

        if ((Phase == BenchPhase.RecoveryRequired) != RequiresRecovery)
            throw new InvalidDataException("RecoveryRequired 阶段与 RequiresRecovery 标志不一致。");
        if (RecoveryReasons.Any(string.IsNullOrWhiteSpace)
            || RecoveryReasons.Distinct(StringComparer.Ordinal).Count() != RecoveryReasons.Count
            || RecoveryReasons.Any(reason => !KnownRecoveryReasons.Contains(reason)))
            throw new InvalidDataException("台架控制状态包含空白、重复或未知恢复原因。");
        if (RequiresRecovery && RecoveryReasons.Count == 0)
            throw new InvalidDataException("恢复受限状态必须至少包含一个恢复原因。");
        if ((LastSafeStopJson is null) != (LastEvidenceKey is null)
            || LastSafeStopJson is not null && string.IsNullOrWhiteSpace(LastSafeStopJson)
            || LastEvidenceKey is not null && string.IsNullOrWhiteSpace(LastEvidenceKey))
            throw new InvalidDataException("安全收尾 JSON 与证据键必须同时为空或同时提供。");
    }
}
