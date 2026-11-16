using System.Data.SQLite;
using System.Text.Json;
using XXX.TestBench.Core.Domain.Execution;
using XXX.TestBench.Core.Execution;
using XXX.TestBench.Core.Ports;
using XXX.TestBench.Infrastructure.Persistence.Entities;

namespace XXX.TestBench.Infrastructure.Persistence.Repositories;

/// <summary>
/// SQLite v9 台架占用状态仓储；用 RowVersion 和阶段条件保护并发操作。
/// </summary>
public sealed class BenchControlStateRepository : SqliteRepositoryBase, IBenchControlStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    public BenchControlStateRepository(ISqliteConnectionFactory factory) : base(factory) { }

    public async Task<BenchControlState?> GetAsync(string benchId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(benchId);
        var row = await RunDbAsync(() => Select<SqliteBenchControlState>()
            .Where(state => state.BenchId == benchId)
            .ToOne(), cancellationToken);
        return row is null ? null : Map(row);
    }

    public async Task<bool> TrySaveAsync(
        BenchControlState next,
        long? expectedRowVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(next);
        next.EnsureValid();
        if (expectedRowVersion is < 0)
            throw new ArgumentOutOfRangeException(nameof(expectedRowVersion));

        var currentRow = await RunDbAsync(() => Select<SqliteBenchControlState>()
            .Where(state => state.BenchId == next.BenchId)
            .ToOne(), cancellationToken);

        if (currentRow is null)
        {
            if (expectedRowVersion is not null || next.RowVersion != 0
                || next.OperationId is null
                || next.Phase is not (BenchPhase.Preparing or BenchPhase.Maintaining or BenchPhase.RecoveryRequired)
                || next.RequiresRecovery != (next.Phase == BenchPhase.RecoveryRequired))
                return false;

            try
            {
                return await InsertAsync(ToRow(next), cancellationToken) == 1;
            }
            catch (SQLiteException)
            {
                // 同时首次领取时，主键冲突表示另一个节点已抢先建立状态；未知写入结果先重新查证。
                var persisted = await GetAsync(next.BenchId, CancellationToken.None);
                if (persisted is null) throw;
                return IsSameState(persisted, next);
            }
        }

        if (expectedRowVersion is null)
            return IsSameState(Map(currentRow), next);
        if (currentRow.RowVersion != expectedRowVersion.Value)
            return false;
        if (next.RowVersion != checked(expectedRowVersion.Value + 1))
            throw new InvalidOperationException("台架控制状态 RowVersion 必须逐次递增。");

        var current = Map(currentRow);
        EnsureTransition(current, next);
        var affected = await RunDbAsync(() => Update<SqliteBenchControlState>()
            .Where(state => state.BenchId == current.BenchId
                && state.OwnerNodeId == current.OwnerNodeId
                && state.RowVersion == expectedRowVersion.Value)
            .Set(state => state.OwnerNodeId, next.OwnerNodeId)
            .Set(state => state.OperationId, next.OperationId?.ToString("D").ToLowerInvariant())
            .Set(state => state.Phase, (int)next.Phase)
            .Set(state => state.RequiresRecovery, next.RequiresRecovery ? 1 : 0)
            .Set(state => state.RecoveryReason, SerializeReasons(next.RecoveryReasons))
            .Set(state => state.RowVersion, next.RowVersion)
            .Set(state => state.UpdatedAtUtc, next.UpdatedAtUtc.ToString("O"))
            .Set(state => state.LastSafeStopJson, next.LastSafeStopJson)
            .Set(state => state.LastEvidenceKey, next.LastEvidenceKey)
            .ExecuteAffrows(), cancellationToken);
        return affected == 1;
    }

    private static void EnsureTransition(BenchControlState current, BenchControlState next)
    {
        if (!string.Equals(current.BenchId, next.BenchId, StringComparison.Ordinal)
            || !string.Equals(current.OwnerNodeId, next.OwnerNodeId, StringComparison.Ordinal))
            throw new InvalidOperationException("台架控制状态不能更换台架或控制节点身份。");

        if (current.IsIdle)
        {
            if (next.IsIdle) return;
            if (next.OperationId is null
                || next.Phase is not (BenchPhase.Preparing or BenchPhase.Maintaining or BenchPhase.RecoveryRequired)
                || next.RequiresRecovery != (next.Phase == BenchPhase.RecoveryRequired))
                throw new InvalidOperationException("空闲台架只能领取为 Preparing、Maintaining 或显式 RecoveryRequired。");
            return;
        }

        if (next.OperationId is null)
        {
            if (!next.IsIdle || string.IsNullOrWhiteSpace(next.LastSafeStopJson)
                || string.IsNullOrWhiteSpace(next.LastEvidenceKey))
                throw new InvalidOperationException("清除台架占用前必须保存已确认的安全收尾结果和证据键。");
            EnsureSafeStopConfirmed(next.LastSafeStopJson, next.LastEvidenceKey);
            return;
        }

        if (current.OperationId != next.OperationId)
            throw new InvalidOperationException("活动台架控制状态不能更换 OperationId。");
        if (current.Phase != next.Phase && !BenchPhaseTransitions.CanTransition(current.Phase, next.Phase))
            throw new InvalidOperationException($"不允许台架阶段从 {current.Phase} 转为 {next.Phase}。");
        if (current.RequiresRecovery && (!next.RequiresRecovery
            || current.RecoveryReasons.Except(next.RecoveryReasons, StringComparer.Ordinal).Any()))
            throw new InvalidOperationException("恢复受限状态不能在活动操作期间解除或改写恢复原因。");
        if (current.LastSafeStopJson is not null
            && (!string.Equals(current.LastSafeStopJson, next.LastSafeStopJson, StringComparison.Ordinal)
                || !string.Equals(current.LastEvidenceKey, next.LastEvidenceKey, StringComparison.Ordinal)))
            throw new InvalidOperationException("已保存的收尾证据不可在活动操作中替换。");
    }

    private static SqliteBenchControlState ToRow(BenchControlState state) => new()
    {
        BenchId = state.BenchId,
        OwnerNodeId = state.OwnerNodeId,
        OperationId = state.OperationId?.ToString("D").ToLowerInvariant(),
        Phase = (int)state.Phase,
        RequiresRecovery = state.RequiresRecovery ? 1 : 0,
        RecoveryReason = SerializeReasons(state.RecoveryReasons),
        RowVersion = state.RowVersion,
        UpdatedAtUtc = state.UpdatedAtUtc.ToString("O"),
        LastSafeStopJson = state.LastSafeStopJson,
        LastEvidenceKey = state.LastEvidenceKey
    };

    private static BenchControlState Map(SqliteBenchControlState row)
    {
        Guid? operationId = null;
        if (row.OperationId is not null)
        {
            if (!Guid.TryParseExact(row.OperationId, "D", out var parsed)
                || !string.Equals(row.OperationId, parsed.ToString("D").ToLowerInvariant(), StringComparison.Ordinal))
                throw new InvalidDataException("数据库台架控制状态包含无效 OperationId。");
            operationId = parsed;
        }

        var state = new BenchControlState(
            row.BenchId,
            row.OwnerNodeId,
            operationId,
            (BenchPhase)row.Phase,
            row.RequiresRecovery switch
            {
                0 => false,
                1 => true,
                _ => throw new InvalidDataException("数据库台架控制状态包含无效 RequiresRecovery 值。")
            },
            DeserializeReasons(row.RecoveryReason),
            row.RowVersion,
            ParseUtc(row.UpdatedAtUtc),
            row.LastSafeStopJson,
            row.LastEvidenceKey);
        state.EnsureValid();
        return state;
    }

    private static string? SerializeReasons(IReadOnlyList<string> reasons)
        => reasons.Count == 0 ? null : JsonSerializer.Serialize(reasons, JsonOptions);

    private static void EnsureSafeStopConfirmed(string safeStopJson, string evidenceKey)
    {
        SafeStopResult? result;
        try
        {
            result = JsonSerializer.Deserialize<SafeStopResult>(safeStopJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("安全收尾证据 JSON 无法解析，拒绝清除台架占用。", ex);
        }

        if (result is null || result.Disposition != SafeStopDisposition.Confirmed
            || string.IsNullOrWhiteSpace(evidenceKey)
            || result.Actions is null
            || result.Actions.Any(action => action is null
                || string.IsNullOrWhiteSpace(action.ActionCode)
                || action.Disposition != SafeStopDisposition.Confirmed
                || string.IsNullOrWhiteSpace(action.EvidenceKey)))
            throw new InvalidOperationException("安全收尾尚未确认或动作证据不完整，拒绝清除台架占用。");
    }

    private static IReadOnlyList<string> DeserializeReasons(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        return JsonSerializer.Deserialize<string[]>(json, JsonOptions)
            ?? throw new InvalidDataException("数据库台架控制状态的恢复原因 JSON 为空。");
    }

    private static bool IsSameState(BenchControlState left, BenchControlState right)
        => left.BenchId == right.BenchId
           && left.OwnerNodeId == right.OwnerNodeId
           && left.OperationId == right.OperationId
           && left.Phase == right.Phase
           && left.RequiresRecovery == right.RequiresRecovery
           && left.RowVersion == right.RowVersion
           && left.UpdatedAtUtc == right.UpdatedAtUtc
           && left.LastSafeStopJson == right.LastSafeStopJson
           && left.LastEvidenceKey == right.LastEvidenceKey
           && left.RecoveryReasons.SequenceEqual(right.RecoveryReasons, StringComparer.Ordinal);
}
