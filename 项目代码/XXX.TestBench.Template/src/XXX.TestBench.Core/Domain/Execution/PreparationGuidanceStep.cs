namespace XXX.TestBench.Core.Domain.Execution;

/// <summary>准备指导中的一个有序步骤及其用户确认要求。</summary>
public sealed record PreparationGuidanceStep
{
    public required string StepId { get; init; }
    public required string Title { get; init; }
    public required string Text { get; init; }
    public string? ImageKey { get; init; }
    public required bool RequiresConfirmation { get; init; }
}
