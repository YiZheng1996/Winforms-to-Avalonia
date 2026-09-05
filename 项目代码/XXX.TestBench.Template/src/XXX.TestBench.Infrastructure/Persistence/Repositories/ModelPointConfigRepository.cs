using XXX.TestBench.Core.Domain.TestPoints;
using XXX.TestBench.Core.Ports;
using XXX.TestBench.Infrastructure.Persistence;
using XXX.TestBench.Infrastructure.Persistence.Entities;

namespace XXX.TestBench.Infrastructure.Persistence.Repositories;

/// <summary>
/// 产品型号项点配置的数据库实现；整表替换式保存，序号连续且一致。
/// </summary>
public sealed class ModelPointConfigRepository : SqliteRepositoryBase, IModelPointConfigRepository
{
    /// <summary>
    /// 数据库连接工厂，用于整表替换事务。
    /// </summary>
    private readonly ISqliteConnectionFactory _factory;

    /// <summary>
    /// 创建项点配置仓库。
    /// </summary>
    public ModelPointConfigRepository(ISqliteConnectionFactory factory) : base(factory) => _factory = factory;

    /// <summary>
    /// 读取某产品型号的项点配置列表。
    /// </summary>
    public async Task<IReadOnlyList<ModelPointConfig>> ListByModelAsync(int productModelId, CancellationToken ct = default)
    {
        var rows = await RunDbAsync(() => Select<SqliteModelPointConfig>()
            .Where(x => x.ProductModelId == productModelId)
            .OrderBy(x => x.SortOrder)
            .ToList(), ct);
        return rows
            .Select(row => new ModelPointConfig(row.ProductModelId, row.TestItemPointId, row.SortOrder))
            .ToList();
    }

    /// <summary>
    /// 在事务中整体替换某产品型号的项点配置。
    /// </summary>
    public async Task ReplaceAsync(int productModelId, IReadOnlyList<ModelPointConfig> configs, CancellationToken ct = default)
    {
        await using var lease = await _factory.OpenLeaseAsync(ct);
        await using var txn = await lease.Connection.BeginTransactionAsync(ct);
        try
        {
            await RunDbAsync(() => Delete<SqliteModelPointConfig>(lease.Connection, txn)
                .Where(x => x.ProductModelId == productModelId)
                .ExecuteAffrows(), ct);

            if (configs.Count > 0)
            {
                var configuredAtUtc = DateTime.UtcNow.ToString("O");
                var rows = configs.Select(config => new SqliteModelPointConfig
                {
                    ProductModelId = config.ProductModelId,
                    TestItemPointId = config.TestItemPointId,
                    SortOrder = config.SortOrder,
                    ConfiguredAtUtc = configuredAtUtc
                }).ToList();
                await RunDbAsync(() => InsertMany(rows, lease.Connection, txn).ExecuteAffrows(), ct);
            }

            await txn.CommitAsync(ct);
        }
        catch
        {
            await txn.RollbackAsync(ct);
            throw;
        }
    }
}
