using XXX.TestBench.Core.Domain.TestPoints;

namespace XXX.TestBench.Core.Ports;

/// <summary>
/// 产品型号项点配置持久化边界：整表替换式保存，保证序号连续且一致。
/// </summary>
public interface IModelPointConfigRepository
{
    /// <summary>
    /// 读取某产品型号的项点配置列表。
    /// </summary>
    Task<IReadOnlyList<ModelPointConfig>> ListByModelAsync(int productModelId, CancellationToken ct = default);
    /// <summary>
    /// 按预期顺序版本整表替换某产品型号的项点配置；版本不匹配时拒绝覆盖。
    /// </summary>
    Task ReplaceAsync(
        int productModelId,
        IReadOnlyList<ModelPointConfig> configs,
        long expectedSequenceRevision,
        CancellationToken ct = default);
}
