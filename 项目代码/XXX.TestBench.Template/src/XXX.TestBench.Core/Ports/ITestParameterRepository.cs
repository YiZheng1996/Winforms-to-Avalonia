using XXX.TestBench.Core.Domain.TestParameters;

namespace XXX.TestBench.Core.Ports;

/// <summary>
/// 直编试验参数的持久化边界：按项目、产品类型、产品型号三级读写。
/// </summary>
public interface ITestParameterRepository
{
    Task<ProjectTestParameter?> GetProjectAsync(CancellationToken ct = default);
    Task<ProductTypeTestParameter?> GetTypeAsync(int productTypeId, CancellationToken ct = default);
    Task<ProductModelTestParameter?> GetModelAsync(int productModelId, CancellationToken ct = default);
    Task SaveProjectAsync(ProjectTestParameter value, CancellationToken ct = default);
    Task SaveTypeAsync(ProductTypeTestParameter value, CancellationToken ct = default);
    Task SaveModelAsync(ProductModelTestParameter value, CancellationToken ct = default);
}
