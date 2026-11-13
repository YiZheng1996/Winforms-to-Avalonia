using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Infrastructure.Configuration;
using XXX.TestBench.Infrastructure.Persistence;

namespace XXX.TestBench.App.Composition;

/// <summary>
/// 启动预检通过后移交组合根的设置、同源数据库服务和进程锁。
/// </summary>
public sealed class ApplicationBootstrapResult : IDisposable
{
    private int _disposed;

    internal ApplicationBootstrapResult(
        SystemSettings settings,
        DatabaseServices database,
        InstallationProcessLock processLock)
    {
        Settings = settings;
        Database = database;
        ProcessLock = processLock;
    }

    public SystemSettings Settings { get; }
    public DatabaseServices Database { get; }
    private InstallationProcessLock ProcessLock { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Database.Dispose();
        ProcessLock.Dispose();
    }
}
