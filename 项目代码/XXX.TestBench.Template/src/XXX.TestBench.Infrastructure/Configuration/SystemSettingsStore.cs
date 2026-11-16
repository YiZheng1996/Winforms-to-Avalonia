using System.Text.Json;
using System.Text.Json.Serialization;
using XXX.TestBench.Core.Configuration;

namespace XXX.TestBench.Infrastructure.Configuration;

/// <summary>
/// 本机版本化设置的原子文件存储。读取不创建文件或目录，写入必须携带预期 Revision。
/// </summary>
public sealed class SystemSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true
    };

    private readonly string _path;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _fileGateTimeout;

    public SystemSettingsStore(string path, TimeProvider timeProvider, TimeSpan fileGateTimeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("设置文件路径必须是绝对路径", nameof(path));
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (fileGateTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(fileGateTimeout));
        _path = Path.GetFullPath(path);
        _timeProvider = timeProvider;
        _fileGateTimeout = fileGateTimeout;
    }

    public async Task<SystemSettings?> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path)) return null;
        await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read,
            4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var settings = await JsonSerializer.DeserializeAsync<SystemSettings>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("system-settings.json 为空");
        var errors = settings.Validate();
        if (errors.Count > 0) throw new InvalidDataException("system-settings.json 无效：" + string.Join("；", errors));
        return settings;
    }

    /// <summary>
    /// 以 Revision 条件保存。上一份完整 JSON 原子备份到 .previous，候选写入成功后再替换当前文件。
    /// </summary>
    public async Task SaveAsync(
        SystemSettings settings,
        long? expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var validation = settings.Validate();
        if (validation.Count > 0) throw new InvalidDataException("system-settings.json 无效：" + string.Join("；", validation));

        var directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("设置文件路径没有父目录");
        Directory.CreateDirectory(directory);
        await using var gate = await AcquireFileGateAsync(_path + ".lock", cancellationToken);
        var current = await ReadAsync(cancellationToken);
        if (current is null)
        {
            if (expectedRevision is not null || settings.Revision != 0)
                throw new InvalidOperationException("system-settings.json 不存在，首次保存 Revision 必须为 0");
        }
        else if (expectedRevision != current.Revision || settings.Revision != checked(current.Revision + 1))
        {
            throw new InvalidOperationException("system-settings.json Revision 冲突；请重新读取后再保存");
        }

        var bytes = JsonSerializer.SerializeToUtf8Bytes(settings, JsonOptions);
        var temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var previousTemp = _path + ".previous." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            if (current is not null)
            {
                File.Copy(_path, previousTemp, overwrite: false);
                using (var previous = new FileStream(previousTemp, FileMode.Open, FileAccess.ReadWrite, FileShare.None,
                           4096, FileOptions.WriteThrough))
                    previous.Flush(flushToDisk: true);
            }

            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (current is not null)
                File.Move(previousTemp, _path + ".previous", overwrite: true);
            File.Move(temp, _path, overwrite: current is not null);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
            if (File.Exists(previousTemp)) File.Delete(previousTemp);
        }
    }

    private async Task<FileStream> AcquireFileGateAsync(string gatePath, CancellationToken cancellationToken)
    {
        var startedAt = _timeProvider.GetTimestamp();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_timeProvider.GetElapsedTime(startedAt) >= _fileGateTimeout)
                throw new TimeoutException("等待 system-settings 文件门超时");
            try
            {
                return new FileStream(gatePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
                    1, FileOptions.Asynchronous | FileOptions.WriteThrough);
            }
            catch (IOException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), _timeProvider, cancellationToken);
            }
        }
    }
}
