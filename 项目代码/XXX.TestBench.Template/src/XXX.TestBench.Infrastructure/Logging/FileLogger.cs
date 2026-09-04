using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Infrastructure.Logging;

/// <summary>
/// 写入文件的日志实现。
/// </summary>
public sealed class FileLogger : IAppLogger, IDisposable
{
    /// <summary>
    /// 写日志的同步锁。
    /// </summary>
    private readonly object _gate = new();
    /// <summary>
    /// 日志文件写入器。
    /// </summary>
    private readonly StreamWriter _writer;

    /// <summary>
    /// 打开当天的日志文件，目录不存在时自动创建。
    /// </summary>
    public FileLogger(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"app-{DateTime.Now:yyyyMMdd}.log");
        _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            AutoFlush = true
        };
    }

    /// <summary>
    /// 记录一条普通信息。
    /// </summary>
    public void Info(string message) => Write("INFO", message, null);
    /// <summary>
    /// 记录一条警告。
    /// </summary>
    public void Warn(string message) => Write("WARN", message, null);
    /// <summary>
    /// 记录一条错误，可附带异常内容。
    /// </summary>
    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    /// <summary>
    /// 按级别写入一行日志，有异常时追加异常详情。
    /// </summary>
    private void Write(string level, string message, Exception? exception)
    {
        lock (_gate)
        {
            _writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}");
            if (exception is not null) _writer.WriteLine(exception);
        }
    }

    /// <summary>
    /// 关闭并释放日志文件。
    /// </summary>
    public void Dispose() => _writer.Dispose();
}
