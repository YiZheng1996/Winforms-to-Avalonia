using FreeSql.DataAnnotations;

namespace XXX.TestBench.Infrastructure.Persistence.Entities;

/// <summary>
/// 试验记录表的 SQLite 映射实体。
/// </summary>
[Table(Name = "test_records")]
internal sealed class SqliteTestRecord
{
    /// <summary>
    /// 试验记录主键，由数据库自增生成。
    /// </summary>
    [Column(Name = "id", IsPrimary = true, IsIdentity = true)]
    public int Id { get; set; }

    /// <summary>
    /// 对外展示的试验记录号。
    /// </summary>
    [Column(Name = "record_number")]
    public string RecordNumber { get; set; } = string.Empty;

    /// <summary>
    /// 产品型号编号。
    /// </summary>
    [Column(Name = "product_model_id")]
    public int ProductModelId { get; set; }

    /// <summary>
    /// 被测产品编号，可为空。
    /// </summary>
    [Column(Name = "product_number", IsNullable = true)]
    public string? ProductNumber { get; set; }

    /// <summary>
    /// 生产批次号，可为空。
    /// </summary>
    [Column(Name = "batch_number", IsNullable = true)]
    public string? BatchNumber { get; set; }

    /// <summary>
    /// 工位号，可为空。
    /// </summary>
    [Column(Name = "station_number", IsNullable = true)]
    public string? StationNumber { get; set; }

    /// <summary>
    /// 试验备注，可为空。
    /// </summary>
    [Column(Name = "remark", IsNullable = true)]
    public string? Remark { get; set; }

    /// <summary>
    /// 试验参数快照，可为空。
    /// </summary>
    [Column(Name = "parameter_snapshot", IsNullable = true)]
    public string? ParameterSnapshot { get; set; }

    /// <summary>
    /// 试验顺序快照，可为空。
    /// </summary>
    [Column(Name = "sequence_snapshot", IsNullable = true)]
    public string? SequenceSnapshot { get; set; }

    /// <summary>
    /// 执行设备模式枚举值。
    /// </summary>
    [Column(Name = "device_mode")]
    public int DeviceMode { get; set; }

    /// <summary>
    /// 创建记录时使用的完整设备配置版本。
    /// </summary>
    [Column(Name = "device_configuration_revision", IsNullable = true)]
    public string? DeviceConfigurationRevision { get; set; }

    /// <summary>
    /// 创建记录时解析出的 SignalKey → PointId 映射 JSON 快照。
    /// </summary>
    [Column(Name = "signal_bindings_snapshot", IsNullable = true)]
    public string? SignalBindingsSnapshot { get; set; }

    /// <summary>
    /// 操作用户编号。
    /// </summary>
    [Column(Name = "operator_user_id")]
    public int OperatorUserId { get; set; }

    /// <summary>
    /// 试验记录状态枚举值。
    /// </summary>
    [Column(Name = "state")]
    public int State { get; set; }

    /// <summary>
    /// 试验结论，可为空。
    /// </summary>
    [Column(Name = "conclusion", IsNullable = true)]
    public string? Conclusion { get; set; }

    /// <summary>
    /// 试验开始时间，使用 UTC 文本保存。
    /// </summary>
    [Column(Name = "started_at_utc")]
    public string StartedAtUtc { get; set; } = string.Empty;

    /// <summary>
    /// 试验完成时间，使用 UTC 文本保存；未完成时为空。
    /// </summary>
    [Column(Name = "finished_at_utc", IsNullable = true)]
    public string? FinishedAtUtc { get; set; }
}
