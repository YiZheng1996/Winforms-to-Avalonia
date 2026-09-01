using Microsoft.Data.Sqlite;

namespace XXX.TestBench.Infrastructure.Persistence.Repositories;

public abstract class SqliteRepositoryBase
{
    private readonly ISqliteConnectionFactory _factory;

    protected SqliteRepositoryBase(ISqliteConnectionFactory factory) => _factory = factory;

    protected SqliteConnection OpenConnection() => SqliteAmbient.Current ?? _factory.Open();

    protected static bool OwnsConnection => SqliteAmbient.Current is null;

    protected static string? ParseNullableString(object? value) => value is DBNull or null ? null : Convert.ToString(value);

    protected static DateTime ParseUtc(string value) => DateTime.Parse(value, null, System.Globalization.DateTimeStyles.RoundtripKind);

    protected static DateTime? ParseNullableUtc(object? value) => value is DBNull or null ? null : ParseUtc(Convert.ToString(value)!);
}
