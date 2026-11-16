using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Reports;
using XXX.TestBench.Core.Ports;
using XXX.TestBench.Infrastructure.Persistence;
using XXX.TestBench.Infrastructure.Persistence.Entities;

namespace XXX.TestBench.Infrastructure.Persistence.Repositories;

/// <summary>型号报表资源配置仓储，使用 v9 预留表和行版本控制并发更新。</summary>
public sealed class ModelResourceProfileRepository : SqliteRepositoryBase, IModelResourceProfileRepository
{
    public ModelResourceProfileRepository(ISqliteConnectionFactory factory) : base(factory) { }

    public async Task<ModelResourceProfile?> GetAsync(int productModelId, CancellationToken cancellationToken = default)
    {
        if (productModelId <= 0) throw new ArgumentOutOfRangeException(nameof(productModelId));
        var row = await RunDbAsync(() => Select<SqliteModelResourceProfile>()
            .Where(value => value.ProductModelId == productModelId).ToOne(), cancellationToken);
        return row is null ? null : Map(row);
    }

    public async Task SaveAsync(ModelResourceProfile profile, long? expectedRowVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.EnsureValid();
        if (expectedRowVersion is < 0) throw new DomainException("资源配置预期版本无效。");

        var current = await RunDbAsync(() => Select<SqliteModelResourceProfile>()
            .Where(value => value.ProductModelId == profile.ProductModelId).ToOne(), cancellationToken);
        var nowVersion = current?.RowVersion;
        if (nowVersion != expectedRowVersion)
            throw new DomainException("型号资源配置已被其他用户修改，请重新打开后再保存。");

        var row = Map(profile);
        if (current is null)
        {
            row.RowVersion = 0;
            var inserted = await InsertAsync(row, cancellationToken);
            if (inserted != 1) throw new DomainException("型号资源配置创建失败。");
            profile.RowVersion = 0;
            return;
        }

        var nextVersion = checked(current.RowVersion + 1);
        cancellationToken.ThrowIfCancellationRequested();
        var updated = Update<SqliteModelResourceProfile>()
            .Where(value => value.ProductModelId == profile.ProductModelId && value.RowVersion == current.RowVersion)
            .Set(value => value.TemplateKey, row.TemplateKey)
            .Set(value => value.MappingKey, row.MappingKey)
            .Set(value => value.ProcessTemplateKey, row.ProcessTemplateKey)
            .Set(value => value.ProcessMappingKey, row.ProcessMappingKey)
            .Set(value => value.GuidanceKey, row.GuidanceKey)
            .Set(value => value.Revision, row.Revision)
            .Set(value => value.UpdatedBy, row.UpdatedBy)
            .Set(value => value.UpdatedAtUtc, row.UpdatedAtUtc)
            .Set(value => value.RowVersion, nextVersion)
            .ExecuteAffrows();
        if (updated != 1) throw new DomainException("型号资源配置保存冲突，请重新打开后再保存。");
        profile.RowVersion = nextVersion;
    }

    private static ModelResourceProfile Map(SqliteModelResourceProfile row) => new()
    {
        ProductModelId = row.ProductModelId,
        TemplateKey = row.TemplateKey,
        MappingKey = row.MappingKey,
        ProcessTemplateKey = row.ProcessTemplateKey,
        ProcessMappingKey = row.ProcessMappingKey,
        GuidanceKey = row.GuidanceKey,
        Revision = row.Revision,
        UpdatedBy = row.UpdatedBy,
        UpdatedAtUtc = ParseUtc(row.UpdatedAtUtc),
        RowVersion = row.RowVersion
    };

    private static SqliteModelResourceProfile Map(ModelResourceProfile profile) => new()
    {
        ProductModelId = profile.ProductModelId,
        TemplateKey = profile.TemplateKey,
        MappingKey = profile.MappingKey,
        ProcessTemplateKey = profile.ProcessTemplateKey,
        ProcessMappingKey = profile.ProcessMappingKey,
        GuidanceKey = profile.GuidanceKey,
        Revision = profile.Revision,
        UpdatedBy = profile.UpdatedBy,
        UpdatedAtUtc = profile.UpdatedAtUtc.ToString("O"),
        RowVersion = profile.RowVersion
    };
}
