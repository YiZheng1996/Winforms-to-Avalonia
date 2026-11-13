using XXX.TestBench.Core.Domain.TestParameters;

namespace XXX.TestBench.Core.Ports;

/// <summary>
/// 直编试验参数的持久化边界：按项目和产品（产品类型 + 产品型号）两级读写。
/// </summary>
public interface ITestParameterRepository
{
    /// <summary>
    /// 读取项目级参数。
    /// </summary>
    Task<ProjectTestParameter?> GetProjectAsync(CancellationToken ct = default);
    /// <summary>
    /// 读取产品级参数。
    /// </summary>
    Task<ProductTestParameter?> GetProductAsync(int productTypeId, int productModelId, CancellationToken ct = default);
    /// <summary>
    /// 保存项目级参数。
    /// </summary>
    Task SaveProjectAsync(ProjectTestParameter value, CancellationToken ct = default);
    /// <summary>
    /// 保存产品级参数。
    /// </summary>
    Task SaveProductAsync(ProductTestParameter value, CancellationToken ct = default);
}
