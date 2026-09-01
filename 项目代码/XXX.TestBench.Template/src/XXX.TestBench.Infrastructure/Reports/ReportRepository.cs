using XXX.TestBench.Core.Domain.Reports;
using XXX.TestBench.Core.Ports;
using XXX.TestBench.Infrastructure.Persistence;
using XXX.TestBench.Infrastructure.Persistence.Repositories;

namespace XXX.TestBench.Infrastructure.Reports;

public sealed class ReportRepository : SqliteRepositoryBase, IReportRepository
{
    public ReportRepository(ISqliteConnectionFactory factory) : base(factory) { }

    public async Task AddAsync(ReportRecord record, CancellationToken ct = default)
    {
        var conn = OpenConnection();
        var owns = OwnsConnection;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO report_records (test_record_id, template_path, output_path, status, error, created_by_user_id, created_at_utc, completed_at_utc)
                VALUES ($record, $template, $output, $status, $error, $creator, $created, $completed)
                """;
            cmd.Parameters.AddWithValue("$record", record.TestRecordId);
            cmd.Parameters.AddWithValue("$template", record.TemplatePath);
            cmd.Parameters.AddWithValue("$output", (object?)record.OutputPath ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$status", (int)record.Status);
            cmd.Parameters.AddWithValue("$error", (object?)record.Error ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$creator", record.CreatedByUserId);
            cmd.Parameters.AddWithValue("$created", record.CreatedAtUtc.ToString("O"));
            cmd.Parameters.AddWithValue("$completed", (object?)record.CompletedAtUtc?.ToString("O") ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct);
            record.Id = (int)(long)(await UserRepository.LastInsertRowIdAsync(conn, ct));
        }
        finally { if (owns) await conn.DisposeAsync(); }
    }

    public async Task UpdateAsync(ReportRecord record, CancellationToken ct = default)
    {
        var conn = OpenConnection();
        var owns = OwnsConnection;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE report_records SET output_path=$output, status=$status, error=$error, completed_at_utc=$completed WHERE id=$id";
            cmd.Parameters.AddWithValue("$output", (object?)record.OutputPath ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$status", (int)record.Status);
            cmd.Parameters.AddWithValue("$error", (object?)record.Error ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$completed", (object?)record.CompletedAtUtc?.ToString("O") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$id", record.Id);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally { if (owns) await conn.DisposeAsync(); }
    }

    public async Task<ReportRecord?> GetAsync(int id, CancellationToken ct = default)
    {
        var conn = OpenConnection();
        var owns = OwnsConnection;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, test_record_id, template_path, output_path, status, error, created_by_user_id, created_at_utc, completed_at_utc FROM report_records WHERE id=$id";
            cmd.Parameters.AddWithValue("$id", id);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            return await reader.ReadAsync(ct) ? Map(reader) : null;
        }
        finally { if (owns) await conn.DisposeAsync(); }
    }

    public async Task<IReadOnlyList<ReportRecord>> ListByRecordAsync(int testRecordId, CancellationToken ct = default)
    {
        var result = new List<ReportRecord>();
        var conn = OpenConnection();
        var owns = OwnsConnection;
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, test_record_id, template_path, output_path, status, error, created_by_user_id, created_at_utc, completed_at_utc FROM report_records WHERE test_record_id=$id ORDER BY id";
            cmd.Parameters.AddWithValue("$id", testRecordId);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) result.Add(Map(reader));
            return result;
        }
        finally { if (owns) await conn.DisposeAsync(); }
    }

    private static ReportRecord Map(Microsoft.Data.Sqlite.SqliteDataReader r) => new()
    {
        Id = r.GetInt32(0),
        TestRecordId = r.GetInt32(1),
        TemplatePath = r.GetString(2),
        OutputPath = ParseNullableString(r.GetValue(3)),
        Status = (ReportStatus)r.GetInt32(4),
        Error = ParseNullableString(r.GetValue(5)),
        CreatedByUserId = r.GetInt32(6),
        CreatedAtUtc = ParseUtc(r.GetString(7)),
        CompletedAtUtc = ParseNullableUtc(r.GetValue(8))
    };
}
