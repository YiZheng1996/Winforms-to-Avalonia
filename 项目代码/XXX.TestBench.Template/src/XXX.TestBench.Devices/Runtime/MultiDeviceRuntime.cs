using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Ports;
using XXX.TestBench.Devices.Drivers;

namespace XXX.TestBench.Devices.Runtime;

/// <summary>
/// 多设备运行时稳定门面。调用方只持有该对象，配置切换时由上层替换其会话集合。
/// </summary>
public sealed class MultiDeviceRuntime : IDeviceRuntime
{
    private readonly DeviceConfigurationSnapshot _snapshot;
    private readonly ChannelManager _channels;
    private readonly IReadOnlyDictionary<string, DeviceSession> _sessions;
    private int _started;
    private int _disposed;

    public MultiDeviceRuntime(
        DeviceConfigurationSnapshot snapshot,
        IClock clock,
        DriverRegistry? drivers = null)
    {
        _snapshot = snapshot;
        var registry = drivers ?? DriverRegistry.CreateDefault();
        MultiDeviceConfigurationValidator.EnsureValid(snapshot, registry.Descriptors);

        _channels = new ChannelManager(snapshot.Device.Channels);
        var channelMap = snapshot.Device.Channels.ToDictionary(channel => channel.Id, StringComparer.OrdinalIgnoreCase);
        var driverByDevice = snapshot.Device.Devices
            .ToDictionary(device => device.Id, device => device.DriverKey, StringComparer.OrdinalIgnoreCase);
        var pointMap = snapshot.Points.Points
            .Where(point => point.IsEnabled)
            .GroupBy(point => point.DeviceId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(point =>
                point.ToDomain(driverByDevice[group.Key], snapshot.Revision)).ToList(), StringComparer.OrdinalIgnoreCase);

        _sessions = snapshot.Device.Devices
            .ToDictionary(
                device => device.Id,
                device => new DeviceSession(
                    device,
                    channelMap[device.ChannelId],
                    pointMap.TryGetValue(device.Id, out var points) ? points : Array.Empty<DevicePoint>(),
                    snapshot.Simulation,
                    clock,
                    _channels,
                    snapshot.Revision),
                StringComparer.OrdinalIgnoreCase);
    }

    public string Name => "多设备运行时";
    public DeviceMode Mode => _snapshot.Device.DeviceMode;
    public bool IsSimulation => Mode == DeviceMode.Simulation;
    public string ActiveRevision => _snapshot.Revision;
    public SignalBindingsConfig SignalBindings => _snapshot.SignalBindings;
    public IReadOnlyCollection<DeviceSession> Sessions => _sessions.Values.ToArray();
    public ChannelManager Channels => _channels;

    public DeviceRuntimeInfo Status
    {
        get
        {
            var statuses = ListDeviceStatuses();
            var activeStatuses = statuses
                .Where(status => status.ConnectionState != DeviceConnectionState.Disabled)
                .ToList();
            var enabledCount = activeStatuses.Count;
            var onlineCount = activeStatuses.Count(status => status.ConnectionState is DeviceConnectionState.Online or DeviceConnectionState.Degraded);
            var health = enabledCount == 0
                ? DeviceHealth.Faulted
                : onlineCount == 0
                    ? activeStatuses.Any(status => status.ConnectionState == DeviceConnectionState.Unknown)
                        ? DeviceHealth.Unknown
                        : DeviceHealth.Faulted
                    : onlineCount == enabledCount
                        ? DeviceHealth.Healthy
                        : DeviceHealth.Degraded;
            var connectionState = health switch
            {
                DeviceHealth.Healthy => DeviceConnectionState.Online,
                DeviceHealth.Degraded => DeviceConnectionState.Degraded,
                DeviceHealth.Faulted => DeviceConnectionState.Faulted,
                _ => DeviceConnectionState.Unknown
            };
            var lastError = string.Join("；", statuses
                .Where(status => !string.IsNullOrWhiteSpace(status.LastError))
                .Select(status => $"{status.DeviceId}:{status.LastError}"));
            return new DeviceRuntimeInfo(
                Name,
                "MultiDevice",
                "multi://active",
                IsSimulation,
                health,
                onlineCount > 0,
                string.IsNullOrWhiteSpace(lastError) ? null : lastError,
                "",
                "",
                ActiveRevision,
                statuses.Select(status => status.ConnectionGeneration).DefaultIfEmpty().Max(),
                connectionState);
        }
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _started, 1) != 0) return;
        if (!_sessions.Values.Any(session => session.ConnectionState != DeviceConnectionState.Disabled))
            throw new DomainException("当前配置没有启用设备，不能启动多设备运行时");

        await Task.WhenAll(_sessions.Values.Select(session => session.StartAsync(ct)));
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _started, 0) == 0 && _sessions.Count == 0) return;
        await Task.WhenAll(_sessions.Values.Select(session => session.StopAsync(ct)));
        await _channels.StopAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await StopAsync();
        foreach (var session in _sessions.Values)
            await session.DisposeAsync();
        await _channels.DisposeAsync();
    }

    public Task<IReadOnlyList<DevicePoint>> ListPointsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<DevicePoint>>(_sessions.Values
            .SelectMany(session => session.Points)
            .ToList());
    }

    public Task<PointValue> ReadAsync(DevicePoint point, CancellationToken ct = default)
        => ResolveSession(point).ReadAsync(point, ct);

    public Task<PointValue> ReadFreshAsync(string pointId, CancellationToken ct = default)
        => ResolveSession(pointId).ReadFreshAsync(pointId, ct);

    public Task<PointValue> WriteAsync(DevicePoint point, object? value, CancellationToken ct = default)
        => ResolveSession(point).WriteAsync(point, value, ct);

    public DeviceRuntimeInfo GetDeviceStatus(string deviceId)
        => _sessions.TryGetValue(deviceId.Trim(), out var session)
            ? session.Status
            : throw new DomainException($"设备不存在：{deviceId}");

    public IReadOnlyList<DeviceRuntimeInfo> ListDeviceStatuses()
        => _sessions.Values.Select(session => session.Status).ToList();

    public DevicePoint? GetPoint(string pointId)
        => _sessions.Values.Select(session => session.GetPoint(pointId)).FirstOrDefault(point => point is not null);

    private DeviceSession ResolveSession(DevicePoint point)
    {
        if (string.IsNullOrWhiteSpace(point.PointId))
            throw new DomainException("多设备运行时只接受带 PointId 的点位对象，拒绝使用旧地址对象");
        return ResolveSession(point.PointId);
    }

    private DeviceSession ResolveSession(string pointId)
    {
        var matches = _sessions.Values.Where(session => session.GetPoint(pointId) is not null).ToList();
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new DomainException($"点位不存在或未启用：{pointId}"),
            _ => throw new DomainException($"PointId 不唯一：{pointId}")
        };
    }
}
