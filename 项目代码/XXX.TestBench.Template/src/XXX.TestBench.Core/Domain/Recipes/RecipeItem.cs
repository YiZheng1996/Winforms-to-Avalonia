namespace XXX.TestBench.Core.Domain.Recipes;

/// <summary>配方包含的项点；顺序在配方内唯一。</summary>
public sealed class RecipeItem
{
    public int Id { get; set; }
    public int RecipeVersionId { get; init; }
    public int TestItemDefinitionId { get; init; }
    public int SortOrder { get; set; }
    public bool IsEnabled { get; set; } = true;
}
