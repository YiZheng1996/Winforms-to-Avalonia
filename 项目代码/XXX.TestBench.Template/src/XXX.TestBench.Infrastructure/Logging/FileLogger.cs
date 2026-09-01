using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Infrastructure.Logging;

public sealed class FileLogger : IAppLogger, IDisposable
{
    private readonly object _gate = new();
    private readonly StreamWriter _writer;

    public FileLogger(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"app-{DateTime.Now:yyyyMMdd}.log");
        _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            AutoFlush = true
        };
    }

    public void Info(string message) => Write("INFO", message, null);
    public void Warn(string message) => Write("WARN", message, null);
    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        lock (_gate)
        {
            _writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}");
            if (exception is not null) _writer.WriteLine(exception);
        }
    }

    public void Dispose() => _writer.Dispose();
}
