using XXX.TestBench.Core.Domain.TestParameters;
using XXX.TestBench.Core.Ports;
using XXX.TestBench.Infrastructure.Persistence;
using XXX.TestBench.Infrastructure.Persistence.Entities;

namespace XXX.TestBench.Infrastructure.Persistence.Repositories;

/// <summary>
/// 两级直编试验参数的数据库实现；保存按作用域键插入或更新。
/// </summary>
public sealed class TestParameterRepository : SqliteRepositoryBase, ITestParameterRepository
{
    /// <summary>
    /// 项目级参数固定键。
    /// </summary>
    private const string ProjectKey = "PROJECT";

    /// <summary>
    /// 创建参数仓库。
    /// </summary>
    public TestParameterRepository(ISqliteConnectionFactory factory) : base(factory) { }

    /// <summary>
    /// 读取项目级参数。
    /// </summary>
    public async Task<ProjectTestParameter?> GetProjectAsync(CancellationToken ct = default)
    {
        var row = await RunDbAsync(() => Select<SqliteProjectTestParameter>()
            .Where(x => x.ScopeKey == ProjectKey)
            .ToOne(), ct);
        return row is null ? null : new ProjectTestParameter
        {
            TestTimeSeconds = row.TestTimeSeconds,
            UpdatedBy = row.UpdatedBy,
            UpdatedAtUtc = ParseUtc(row.UpdatedAtUtc)
        };
    }

    /// <summary>
    /// 保存项目级参数。
    /// </summary>
    public Task SaveProjectAsync(ProjectTestParameter value, CancellationToken ct = default)
        => RunDbAsync(() => InsertOrUpdate<SqliteProjectTestParameter>()
            .SetSource(new SqliteProjectTestParameter
            {
                ScopeKey = ProjectKey,
                TestTimeSeconds = value.TestTimeSeconds,
                UpdatedBy = value.UpdatedBy,
                UpdatedAtUtc = value.UpdatedAtUtc.ToString("O")
            })
            .ExecuteAffrows(), ct);

    /// <summary>
    /// 读取产品级参数。
    /// </summary>
    public async Task<ProductTestParameter?> GetProductAsync(int productTypeId, int productModelId, CancellationToken ct = default)
    {
        var row = await RunDbAsync(() => Select<SqliteProductTestParameter>()
            .Where(x => x.ProductTypeId == productTypeId)
            .Where(x => x.ProductModelId == productModelId)
            .ToOne(), ct);
        return row is null ? null : new ProductTestParameter
        {
            ProductTypeId = row.ProductTypeId,
            ProductModelId = row.ProductModelId,
            TestVoltageV = row.TestVoltageV,
            ProtectCurrentMa = row.ProtectCurrentMa,
            UpdatedBy = row.UpdatedBy,
            UpdatedAtUtc = ParseUtc(row.UpdatedAtUtc)
        };
    }

    /// <summary>
    /// 保存产品级参数。
    /// </summary>
    public Task SaveProductAsync(ProductTestParameter value, CancellationToken ct = default)
        => RunDbAsync(() => InsertOrUpdate<SqliteProductTestParameter>()
            .SetSource(new SqliteProductTestParameter
            {
                ProductTypeId = value.ProductTypeId,
                ProductModelId = value.ProductModelId,
                TestVoltageV = value.TestVoltageV,
                ProtectCurrentMa = value.ProtectCurrentMa,
                UpdatedBy = value.UpdatedBy,
                UpdatedAtUtc = value.UpdatedAtUtc.ToString("O")
            })
            .ExecuteAffrows(), ct);
}
