namespace XXX.TestBench.Core.Domain.Execution;

/// <summary>
/// 安装级持久操作标记，独立于可切换的业务数据库和报表目录。
/// </summary>
public sealed record OperationFence(
    int SchemaVersion,
    long Revision,
    Guid OperationId,
    string BenchId,
    string NodeId,
    string DatabaseInstanceId,
    Guid? RunId,
    string DeviceConfigurationRevision,
    BenchPhase Phase,
    bool ActionMayHaveBeenIssued,
    string? LastCommandId,
    string? LastCommandDisposition,
    bool SafeStopConfirmed,
    string? LastSafeStopJson,
    string? LastEvidenceKey,
    IReadOnlyList<string> RecoveryReasons,
    string UpdatedAtUtc)
{
    public const int CurrentSchemaVersion = 1;

    public void EnsureValid()
    {
        if (SchemaVersion != CurrentSchemaVersion)
            throw new InvalidDataException($"不支持的 operation-fence 版本：{SchemaVersion}");
        if (Revision < 0 || OperationId == Guid.Empty)
            throw new InvalidDataException("operation-fence 版本号或 OperationId 无效");
        if (string.IsNullOrWhiteSpace(BenchId) || string.IsNullOrWhiteSpace(NodeId)
            || string.IsNullOrWhiteSpace(DeviceConfigurationRevision))
            throw new InvalidDataException("operation-fence 缺少台架、本机或设备配置身份");
        if (!Guid.TryParseExact(DatabaseInstanceId, "D", out var databaseId)
            || !string.Equals(DatabaseInstanceId, databaseId.ToString("D").ToLowerInvariant(), StringComparison.Ordinal))
            throw new InvalidDataException("DatabaseInstanceId 必须为小写 GUID D 格式");
        if (RunId == Guid.Empty)
            throw new InvalidDataException("RunId 不能是空 GUID");
        if (!Enum.IsDefined(Phase) || Phase == BenchPhase.Idle)
            throw new InvalidDataException("持久操作标记不能使用未知或 Idle 阶段");
        if (!DateTime.TryParseExact(UpdatedAtUtc, "O", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var updated)
            || updated.Kind != DateTimeKind.Utc)
            throw new InvalidDataException("UpdatedAtUtc 必须是 UTC O 格式");
        if (RecoveryReasons is null || RecoveryReasons.Any(string.IsNullOrWhiteSpace)
            || RecoveryReasons.Distinct(StringComparer.Ordinal).Count() != RecoveryReasons.Count)
            throw new InvalidDataException("恢复原因列表为空项或重复");
        if (RecoveryReasons.Any(reason => reason is not RecoveryReasonCode.SafetyUnconfirmed
                and not RecoveryReasonCode.InterruptedOperation
                and not RecoveryReasonCode.PersistencePending))
            throw new InvalidDataException("operation-fence 包含未知恢复原因代码");
        if (Phase == BenchPhase.RecoveryRequired && RecoveryReasons.Count == 0)
            throw new InvalidDataException("RecoveryRequired 必须至少包含一个恢复原因");
        if (ActionMayHaveBeenIssued && (string.IsNullOrWhiteSpace(LastCommandId)
            || string.IsNullOrWhiteSpace(LastCommandDisposition)))
            throw new InvalidDataException("可能已发生设备动作时必须保留 CommandId 和实际 Disposition");
        if (SafeStopConfirmed && ActionMayHaveBeenIssued
            && (string.IsNullOrWhiteSpace(LastSafeStopJson) || string.IsNullOrWhiteSpace(LastEvidenceKey)))
            throw new InvalidDataException("已确认收尾的设备动作必须保留收尾结果和证据键");
    }
}
