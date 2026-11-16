namespace XXX.TestBench.Core.Domain.Execution;

/// <summary>型号准备指导的版本化 JSON 内容。</summary>
public sealed record PreparationGuidanceContentV1
{
    public required int SchemaVersion { get; init; }
    public required string GuidanceId { get; init; }
    public required string Version { get; init; }
    public required IReadOnlyList<PreparationGuidanceStep> Steps { get; init; }
}
