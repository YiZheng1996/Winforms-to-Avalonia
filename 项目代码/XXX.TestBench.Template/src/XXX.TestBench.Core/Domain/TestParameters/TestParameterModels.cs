using System.Text.Json;

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
}

/// <summary>
/// 参数校验结果：是否合法以及不合法时的原因。
/// </summary>
public sealed record TestParameterValidationResult(bool IsValid, string Error)
{
    public static TestParameterValidationResult Success() => new(true, string.Empty);
    public static TestParameterValidationResult Fail(string error) => new(false, error);
}

/// <summary>
/// 直编参数统一校验器。界面保存与自动试验启动必须共用同一套范围判断。
/// </summary>
public static class TestParameterValidator
{
    /// <summary>
    /// 校验试验时间（秒）。合法返回 null，否则返回错误原因。
    /// </summary>
    public static string? ValidateTestTime(int seconds)
        => seconds is < 1 or > 3600 ? "试验时间必须在 1～3600 s 之间。" : null;

    /// <summary>
    /// 校验试验电压（V）。合法返回 null，否则返回错误原因。
    /// </summary>
    public static string? ValidateTestVoltage(double voltage)
        => !double.IsFinite(voltage) || voltage is <= 0 or > 5000
            ? "试验电压必须大于 0 且不超过 5000 V。"
            : null;

    /// <summary>
    /// 校验保护电流（mA）。合法返回 null，否则返回错误原因。
    /// </summary>
    public static string? ValidateProtectCurrent(double current)
        => !double.IsFinite(current) || current is <= 0 or > 1000
            ? "保护电流必须大于 0 且不超过 1000 mA。"
            : null;

    /// <summary>
    /// 校验合并后的全部有效参数。
    /// </summary>
    public static TestParameterValidationResult Validate(EffectiveTestParameters value)
    {
        if (value.ProductTypeId <= 0) return TestParameterValidationResult.Fail("未选择有效的产品类型。");
        if (value.ProductModelId <= 0) return TestParameterValidationResult.Fail("未选择有效的产品型号。");
        var error = ValidateTestTime(value.TestTimeSeconds)
            ?? ValidateTestVoltage(value.TestVoltageV)
            ?? ValidateProtectCurrent(value.ProtectCurrentMa);
        return error is null ? TestParameterValidationResult.Success() : TestParameterValidationResult.Fail(error);
    }
}

/// <summary>
/// 试验记录参数快照的 JSON 编解码。启动时固化，执行与追溯只读快照，不依赖界面输入框。
/// </summary>
public static class TestParameterSnapshot
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    /// <summary>
    /// 把有效参数序列化为快照文本。
    /// </summary>
    public static string ToJson(EffectiveTestParameters value) => JsonSerializer.Serialize(value, Options);

    /// <summary>
    /// 把快照文本反序列化为有效参数；空或格式错误返回 null。
    /// </summary>
    public static EffectiveTestParameters? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<EffectiveTestParameters>(json, Options); }
        catch (JsonException) { return null; }
    }
}
