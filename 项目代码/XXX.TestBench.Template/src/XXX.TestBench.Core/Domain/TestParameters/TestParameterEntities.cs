namespace XXX.TestBench.Core.Domain.TestParameters;

/// <summary>
/// 项目级试验参数（全项目仅 1 行，scope_key 固定为 PROJECT）。
/// </summary>
public sealed class ProjectTestParameter
{
    public int TestTimeSeconds { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>
/// 产品类型级试验参数（每个产品类型 1 行）。
/// </summary>
public sealed class ProductTypeTestParameter
{
    public int ProductTypeId { get; init; }
    public double TestVoltageV { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>
/// 产品型号级试验参数（每个产品型号 1 行）。
/// </summary>
public sealed class ProductModelTestParameter
{
    public int ProductModelId { get; init; }
    public double ProtectCurrentMa { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; }
}
