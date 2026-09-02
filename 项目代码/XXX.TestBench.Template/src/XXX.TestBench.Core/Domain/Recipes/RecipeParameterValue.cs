namespace XXX.TestBench.Core.Domain.Recipes;

/// <summary>
/// 配方项点参数值。值以字符串保存，由 ParameterDefinition 校验。
/// </summary>
public sealed class RecipeParameterValue
{
    public int Id { get; set; }
    public int RecipeItemId { get; init; }
    public int ParameterDefinitionId { get; init; }
    public required string RawValue { get; set; }
}
