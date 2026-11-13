using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.TestPoints;
using XXX.TestBench.Core.Ports;
using XXX.TestBench.Infrastructure.Persistence;
using XXX.TestBench.Infrastructure.Persistence.Entities;

namespace XXX.TestBench.Infrastructure.Persistence.Repositories;

/// <summary>
/// 产品型号项点配置的数据库实现；按顺序版本条件整表替换。
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
    public async Task ReplaceAsync(
        int productModelId,
        IReadOnlyList<ModelPointConfig> configs,
        long expectedSequenceRevision,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(configs);
        if (expectedSequenceRevision < 0)
            throw new DomainException("型号项点顺序版本不能小于零");
        if (configs.Any(config => config.ProductModelId != productModelId))
            throw new DomainException("项点配置中包含其他产品型号的数据");
        var nextSequenceRevision = checked(expectedSequenceRevision + 1);

        await using var lease = await _factory.OpenLeaseAsync(ct);
        await using var txn = await lease.Connection.BeginTransactionAsync(ct);
        try
        {
            // 与整表替换处于同一事务；旧编辑窗口只能命中当前 sequence_revision。
            var updated = await RunDbAsync(() => Update<SqliteProductModel>(lease.Connection, txn)
                .Where(x => x.Id == productModelId && x.SequenceRevision == expectedSequenceRevision)
                .Set(x => x.SequenceRevision, nextSequenceRevision)
                .ExecuteAffrows(), ct);
            if (updated != 1)
                throw new DomainException("型号项点顺序已被其他操作修改，请重新加载后再保存");

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
