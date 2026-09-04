using XXX.TestBench.Core.Domain.TestParameters;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Infrastructure.Persistence.Repositories;

/// <summary>
/// 三级直编试验参数的数据库实现；保存按作用域键更新或新增。
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
        var row = await QuerySingleAsync<ProjectRow>(
            "SELECT test_time_seconds AS TestTimeSeconds, updated_by AS UpdatedBy, updated_at_utc AS UpdatedAtUtc FROM project_test_parameters WHERE scope_key=@key",
            new { key = ProjectKey }, ct);
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
    public Task SaveProjectAsync(ProjectTestParameter value, CancellationToken ct = default) => ExecuteAsync("""
        INSERT INTO project_test_parameters (scope_key, test_time_seconds, updated_by, updated_at_utc)
        VALUES (@key, @testTimeSeconds, @updatedBy, @updatedAtUtc)
        ON CONFLICT(scope_key) DO UPDATE SET test_time_seconds=@testTimeSeconds, updated_by=@updatedBy, updated_at_utc=@updatedAtUtc
        """, new
    {
        key = ProjectKey,
        testTimeSeconds = value.TestTimeSeconds,
        updatedBy = value.UpdatedBy,
        updatedAtUtc = value.UpdatedAtUtc.ToString("O")
    }, ct);

    /// <summary>
    /// 读取产品类型级参数。
    /// </summary>
    public async Task<ProductTypeTestParameter?> GetTypeAsync(int productTypeId, CancellationToken ct = default)
    {
        var row = await QuerySingleAsync<TypeRow>(
            "SELECT product_type_id AS ProductTypeId, test_voltage_v AS TestVoltageV, updated_by AS UpdatedBy, updated_at_utc AS UpdatedAtUtc FROM product_type_test_parameters WHERE product_type_id=@id",
            new { id = productTypeId }, ct);
        return row is null ? null : new ProductTypeTestParameter
        {
            ProductTypeId = row.ProductTypeId,
            TestVoltageV = row.TestVoltageV,
            UpdatedBy = row.UpdatedBy,
            UpdatedAtUtc = ParseUtc(row.UpdatedAtUtc)
        };
    }

    /// <summary>
    /// 保存产品类型级参数。
    /// </summary>
    public Task SaveTypeAsync(ProductTypeTestParameter value, CancellationToken ct = default) => ExecuteAsync("""
        INSERT INTO product_type_test_parameters (product_type_id, test_voltage_v, updated_by, updated_at_utc)
        VALUES (@id, @testVoltageV, @updatedBy, @updatedAtUtc)
        ON CONFLICT(product_type_id) DO UPDATE SET test_voltage_v=@testVoltageV, updated_by=@updatedBy, updated_at_utc=@updatedAtUtc
        """, new
    {
        id = value.ProductTypeId,
        testVoltageV = value.TestVoltageV,
        updatedBy = value.UpdatedBy,
        updatedAtUtc = value.UpdatedAtUtc.ToString("O")
    }, ct);

    /// <summary>
    /// 读取产品型号级参数。
    /// </summary>
    public async Task<ProductModelTestParameter?> GetModelAsync(int productModelId, CancellationToken ct = default)
    {
        var row = await QuerySingleAsync<ModelRow>(
            "SELECT product_model_id AS ProductModelId, protect_current_ma AS ProtectCurrentMa, updated_by AS UpdatedBy, updated_at_utc AS UpdatedAtUtc FROM product_model_test_parameters WHERE product_model_id=@id",
            new { id = productModelId }, ct);
        return row is null ? null : new ProductModelTestParameter
        {
            ProductModelId = row.ProductModelId,
            ProtectCurrentMa = row.ProtectCurrentMa,
            UpdatedBy = row.UpdatedBy,
            UpdatedAtUtc = ParseUtc(row.UpdatedAtUtc)
        };
    }

    /// <summary>
    /// 保存产品型号级参数。
    /// </summary>
    public Task SaveModelAsync(ProductModelTestParameter value, CancellationToken ct = default) => ExecuteAsync("""
        INSERT INTO product_model_test_parameters (product_model_id, protect_current_ma, updated_by, updated_at_utc)
        VALUES (@id, @protectCurrentMa, @updatedBy, @updatedAtUtc)
        ON CONFLICT(product_model_id) DO UPDATE SET protect_current_ma=@protectCurrentMa, updated_by=@updatedBy, updated_at_utc=@updatedAtUtc
        """, new
    {
        id = value.ProductModelId,
        protectCurrentMa = value.ProtectCurrentMa,
        updatedBy = value.UpdatedBy,
        updatedAtUtc = value.UpdatedAtUtc.ToString("O")
    }, ct);

    private sealed class ProjectRow
    {
        public int TestTimeSeconds { get; set; }
        public string UpdatedBy { get; set; } = string.Empty;
        public string UpdatedAtUtc { get; set; } = string.Empty;
    }

    private sealed class TypeRow
    {
        public int ProductTypeId { get; set; }
        public double TestVoltageV { get; set; }
        public string UpdatedBy { get; set; } = string.Empty;
        public string UpdatedAtUtc { get; set; } = string.Empty;
    }

    private sealed class ModelRow
    {
        public int ProductModelId { get; set; }
        public double ProtectCurrentMa { get; set; }
        public string UpdatedBy { get; set; } = string.Empty;
        public string UpdatedAtUtc { get; set; } = string.Empty;
    }
}
