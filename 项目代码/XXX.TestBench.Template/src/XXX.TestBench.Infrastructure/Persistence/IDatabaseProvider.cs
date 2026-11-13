using XXX.TestBench.Core.Configuration;

namespace XXX.TestBench.Infrastructure.Persistence;

/// <summary>
/// 数据库 Provider 边界。测试配置只读；打开已存在库与创建新库是两个显式入口。
/// </summary>
public interface IDatabaseProvider
{
    string Key { get; }
    string DisplayName { get; }
    IReadOnlySet<string> Capabilities { get; }

    Task<DatabaseProviderTestResult> TestAsync(
        DatabaseProfile profile,
        CancellationToken cancellationToken = default);

    Task<DatabaseServices> OpenExistingAsync(
        DatabaseProfile profile,
        CancellationToken cancellationToken = default);

    Task<DatabaseServices> CreateNewAsync(
        DatabaseProfile profile,
        string storageId,
        string initialAdministratorPassword,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 只读连接测试结果；不包含连接字符串、账号密码或数据库数据。
/// </summary>
public sealed record DatabaseProviderTestResult(
    bool Succeeded,
    string Message,
    int? SchemaVersion,
    string? DatabaseInstanceId,
    string? StorageId);
