using XXX.TestBench.Core.Common;

namespace XXX.TestBench.Core.Domain.TestDefinitions;

/// <summary>试验项参数定义。值必须通过本定义校验，不能在工艺监控页临时覆盖。</summary>
public sealed class ParameterDefinition
{
    public int Id { get; set; }
    public int TestItemDefinitionId { get; init; }
    public required string Code { get; init; }
    public required string Name { get; set; }
    public ParameterDataType DataType { get; set; }
    public string? Unit { get; set; }
    public bool IsRequired { get; set; }
    public decimal? MinValue { get; set; }
    public decimal? MaxValue { get; set; }
    public int? Precision { get; set; }
    public IReadOnlyList<string> AllowedValues { get; set; } = Array.Empty<string>();
    public int SortOrder { get; set; }

    /// <summary>按定义校验参数值；返回错误信息，合法时返回 null。</summary>
    public string? Validate(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return IsRequired ? $"参数 {Name}({Code}) 为必填项" : null;
        }

        switch (DataType)
        {
            case ParameterDataType.Boolean:
                if (!bool.TryParse(rawValue, out _)) return $"参数 {Name}({Code}) 需要布尔值";
                break;
            case ParameterDataType.Integer:
                if (!long.TryParse(rawValue, out var i) || (MinValue.HasValue && i < MinValue.Value) || (MaxValue.HasValue && i > MaxValue.Value))
                    return $"参数 {Name}({Code}) 需要 [{MinValue?.ToString() ?? "-∞"}, {MaxValue?.ToString() ?? "+∞"}] 内的整数";
                break;
            case ParameterDataType.Decimal:
                if (!decimal.TryParse(rawValue, out var d) || (MinValue.HasValue && d < MinValue.Value) || (MaxValue.HasValue && d > MaxValue.Value))
                    return $"参数 {Name}({Code}) 需要 [{MinValue?.ToString() ?? "-∞"}, {MaxValue?.ToString() ?? "+∞"}] 内的数值";
                break;
            case ParameterDataType.Enum:
                if (AllowedValues.Count == 0 || !AllowedValues.Contains(rawValue, StringComparer.Ordinal))
                    return $"参数 {Name}({Code}) 需要枚举值之一：{string.Join("、", AllowedValues)}";
                break;
            case ParameterDataType.Text:
                break;
            default:
                return $"参数 {Name}({Code}) 类型未支持";
        }
        return null;
    }
}
