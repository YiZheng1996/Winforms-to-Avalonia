using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Core.Application;

/// <summary>
/// UI 无关的诊断目标快照。只携带稳定身份和显示所需的只读文字。
/// </summary>
public sealed record DevicePointDiagnosticTarget(
    string PointId,
    string DeviceId,
    string PointName,
    string Address,
    string DataTypeText,
    string ChannelId);

/// <summary>
/// 单点诊断结果状态。Stale/Bad 与成功严格区分。
/// </summary>
public enum DevicePointDiagnosticResultKind
{
    Good,
    Stale,
    Bad,
    Failed,
    Discarded,
    Cancelled
}

/// <summary>
/// 单点诊断结果，不包含任何 UI 状态或 Avalonia 类型。
/// </summary>
public sealed record DevicePointDiagnosticReadItem(
    DevicePointDiagnosticTarget Target,
    DevicePointDiagnosticResultKind Kind,
    PointValue? Value,
    string? Error);

/// <summary>
/// 一次诊断读取结果，顺序与请求目标一致。
/// </summary>
public sealed record DevicePointDiagnosticReadResult(
    IReadOnlyList<DevicePointDiagnosticReadItem> Items)
{
    public int GoodCount => Items.Count(item => item.Kind == DevicePointDiagnosticResultKind.Good);
    public int StaleCount => Items.Count(item => item.Kind == DevicePointDiagnosticResultKind.Stale);
    public int BadCount => Items.Count(item => item.Kind == DevicePointDiagnosticResultKind.Bad);
    public int FailedCount => Items.Count(item => item.Kind == DevicePointDiagnosticResultKind.Failed);
    public int DiscardedCount => Items.Count(item => item.Kind == DevicePointDiagnosticResultKind.Discarded);
    public int CancelledCount => Items.Count(item => item.Kind == DevicePointDiagnosticResultKind.Cancelled);
    public bool Succeeded => Items.Count > 0
        && Items.All(item => item.Kind == DevicePointDiagnosticResultKind.Good);
}

/// <summary>
/// 诊断读取实现：共享手工操作守卫，同一通道串行，不同通道最多四路并发，
/// 并在返回前校验运行时、Revision、PointId、DeviceId 和连接代次。
/// </summary>
public sealed class DevicePointDiagnosticReader : IDevicePointDiagnosticReader
{
    private readonly DeviceManualOperationGuard _guard;
    private readonly Func<IDeviceRuntime?> _runtimeAccessor;
    private readonly IAppLogger? _logger;
    private readonly int _maxConcurrentChannels;

    public DevicePointDiagnosticReader(
        DeviceManualOperationGuard guard,
        Func<IDeviceRuntime?> runtimeAccessor,
        IAppLogger? logger = null,
        int maxConcurrentChannels = 4)
    {
        _guard = guard ?? throw new ArgumentNullException(nameof(guard));
        _runtimeAccessor = runtimeAccessor ?? throw new ArgumentNullException(nameof(runtimeAccessor));
        _logger = logger;
        _maxConcurrentChannels = maxConcurrentChannels > 0 ? maxConcurrentChannels : 4;
    }

    public Task<DevicePointDiagnosticReadResult> ReadAsync(
        IReadOnlyList<DevicePointDiagnosticTarget> targets,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(targets);
        return _guard.ExecuteAsync(token => ReadCoreAsync(targets, token), ct);
    }

    private async Task<DevicePointDiagnosticReadResult> ReadCoreAsync(
        IReadOnlyList<DevicePointDiagnosticTarget> targets,
        CancellationToken ct)
    {
        if (targets.Count == 0)
            return new DevicePointDiagnosticReadResult(Array.Empty<DevicePointDiagnosticReadItem>());

        var runtime = _runtimeAccessor()
            ?? throw new DomainException("设备运行时未初始化，不能读取点位");
        var revision = runtime.ActiveRevision ?? string.Empty;
        var indexedTargets = targets
            .Select((target, index) => new IndexedTarget(index, target))
            .ToList();
        var items = new DevicePointDiagnosticReadItem?[targets.Count];
        var channelGroups = indexedTargets
            .GroupBy(item => ResolveChannelId(runtime, item.Target), StringComparer.OrdinalIgnoreCase)
            .ToList();

        using var channelLimit = new SemaphoreSlim(_maxConcurrentChannels, _maxConcurrentChannels);
        try
        {
            await Task.WhenAll(channelGroups.Select(async channelGroup =>
            {
                await channelLimit.WaitAsync(ct);
                try
                {
                    // 一个通道由同一个物理资源承载，组内严格串行。
                    foreach (var indexedTarget in channelGroup)
                    {
                        ct.ThrowIfCancellationRequested();
                        items[indexedTarget.Index] = await ReadOneAsync(
                            runtime,
                            revision,
                            indexedTarget.Target,
                            ct);
                    }
                }
                finally
                {
                    channelLimit.Release();
                }
            }));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // 把已完成结果和未开始/被取消的目标一起返回，UI 可以保留真实进度，
            // 而不是因批量任务抛出取消异常而抹掉已经完成的行。
            return new DevicePointDiagnosticReadResult(items
                .Select((item, index) => item ?? new DevicePointDiagnosticReadItem(
                    targets[index],
                    DevicePointDiagnosticResultKind.Cancelled,
                    null,
                    "读取已取消"))
                .ToList());
        }

        return new DevicePointDiagnosticReadResult(items
            .Select((item, index) => item ?? new DevicePointDiagnosticReadItem(
                targets[index],
                DevicePointDiagnosticResultKind.Failed,
                null,
                "诊断结果缺失"))
            .ToList());
    }

    private async Task<DevicePointDiagnosticReadItem> ReadOneAsync(
        IDeviceRuntime runtime,
        string revision,
        DevicePointDiagnosticTarget target,
        CancellationToken ct)
    {
        if (!IsCurrentRuntime(runtime, revision))
            return Discarded(target, "运行配置已变化");

        DevicePoint? runtimePoint;
        try
        {
            runtimePoint = runtime.GetPoint(target.PointId);
        }
        catch (Exception ex)
        {
            _logger?.Warn($"点位诊断目标校验失败：{target.PointName}（{target.PointId}）: {ex.Message}");
            return new DevicePointDiagnosticReadItem(
                target,
                DevicePointDiagnosticResultKind.Failed,
                null,
                string.IsNullOrWhiteSpace(ex.Message) ? "目标点位校验失败" : ex.Message);
        }

        if (runtimePoint is null)
            return Discarded(target, "点位不在当前运行时，未执行读取");
        if (!string.Equals(runtimePoint.PointId, target.PointId, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(runtimePoint.DeviceId)
                && !string.Equals(runtimePoint.DeviceId, target.DeviceId, StringComparison.OrdinalIgnoreCase)))
            return Discarded(target, "当前运行时点位身份不匹配，未执行读取");

        var generation = GetConnectionGeneration(runtime, target.DeviceId);
        try
        {
            var value = await runtime.ReadFreshAsync(target.PointId, ct);
            ct.ThrowIfCancellationRequested();

            if (!IsCurrentRuntime(runtime, revision))
                return Discarded(target, "运行时已替换或配置变化");

            var currentGeneration = GetConnectionGeneration(runtime, target.DeviceId);
            var identityError = ValidateIdentity(
                revision,
                generation,
                currentGeneration,
                target,
                value);
            if (identityError is not null)
                return Discarded(target, identityError);

            var kind = value.Quality switch
            {
                PointQuality.Good => DevicePointDiagnosticResultKind.Good,
                PointQuality.Stale => DevicePointDiagnosticResultKind.Stale,
                PointQuality.Bad => DevicePointDiagnosticResultKind.Bad,
                _ => DevicePointDiagnosticResultKind.Failed
            };
            var error = kind == DevicePointDiagnosticResultKind.Failed
                ? "设备返回了未知数据质量"
                : null;
            return new DevicePointDiagnosticReadItem(target, kind, value, error);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            _logger?.Warn($"点位诊断读取超时：{target.PointName}（{target.PointId}）: {ex.Message}");
            return new DevicePointDiagnosticReadItem(
                target,
                DevicePointDiagnosticResultKind.Failed,
                null,
                string.IsNullOrWhiteSpace(ex.Message) ? "读取超时" : ex.Message);
        }
        catch (Exception ex)
        {
            _logger?.Warn($"点位诊断读取失败：{target.PointName}（{target.PointId}）: {ex.Message}");
            return new DevicePointDiagnosticReadItem(
                target,
                DevicePointDiagnosticResultKind.Failed,
                null,
                string.IsNullOrWhiteSpace(ex.Message) ? "读取失败" : ex.Message);
        }
    }

    private bool IsCurrentRuntime(IDeviceRuntime runtime, string revision)
    {
        var current = _runtimeAccessor();
        return ReferenceEquals(runtime, current)
            && string.Equals(revision, current?.ActiveRevision ?? string.Empty, StringComparison.Ordinal);
    }

    private static string? ValidateIdentity(
        string revision,
        long initialGeneration,
        long currentGeneration,
        DevicePointDiagnosticTarget target,
        PointValue value)
    {
        if (!string.IsNullOrWhiteSpace(value.Revision)
            && !string.IsNullOrWhiteSpace(revision)
            && !string.Equals(value.Revision, revision, StringComparison.Ordinal))
            return "返回了旧 Revision";
        if (!string.IsNullOrWhiteSpace(value.PointId)
            && !string.Equals(value.PointId, target.PointId, StringComparison.OrdinalIgnoreCase))
            return "返回 PointId 不匹配";
        if (!string.IsNullOrWhiteSpace(value.DeviceId)
            && !string.Equals(value.DeviceId, target.DeviceId, StringComparison.OrdinalIgnoreCase))
            return "返回 DeviceId 不匹配";
        if (initialGeneration > 0
            && currentGeneration > 0
            && initialGeneration != currentGeneration)
            return "连接代次已变化";
        if (initialGeneration > 0
            && value.ConnectionGeneration > 0
            && initialGeneration != value.ConnectionGeneration)
            return "连接代次已变化";
        return null;
    }

    private static long GetConnectionGeneration(IDeviceRuntime runtime, string deviceId)
    {
        try
        {
            return runtime.GetDeviceStatus(deviceId).ConnectionGeneration;
        }
        catch (DomainException)
        {
            return runtime.Status.ConnectionGeneration;
        }
    }

    private static string ResolveChannelId(
        IDeviceRuntime runtime,
        DevicePointDiagnosticTarget target)
    {
        if (!string.IsNullOrWhiteSpace(target.ChannelId))
            return target.ChannelId.Trim();
        try
        {
            var status = runtime.GetDeviceStatus(target.DeviceId);
            return string.IsNullOrWhiteSpace(status.ChannelId)
                ? target.DeviceId
                : status.ChannelId.Trim();
        }
        catch (DomainException)
        {
            return string.IsNullOrWhiteSpace(target.DeviceId) ? "unknown" : target.DeviceId.Trim();
        }
    }

    private static DevicePointDiagnosticReadItem Discarded(
        DevicePointDiagnosticTarget target,
        string reason)
        => new(target, DevicePointDiagnosticResultKind.Discarded, null, reason);

    private sealed record IndexedTarget(int Index, DevicePointDiagnosticTarget Target);
}
