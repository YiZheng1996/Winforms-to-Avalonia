namespace XXX.TestBench.Infrastructure.Configuration;

/// <summary>
/// 持有安装级独占文件句柄，避免同一 StateRoot 下多个进程同时启用设备。
/// </summary>
public sealed class InstallationProcessLock : IDisposable
{
    private readonly FileStream _stream;
    private int _disposed;

    private InstallationProcessLock(FileStream stream) => _stream = stream;

    public static InstallationProcessLock Acquire(string stateRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateRoot);
        if (!Path.IsPathFullyQualified(stateRoot))
            throw new ArgumentException("StateRoot 必须是绝对路径", nameof(stateRoot));

        var root = Path.GetFullPath(stateRoot);
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "instance.lock");
        FileStream stream;
        try
        {
            stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
                128, FileOptions.WriteThrough);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException("该安装的进程锁已被占用；不会启动第二个设备实例。", ex);
        }

        try
        {
            stream.SetLength(0);
            using var writer = new StreamWriter(stream, leaveOpen: true);
            writer.Write($"pid={Environment.ProcessId};startedUtc={DateTime.UtcNow:O}");
            writer.Flush();
            stream.Flush(flushToDisk: true);
            stream.Position = 0;
            return new InstallationProcessLock(stream);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stream.Dispose();
    }
}
