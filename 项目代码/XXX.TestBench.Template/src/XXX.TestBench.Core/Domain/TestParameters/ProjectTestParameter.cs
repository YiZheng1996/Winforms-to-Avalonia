namespace XXX.TestBench.Core.Domain.TestParameters;

/// <summary>
/// 项目级试验参数，全项目仅一行。
/// </summary>
public sealed class ProjectTestParameter
{
    /// <summary>
    /// 试验时间（秒）。
    /// </summary>
    public int TestTimeSeconds { get; set; }

    /// <summary>
    /// 最后修改人。
    /// </summary>
    public string UpdatedBy { get; set; } = string.Empty;

    /// <summary>
    /// 最后修改时间。
    /// </summary>
    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>
    /// 并发版本；更新成功后递增。
    /// </summary>
    public long RowVersion { get; set; }
}
