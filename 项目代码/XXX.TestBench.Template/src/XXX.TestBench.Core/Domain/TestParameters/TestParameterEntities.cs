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
}

/// <summary>
/// 产品类型级试验参数，每个产品类型一行。
/// </summary>
public sealed class ProductTypeTestParameter
{
    /// <summary>
    /// 所属产品类型编号。
    /// </summary>
    public int ProductTypeId { get; init; }
    /// <summary>
    /// 试验电压（伏）。
    /// </summary>
    public double TestVoltageV { get; set; }
    /// <summary>
    /// 最后修改人。
    /// </summary>
    public string UpdatedBy { get; set; } = string.Empty;
    /// <summary>
    /// 最后修改时间。
    /// </summary>
    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>
/// 产品型号级试验参数，每个产品型号一行。
/// </summary>
public sealed class ProductModelTestParameter
{
    /// <summary>
    /// 所属产品型号编号。
    /// </summary>
    public int ProductModelId { get; init; }
    /// <summary>
    /// 保护电流（毫安）。
    /// </summary>
    public double ProtectCurrentMa { get; set; }
    /// <summary>
    /// 最后修改人。
    /// </summary>
    public string UpdatedBy { get; set; } = string.Empty;
    /// <summary>
    /// 最后修改时间。
    /// </summary>
    public DateTime UpdatedAtUtc { get; set; }
}
