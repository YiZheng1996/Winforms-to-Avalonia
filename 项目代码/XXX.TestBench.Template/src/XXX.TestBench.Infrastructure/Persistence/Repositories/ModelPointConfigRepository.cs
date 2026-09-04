using XXX.TestBench.Core.Domain.TestPoints;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Infrastructure.Persistence.Repositories;

/// <summary>
/// 产品型号项点配置的数据库实现；整表替换式保存，序号连续且一致。
/// </summary>
public sealed class ModelPointConfigRepository : SqliteRepositoryBase, IModelPointConfigRepository
{
    /// <summary>
    /// 数据库连接工厂。
    /// </summary>
    private readonly ISqliteConnectionFactory _factory;

    /// <summary>
    /// 创建项点配置仓库。
    /// </summary>
    public ModelPointConfigRepository(ISqliteConnectionFactory factory) : base(factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// 读取某产品型号的项点配置列表。
    /// </summary>
    public async Task<IReadOnlyList<ModelPointConfig>> ListByModelAsync(int productModelId, CancellationToken ct = default)
    {
        var rows = await QueryAsync<ConfigRow>(
            "SELECT product_model_id AS ProductModelId, test_item_point_id AS TestItemPointId, sort_order AS SortOrder FROM model_point_configs WHERE product_model_id=@id ORDER BY sort_order",
            new { id = productModelId }, ct);
        return rows.Select(r => new ModelPointConfig(r.ProductModelId, r.TestItemPointId, r.SortOrder)).ToList();
    }

    /// <summary>
    /// 在事务中整体替换某产品型号的项点配置。
    /// </summary>
    public async Task ReplaceAsync(int productModelId, IReadOnlyList<ModelPointConfig> configs, CancellationToken ct = default)
    {
        await using var lease = await _factory.OpenLeaseAsync(ct);
        await using var txn = await lease.Connection.BeginTransactionAsync(ct);
        await Db.Ado.ExecuteNonQueryAsync(lease.Connection, txn,
            "DELETE FROM model_point_configs WHERE product_model_id=@id", new { id = productModelId }, ct);
        foreach (var config in configs)
        {
            await Db.Ado.ExecuteNonQueryAsync(lease.Connection, txn, """
                INSERT INTO model_point_configs (product_model_id, test_item_point_id, sort_order, configured_at_utc)
                VALUES (@productModelId, @testItemPointId, @sortOrder, @configuredAtUtc)
                """, new
            {
                productModelId = config.ProductModelId,
                testItemPointId = config.TestItemPointId,
                sortOrder = config.SortOrder,
                configuredAtUtc = DateTime.UtcNow.ToString("O")
            }, ct);
        }
        await txn.CommitAsync(ct);
    }

    private sealed class ConfigRow
    {
        public int ProductModelId { get; set; }
        public int TestItemPointId { get; set; }
        public int SortOrder { get; set; }
    }
}
