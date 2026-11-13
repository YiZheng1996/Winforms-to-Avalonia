using XXX.TestBench.Core.Common;

namespace XXX.TestBench.Core.Domain.Records;

/// <summary>
/// 单个试验项点的执行结果，引用记录内固化序列中的试验项点。
/// </summary>
public sealed class TestItemResult
{
    /// <summary>
    /// 结果记录编号。
    /// </summary>
    public int Id { get; set; }
    /// <summary>
    /// 所属试验记录编号。
    /// </summary>
    public int RecordId { get; init; }
    /// <summary>
    /// 对应的试验项点编号。
    /// </summary>
    public int TestItemPointId { get; init; }
    /// <summary>
    /// 当前执行状态。
    /// </summary>
    public ItemResultState State { get; internal set; } = ItemResultState.Pending;

    /// <summary>
    /// 仓储更新时必须匹配的数据库旧状态；成功持久化后由仓储确认。
    /// </summary>
    public ItemResultState ExpectedPersistedState { get; private set; } = ItemResultState.Pending;
    /// <summary>
    /// 结果汇总值，用于报表展示。
    /// </summary>
    public string? SummaryValue { get; set; }
    /// <summary>
    /// 结果文字说明。
    /// </summary>
    public string? ResultText { get; set; }
    /// <summary>
    /// 新版执行结果使用结构化测量；旧记录保留空集合。
    /// </summary>
    public IReadOnlyList<MeasuredValue> Measurements { get; private set; } = Array.Empty<MeasuredValue>();
    /// <summary>
    /// 新版运行中以 RunId 和冻结项点构成唯一槽；旧记录为空。
    /// </summary>
    public string? ExecutionSlotKey { get; init; }
    public long RowVersion { get; set; }
    /// <summary>
    /// 开始时间。
    /// </summary>
    public DateTime? StartedAtUtc { get; set; }
    /// <summary>
    /// 结束时间。
    /// </summary>
    public DateTime? FinishedAtUtc { get; set; }

    /// <summary>
    /// 标记开始执行；已结束的项点不允许重新开始。
    /// </summary>
    public void Start(DateTime utcNow)
    {
        if (State != ItemResultState.Pending)
            throw new DomainException($"项点结果当前状态 {State}，不能重新开始");
        State = ItemResultState.Running;
        StartedAtUtc ??= utcNow;
    }

    /// <summary>
    /// 写入最终结果与时间。
    /// </summary>
    public void SetResult(ItemResultState final, string? summaryValue, string? resultText, DateTime utcNow)
    {
        if (State != ItemResultState.Running)
            throw new DomainException($"项点结果当前状态 {State}，不能写入终态");
        if (final is not (ItemResultState.Passed or ItemResultState.Failed or ItemResultState.Skipped or ItemResultState.Aborted))
            throw new DomainException($"不支持的终态 {final}");
        State = final;
        SummaryValue = summaryValue;
        ResultText = resultText;
        FinishedAtUtc = utcNow;
    }

    /// <summary>
    /// 写入新版执行合同的项点终态及结构化测量。此终态表示执行结果，不表示产品合格。
    /// </summary>
    public void SetExecutionResult(
        ItemResultState final,
        string? summaryValue,
        string? resultText,
        IReadOnlyList<MeasuredValue> measurements,
        DateTime utcNow)
    {
        if (State != ItemResultState.Running)
            throw new DomainException($"项点结果当前状态 {State}，不能写入终态");
        if (final is not (ItemResultState.Succeeded or ItemResultState.Failed or ItemResultState.Aborted))
            throw new DomainException($"新版执行合同不支持终态 {final}");
        if (utcNow.Kind != DateTimeKind.Utc)
            throw new DomainException("项点结束时间必须是 UTC");

        var values = measurements ?? throw new ArgumentNullException(nameof(measurements));
        var duplicate = values
            .GroupBy(value => value.Key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new DomainException($"项点测量键重复或仅大小写不同：{duplicate.Key}");
        foreach (var value in values)
            value.EnsureValid();

        State = final;
        SummaryValue = summaryValue;
        ResultText = resultText;
        Measurements = values.ToArray();
        FinishedAtUtc = utcNow;
    }

    /// <summary>
    /// 读取持久化测量快照；空值表示旧记录没有结构化测量。
    /// </summary>
    public void RestoreMeasurements(string? json)
        => Measurements = string.IsNullOrWhiteSpace(json)
            ? Array.Empty<MeasuredValue>()
            : MeasuredValuesSnapshotSerializer.Deserialize(json);

    /// <summary>
    /// 更新成功后推进状态与版本条件，避免再次使用旧状态覆盖终态。
    /// </summary>
    public void ConfirmPersisted(long nextRowVersion)
    {
        if (nextRowVersion != checked(RowVersion + 1))
            throw new InvalidOperationException("项点结果 RowVersion 必须逐次递增");
        ExpectedPersistedState = State;
        RowVersion = nextRowVersion;
    }

    /// <summary>
    /// 将从数据库读取的状态设为下一次条件更新的预期状态。
    /// </summary>
    public void RestorePersistedState(ItemResultState state)
        => ExpectedPersistedState = state;
}
