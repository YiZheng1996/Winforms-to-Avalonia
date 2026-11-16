namespace XXX.TestBench.Core.Domain.TestParameters;

/// <summary>
/// 自动试验最终使用的强类型参数（项目级与产品类型/产品型号组合级参数合并后的结果）。
/// </summary>
public sealed record EffectiveTestParameters
{
    public required int ProductTypeId { get; init; }
    public required int ProductModelId { get; init; }
    public required int TestTimeSeconds { get; init; }
    public required double TestVoltageV { get; init; }
    public required double ProtectCurrentMa { get; init; }
    /// <summary>
    /// 项目级与产品级参数的并发版本向量。
    /// </summary>
    public long ProjectRowVersion { get; init; }
    public long ProductRowVersion { get; init; }
}
