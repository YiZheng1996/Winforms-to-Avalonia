namespace XXX.TestBench.Core.Domain.TestParameters;

/// <summary>
/// 参数校验结果：是否合法以及不合法时的原因。
/// </summary>
public sealed record TestParameterValidationResult(bool IsValid, string Error)
{
    public static TestParameterValidationResult Success() => new(true, string.Empty);

    public static TestParameterValidationResult Fail(string error) => new(false, error);
}
