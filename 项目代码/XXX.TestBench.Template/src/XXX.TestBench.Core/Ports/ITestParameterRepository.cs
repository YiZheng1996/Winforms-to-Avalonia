using XXX.TestBench.Core.Domain.TestParameters;

namespace XXX.TestBench.Core.Ports;

/// <summary>
/// 直编试验参数的持久化边界：按项目、产品类型、产品型号三级读写。
/// </summary>
public interface ITestParameterRepository
{
    /// <summary>
    /// 读取项目级参数。
    /// </summary>
    Task<ProjectTestParameter?> GetProjectAsync(CancellationToken ct = default);
    /// <summary>
    /// 读取产品类型级参数。
    /// </summary>
    Task<ProductTypeTestParameter?> GetTypeAsync(int productTypeId, CancellationToken ct = default);
    /// <summary>
    /// 读取产品型号级参数。
    /// </summary>
    Task<ProductModelTestParameter?> GetModelAsync(int productModelId, CancellationToken ct = default);
    /// <summary>
    /// 保存项目级参数。
    /// </summary>
    Task SaveProjectAsync(ProjectTestParameter value, CancellationToken ct = default);
    /// <summary>
    /// 保存产品类型级参数。
    /// </summary>
    Task SaveTypeAsync(ProductTypeTestParameter value, CancellationToken ct = default);
    /// <summary>
    /// 保存产品型号级参数。
    /// </summary>
    Task SaveModelAsync(ProductModelTestParameter value, CancellationToken ct = default);
}
