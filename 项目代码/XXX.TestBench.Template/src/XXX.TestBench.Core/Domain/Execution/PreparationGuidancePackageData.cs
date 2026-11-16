namespace XXX.TestBench.Core.Domain.Execution;

/// <summary>已验证的指导步骤及其图片内容。</summary>
public sealed record PreparationGuidancePackageData(
    PreparationGuidanceContentV1 Content,
    IReadOnlyDictionary<string, ReadOnlyMemory<byte>> Images);
