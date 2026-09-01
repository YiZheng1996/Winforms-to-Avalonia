using Microsoft.Data.Sqlite;

namespace XXX.TestBench.Infrastructure.Persistence;

/// <summary>SQLite 连接工厂：保证目录存在、外键开启、WAL 模式。</summary>
public interface ISqliteConnectionFactory
{
    string DatabasePath { get; }
    SqliteConnection Open();
    SqliteConnection OpenReadOnly();
}

public sealed class SqliteConnectionFactory : ISqliteConnectionFactory
{
    private readonly string _databasePath;

    public SqliteConnectionFactory(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath)) ?? ".");
        _databasePath = Path.GetFullPath(databasePath);
    }

    public string DatabasePath => _databasePath;

    public SqliteConnection Open()
    {
        var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            Pooling = true
        }.ToString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL;";
        cmd.ExecuteNonQuery();
        return conn;
    }

    public SqliteConnection OpenReadOnly()
    {
        var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            ForeignKeys = true,
            Pooling = true
        }.ToString());
        conn.Open();
        return conn;
    }
}
