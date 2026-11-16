using System.Text.Json;
using System.Text.Json.Serialization;
using XXX.TestBench.Core.Domain.Execution;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Infrastructure.Configuration;

/// <summary>
/// 保存与读取安装级 operation-fence。此目录是现场持久状态，不得放入缓存清理路径。
/// </summary>
public sealed class OperationFenceStore : IOperationEvidenceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = false
    };

    private readonly string _fenceDirectory;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _fileGateTimeout;

    public OperationFenceStore(string stateRoot, TimeProvider timeProvider, TimeSpan fileGateTimeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateRoot);
        if (!Path.IsPathFullyQualified(stateRoot))
            throw new ArgumentException("StateRoot 必须是绝对路径", nameof(stateRoot));
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (fileGateTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(fileGateTimeout), "文件门等待期限必须大于零");
        _fenceDirectory = Path.Combine(Path.GetFullPath(stateRoot), "operations");
        _timeProvider = timeProvider;
        _fileGateTimeout = fileGateTimeout;
    }

    /// <summary>
    /// 读取指定操作标记；目录或文件不存在表示尚无本地标记，不代表数据库也无未闭合状态。
    /// </summary>
    public async Task<OperationFence?> ReadAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        if (operationId == Guid.Empty) throw new ArgumentException("OperationId 不能为空", nameof(operationId));
        var path = GetFencePath(operationId);
        if (!File.Exists(path)) return null;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var fence = await JsonSerializer.DeserializeAsync<OperationFence>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException($"operation-fence 为空：{path}");
        fence.EnsureValid();
        if (fence.OperationId != operationId)
            throw new InvalidDataException("operation-fence 文件名与内容中的 OperationId 不一致");
        return fence;
    }

    /// <summary>
    /// 列出所有有效标记。任一标记损坏或版本未知都会让启动恢复检查失败，不静默跳过。
    /// </summary>
    public async Task<IReadOnlyList<OperationFence>> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_fenceDirectory)) return Array.Empty<OperationFence>();
        var result = new List<OperationFence>();
        foreach (var path in Directory.EnumerateFiles(_fenceDirectory, "*.json", SearchOption.TopDirectoryOnly)
                     .OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var fence = await JsonSerializer.DeserializeAsync<OperationFence>(stream, JsonOptions, cancellationToken)
                ?? throw new InvalidDataException($"operation-fence 为空：{path}");
            fence.EnsureValid();
            if (!string.Equals(Path.GetFileName(path), GetFenceFileName(fence.OperationId), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"operation-fence 文件名与内容中的 OperationId 不一致：{path}");
            result.Add(fence);
        }
        return result;
    }

    /// <summary>
    /// 以 Revision 条件更新标记，并在同目录原子替换；首次写入的 expectedRevision 必须为空。
    /// </summary>
    public async Task WriteAsync(
        OperationFence next,
        long? expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(next);
        next.EnsureValid();
        var path = GetFencePath(next.OperationId);
        Directory.CreateDirectory(_fenceDirectory);
        await using var gate = await AcquireFileGateAsync(path + ".lock", cancellationToken);
        var current = await ReadAsync(next.OperationId, cancellationToken);
        if (current is null)
        {
            if (expectedRevision is not null || next.Revision != 0)
                throw new InvalidOperationException("operation-fence 不存在，不能以旧 Revision 覆盖或跳过初始版本");
        }
        else if (expectedRevision != current.Revision || next.Revision != checked(current.Revision + 1))
        {
            throw new InvalidOperationException("operation-fence Revision 冲突；必须先重新读取当前状态");
        }
        else
        {
            EnsureValidTransition(current, next);
        }

        var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(next, JsonOptions);
            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(tempPath, path, overwrite: current is not null);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    /// <summary>
    /// 仅能清除已保存收尾证据且不再可能继续发出动作的标记。
    /// </summary>
    public async Task ClearAfterSafeStopAsync(
        Guid operationId,
        string benchId,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(benchId);
        var path = GetFencePath(operationId);
        await using var gate = await AcquireFileGateAsync(path + ".lock", cancellationToken);
        var current = await ReadAsync(operationId, cancellationToken)
            ?? throw new InvalidOperationException("operation-fence 已不存在，不能确认清理结果");
        if (!string.Equals(current.BenchId, benchId, StringComparison.Ordinal)
            || current.Revision != expectedRevision)
            throw new InvalidOperationException("operation-fence 所属台架或 Revision 不匹配，拒绝清除");
        if (!current.SafeStopConfirmed || current.ActionMayHaveBeenIssued
            || string.IsNullOrWhiteSpace(current.LastSafeStopJson)
            || string.IsNullOrWhiteSpace(current.LastEvidenceKey))
            throw new InvalidOperationException("缺少已持久化的安全收尾确认和动作证据，拒绝清除恢复标记");

        cancellationToken.ThrowIfCancellationRequested();
        File.Delete(path);
    }

    private string GetFencePath(Guid operationId) => Path.Combine(_fenceDirectory, GetFenceFileName(operationId));

    private static string GetFenceFileName(Guid operationId) => operationId.ToString("D").ToLowerInvariant() + ".json";

    private static void EnsureValidTransition(OperationFence current, OperationFence next)
    {
        BenchPhaseTransitions.EnsureTransition(current.Phase, next.Phase);
        if (current.OperationId != next.OperationId
            || !string.Equals(current.BenchId, next.BenchId, StringComparison.Ordinal)
            || !string.Equals(current.NodeId, next.NodeId, StringComparison.Ordinal)
            || !string.Equals(current.DatabaseInstanceId, next.DatabaseInstanceId, StringComparison.Ordinal)
            || !string.Equals(current.DeviceConfigurationRevision, next.DeviceConfigurationRevision, StringComparison.Ordinal))
            throw new InvalidOperationException("operation-fence 操作/台架/节点/数据库/设备版本身份不可替换");
        if (current.RunId is not null && current.RunId != next.RunId)
            throw new InvalidOperationException("operation-fence 已绑定 RunId 后不能改绑或清空");
        if (current.ActionMayHaveBeenIssued && !next.ActionMayHaveBeenIssued
            && (!next.SafeStopConfirmed || string.IsNullOrWhiteSpace(next.LastSafeStopJson)
                || string.IsNullOrWhiteSpace(next.LastEvidenceKey)))
            throw new InvalidOperationException("可能已发出设备动作的标记只能在安全收尾证据已保存后清除");
        if (current.SafeStopConfirmed && !next.SafeStopConfirmed)
            throw new InvalidOperationException("已确认的安全收尾状态不能回退");
    }

    private async Task<FileStream> AcquireFileGateAsync(string gatePath, CancellationToken cancellationToken)
    {
        var startedAt = _timeProvider.GetTimestamp();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_timeProvider.GetElapsedTime(startedAt) >= _fileGateTimeout)
                throw new TimeoutException("等待 operation-fence 文件门超时");
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
