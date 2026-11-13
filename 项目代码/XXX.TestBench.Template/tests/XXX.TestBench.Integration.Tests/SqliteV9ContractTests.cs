using XXX.TestBench.Infrastructure.Identity;
using XXX.TestBench.Infrastructure.Persistence;
using XXX.TestBench.Infrastructure.Persistence.Repositories;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Records;
using XXX.TestBench.Upgrade;
using System.Data.SQLite;
using System.Security.Cryptography;
using Xunit;

namespace XXX.TestBench.Integration.Tests;

public sealed class SqliteV9ContractTests
{
    [Fact]
    public async Task RecordWriter_RejectsOldContractAndMissingCurrentIdentityBeforeInsert()
    {
        using var env = TestEnv.Create();
        using var factory = new SqliteConnectionFactory(env.DbPath);
        await new SqliteDatabase(factory, new Pbkdf2PasswordHasher()).InitializeAsync();
        var repository = new RecordRepository(factory);
        foreach (var version in new[] { 1, 2, 99 })
        {
            var record = new TestRecord
            {
                RecordNumber = "invalid", ProductModelId = 1,
                ProductIdentity = new ProductIdentity("sample", null, null, null),
                RunId = Guid.NewGuid().ToString("D"), ResultContractVersion = version
            };
            await Assert.ThrowsAsync<DomainException>(() => repository.AddRecordAsync(record));
        }
        Assert.Empty(await repository.ListRecordsAsync(null));
    }

    [Fact]
    public async Task UpgradeV8_PreservesSourceAndHistory_AndProducesStableIdentity()
    {
        using var env = TestEnv.Create();
        var source = Path.Combine(env.DataRoot, "source-v8.db");
        using (var connection = new SQLiteConnection(new SQLiteConnectionStringBuilder { DataSource = source, Pooling = false }.ToString()))
        {
            connection.Open();
            foreach (var sql in SqliteSchemaV8.CreateStatements) Execute(connection, sql);
            Execute(connection, "INSERT INTO app_metadata VALUES ('schema_version','8')");
            Execute(connection, "PRAGMA user_version=8");
            Execute(connection, "INSERT INTO roles (id,name,system_key) VALUES (1,'Administrator','Administrator')");
            Execute(connection, "INSERT INTO users (id,login_name,display_name,password_hash,role_id,created_at_utc) VALUES (1,'op','操作员','unused',1,'2026-11-13T00:00:00.0000000Z')");
            Execute(connection, "INSERT INTO product_types (id,name,created_at_utc) VALUES (1,'类型','2026-11-13T00:00:00.0000000Z')");
            Execute(connection, "INSERT INTO product_models (id,product_type_id,name,created_at_utc) VALUES (1,1,'型号','2026-11-13T00:00:00.0000000Z')");
            Execute(connection, "INSERT INTO test_records (id,record_number,product_model_id,product_number,device_mode,operator_user_id,state,conclusion,started_at_utc,finished_at_utc,parameter_snapshot,sequence_snapshot) VALUES (1,'original-001',1,'sample',0,1,1,'原结论不重算','2026-11-13T00:00:00.0000000Z','2026-11-13T00:01:00.0000000Z','{}','[]')");
            Execute(connection, "INSERT INTO test_item_results (record_id,test_item_point_id,state,summary_value,result_text) VALUES (1,7,2,'12.345','原结果')");
            Execute(connection, "INSERT INTO report_records (test_record_id,template_path,output_path,status,created_by_user_id,created_at_utc) VALUES (1,'original.xlsx','original-result.xlsx',1,1,'2026-11-13T00:01:00.0000000Z')");
        }
        var originalHash = SHA256.HashData(await File.ReadAllBytesAsync(source));
        var upgrader = new SqliteV8ToV9Upgrade();
        var firstPath = Path.Combine(env.DataRoot, "candidate-v9.db");
        var first = await upgrader.ConvertAsync(source, firstPath, "history-bench");
        Assert.True(first.Success, string.Join("；", first.Blockers));
        var second = await upgrader.ConvertAsync(source, Path.Combine(env.DataRoot, "retry-v9.db"), "history-bench");
        Assert.True(second.Success, string.Join("；", second.Blockers));
        Assert.Equal(first.LegacyRunIdMappings[1], second.LegacyRunIdMappings[1]);
        Assert.Equal(originalHash, SHA256.HashData(await File.ReadAllBytesAsync(source)));
        Assert.Empty(SqliteSchemaV9.Validate(await SqliteDatabaseInspector.InspectReadOnlyAsync(firstPath)));

        using var factory = new SqliteConnectionFactory(firstPath);
        var repository = new RecordRepository(factory);
        var record = await repository.GetRecordAsync(1);
        Assert.NotNull(record);
        Assert.Equal("原结论不重算", record.Conclusion);
        Assert.Equal(EvaluationState.Legacy, record.EvaluationState);
        Assert.Equal("history-bench", record.BenchId);
        Assert.Equal("[]", record.SequenceSnapshot);
        Assert.Equal("12.345", Assert.Single(await repository.ListItemResultsAsync(1)).SummaryValue);
        await Assert.ThrowsAsync<DomainException>(() => repository.UpdateRecordAsync(record));
        var report = Assert.Single(await new XXX.TestBench.Infrastructure.Reports.ReportRepository(factory).ListByRecordAsync(1));
        Assert.Equal("original-result.xlsx", report.OutputPath);
        Assert.Null(report.RevisionId);
    }

    private static void Execute(SQLiteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    [Fact]
    public async Task CurrentSchema_AcceptsFreshDatabaseButRejectsWrongRunningPredicate()
    {
        using var env = TestEnv.Create();
        using var factory = new SqliteConnectionFactory(env.DbPath);
        await new SqliteDatabase(factory, new Pbkdf2PasswordHasher()).InitializeAsync();
        var inspection = await SqliteDatabaseInspector.InspectReadOnlyAsync(env.DbPath);
        Assert.Empty(SqliteSchemaV9.Validate(inspection));

        await factory.Db.Ado.ExecuteNonQueryAsync("DROP INDEX ux_test_records_bench_running", new { }, default);
        await factory.Db.Ado.ExecuteNonQueryAsync(
            "CREATE UNIQUE INDEX ux_test_records_bench_running ON test_records(bench_id) WHERE state = 1 AND bench_id IS NOT NULL", new { }, default);
        var invalid = await SqliteDatabaseInspector.InspectReadOnlyAsync(env.DbPath);
        Assert.Contains(SqliteSchemaV9.Validate(invalid), issue => issue.Contains("过滤条件"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new SqliteDatabase(factory, new Pbkdf2PasswordHasher()).InitializeAsync());
    }
}
