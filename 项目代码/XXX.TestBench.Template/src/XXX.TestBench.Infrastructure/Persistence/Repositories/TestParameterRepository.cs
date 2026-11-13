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
            UpdatedAtUtc = ParseUtc(row.UpdatedAtUtc),
            RowVersion = row.RowVersion
        };
    }

    /// <summary>
    /// 保存项目级参数。
    /// </summary>
    public Task SaveProjectAsync(ProjectTestParameter value, CancellationToken ct = default)
        => RunDbAsync(() =>
        {
            var current = Select<SqliteProjectTestParameter>()
                .Where(x => x.ScopeKey == ProjectKey)
                .ToOne();
            if (current is null)
            {
                var inserted = Insert(new SqliteProjectTestParameter
                {
                    ScopeKey = ProjectKey,
                    TestTimeSeconds = value.TestTimeSeconds,
                    UpdatedBy = value.UpdatedBy,
                    UpdatedAtUtc = value.UpdatedAtUtc.ToString("O"),
                    RowVersion = value.RowVersion
                }).ExecuteAffrows();
                if (inserted != 1)
                    throw new XXX.TestBench.Core.Common.DomainException("项目参数创建失败，请刷新后重试。");
                value.RowVersion = 0;
                return;
            }

            var expectedVersion = value.RowVersion;
            var affected = Update<SqliteProjectTestParameter>()
                .Where(x => x.ScopeKey == ProjectKey && x.RowVersion == expectedVersion)
                .Set(x => x.TestTimeSeconds, value.TestTimeSeconds)
                .Set(x => x.UpdatedBy, value.UpdatedBy)
                .Set(x => x.UpdatedAtUtc, value.UpdatedAtUtc.ToString("O"))
                .Set(x => x.RowVersion, checked(expectedVersion + 1))
                .ExecuteAffrows();
            if (affected != 1)
                throw new XXX.TestBench.Core.Common.DomainException("项目参数已被其他用户修改，请刷新后重试。");
            value.RowVersion = checked(expectedVersion + 1);
        }, ct);

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
            UpdatedAtUtc = ParseUtc(row.UpdatedAtUtc),
            RowVersion = row.RowVersion
        };
    }

    /// <summary>
    /// 保存产品级参数。
    /// </summary>
    public Task SaveProductAsync(ProductTestParameter value, CancellationToken ct = default)
        => RunDbAsync(() =>
        {
            var current = Select<SqliteProductTestParameter>()
                .Where(x => x.ProductModelId == value.ProductModelId)
                .ToOne();
            if (current is null)
            {
                var inserted = Insert(new SqliteProductTestParameter
                {
                    ProductTypeId = value.ProductTypeId,
                    ProductModelId = value.ProductModelId,
                    TestVoltageV = value.TestVoltageV,
                    ProtectCurrentMa = value.ProtectCurrentMa,
                    UpdatedBy = value.UpdatedBy,
                    UpdatedAtUtc = value.UpdatedAtUtc.ToString("O"),
                    RowVersion = value.RowVersion
                }).ExecuteAffrows();
                if (inserted != 1)
                    throw new XXX.TestBench.Core.Common.DomainException("产品参数创建失败，请刷新后重试。");
                value.RowVersion = 0;
                return;
            }

            var expectedVersion = value.RowVersion;
            var affected = Update<SqliteProductTestParameter>()
                .Where(x => x.ProductModelId == value.ProductModelId && x.RowVersion == expectedVersion)
                .Set(x => x.ProductTypeId, value.ProductTypeId)
                .Set(x => x.TestVoltageV, value.TestVoltageV)
                .Set(x => x.ProtectCurrentMa, value.ProtectCurrentMa)
                .Set(x => x.UpdatedBy, value.UpdatedBy)
                .Set(x => x.UpdatedAtUtc, value.UpdatedAtUtc.ToString("O"))
                .Set(x => x.RowVersion, checked(expectedVersion + 1))
                .ExecuteAffrows();
            if (affected != 1)
                throw new XXX.TestBench.Core.Common.DomainException("产品参数已被其他用户修改，请刷新后重试。");
            value.RowVersion = checked(expectedVersion + 1);
        }, ct);
}
