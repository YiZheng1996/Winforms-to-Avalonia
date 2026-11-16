namespace XXX.TestBench.Core.Domain.Execution;

/// <summary>型号当前绑定的指导包及不可变资源摘要。</summary>
public sealed record ModelPreparationGuidanceSnapshot(
    int ProductModelId,
    string ResourceKey,
    string Sha256,
    PreparationGuidanceContentV1 Content,
    IReadOnlyDictionary<string, ReadOnlyMemory<byte>> Images);
