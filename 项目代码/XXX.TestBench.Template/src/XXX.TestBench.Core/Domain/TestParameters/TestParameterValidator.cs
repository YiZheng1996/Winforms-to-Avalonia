namespace XXX.TestBench.Core.Domain.TestParameters;

/// <summary>
/// 统一校验试验参数，界面保存与自动试验启动共用同一套范围判断。
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
