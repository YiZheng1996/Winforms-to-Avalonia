using XXX.TestBench.Core.Domain.Reports;

namespace XXX.TestBench.Core.Ports;

/// <summary>产品型号与不可变报表资源键之间的并发安全映射。</summary>
public interface IModelResourceProfileRepository
{
    Task<ModelResourceProfile?> GetAsync(int productModelId, CancellationToken cancellationToken = default);

    /// <summary>按预期行版本新增或更新；并发编辑必须拒绝覆盖。</summary>
    Task SaveAsync(ModelResourceProfile profile, long? expectedRowVersion, CancellationToken cancellationToken = default);
}
