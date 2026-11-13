using FreeSql.DataAnnotations;

namespace XXX.TestBench.Infrastructure.Persistence.Entities;

/// <summary>
/// v9 bench_control_state 表映射。该表在 v9 schema 中已存在，不触发数据库版本升级。
/// </summary>
[Table(Name = "bench_control_state")]
internal sealed class SqliteBenchControlState
{
    [Column(Name = "bench_id", IsPrimary = true)]
    public string BenchId { get; set; } = string.Empty;

    [Column(Name = "owner_node_id")]
    public string OwnerNodeId { get; set; } = string.Empty;

    [Column(Name = "operation_id", IsNullable = true)]
    public string? OperationId { get; set; }

    [Column(Name = "phase")]
    public int Phase { get; set; }

    [Column(Name = "requires_recovery")]
    public int RequiresRecovery { get; set; }

    [Column(Name = "recovery_reason", IsNullable = true)]
    public string? RecoveryReason { get; set; }

    [Column(Name = "row_version")]
    public long RowVersion { get; set; }

    [Column(Name = "updated_at_utc")]
    public string UpdatedAtUtc { get; set; } = string.Empty;

    [Column(Name = "last_safe_stop_json", IsNullable = true)]
    public string? LastSafeStopJson { get; set; }

    [Column(Name = "last_evidence_key", IsNullable = true)]
    public string? LastEvidenceKey { get; set; }
}
