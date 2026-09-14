using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Ports;
using XXX.TestBench.Devices.Drivers;
using XXX.TestBench.Devices.Siemens;
using XXX.TestBench.Devices.Modbus;

namespace XXX.TestBench.Devices.Runtime;

/// <summary>
/// 多设备运行时稳定门面。调用方只持有该对象，配置切换时由上层替换其会话集合。
/// </summary>
public sealed class MultiDeviceRuntime : IDeviceRuntime
{
    private readonly DeviceConfigurationSnapshot _snapshot;
    private readonly ChannelManager _channels;
    private readonly IReadOnlyDictionary<string, DeviceSession> _sessions;
    private readonly DeviceMode _mode;
    private readonly IS7PlcClientFactory? _s7ClientFactory;
    private readonly IDeviceEventSink? _events;
    private readonly IModbusClientFactory? _modbusClientFactory;
    private RuntimeActivationReport _activationReport = RuntimeActivationReport.Empty;
    private int _started;
    private int _disposed;

    public MultiDeviceRuntime(
        DeviceConfigurationSnapshot snapshot,
        IClock clock,
        DriverRegistry? drivers = null,
        IS7PlcClientFactory? s7ClientFactory = null,
        IDeviceEventSink? events = null,
        IModbusClientFactory? modbusClientFactory = null)
    {
        _snapshot = snapshot;
        _mode = ResolveAggregateMode(snapshot.Device.Devices);
        var registry = drivers ?? DriverRegistry.CreateDefault();
        MultiDeviceConfigurationValidator.EnsureValid(snapshot, registry.Descriptors);
        _s7ClientFactory = s7ClientFactory;
        _events = events;
        _modbusClientFactory = modbusClientFactory;

        _channels = new ChannelManager(snapshot.Device.Channels, _modbusClientFactory);
        var channelMap = snapshot.Device.Channels.ToDictionary(channel => channel.Id, StringComparer.OrdinalIgnoreCase);
        var driverByDevice = snapshot.Device.Devices
            .ToDictionary(device => device.Id, device => device.DriverKey, StringComparer.OrdinalIgnoreCase);
        var pointMap = snapshot.Points.Points
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
                    snapshot.Revision,
                    _s7ClientFactory,
                    _events,
                    _modbusClientFactory),
                StringComparer.OrdinalIgnoreCase);
    }

    public string Name => "多设备运行时";
    public DeviceMode Mode => _mode;
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
            var activeStatuses = statuses.ToList();
            var enabledCount = activeStatuses.Count;
            var onlineCount = activeStatuses.Count(status => status.ConnectionState is DeviceConnectionState.Online or DeviceConnectionState.Degraded);
            var hasConnecting = activeStatuses.Any(status => status.ConnectionState == DeviceConnectionState.Connecting);
            var hasFault = activeStatuses.Any(status => status.ConnectionState == DeviceConnectionState.Faulted);
            var health = enabledCount == 0
                ? DeviceHealth.Faulted
                : onlineCount == 0
                    ? hasConnecting
                        ? DeviceHealth.Degraded
                        : activeStatuses.Any(status => status.ConnectionState == DeviceConnectionState.Unknown)
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
            if (onlineCount == 0 && hasConnecting && !hasFault)
                connectionState = DeviceConnectionState.Connecting;
            var lastError = string.Join("；", statuses
                .Where(status => !string.IsNullOrWhiteSpace(status.LastError))
                .Select(status => $"{status.DeviceId}:{status.LastError}"));
            return new DeviceRuntimeInfo(
                Name,
                "多设备运行时",
                "multi://active",
                IsSimulation,
                health,
                onlineCount > 0,
                string.IsNullOrWhiteSpace(lastError) ? null : lastError,
                "",
                "",
                ActiveRevision,
                statuses.Select(status => status.ConnectionGeneration).DefaultIfEmpty().Max(),
                connectionState,
                Mode,
                Latest(statuses, status => status.LastSuccessUtc),
                Latest(statuses, status => status.LastFailureUtc),
                statuses.Sum(status => status.ConsecutiveFailures),
                Latest(statuses, status => status.DemotedUntilUtc),
                statuses.Any(status => status.NegotiatedPduSize.HasValue)
                    ? statuses.Where(status => status.NegotiatedPduSize.HasValue).Max(status => status.NegotiatedPduSize!.Value)
                    : null);
        }
    }

    public RuntimeActivationReport ActivationReport => _activationReport;

    public async Task StartAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _started, 1) != 0) return;
        if (_sessions.Count == 0)
            throw new DomainException("当前配置没有设备，不能启动多设备运行时");

        await _channels.StartAsync(ct);
        var sessions = _sessions.Values.ToArray();
        var results = await Task.WhenAll(sessions.Select(session => session.StartAsync(ct)));
        var issues = new List<DeviceActivationIssue>();
        for (var index = 0; index < results.Length; index++)
        {
            if (results[index].Ok) continue;
            var message = results[index].Error ?? sessions[index].Status.LastError ?? "设备启动失败";
            issues.Add(new DeviceActivationIssue(sessions[index].DeviceId, sessions[index].Status.Name, message));
        }
        _activationReport = new RuntimeActivationReport(sessions.Length, results.Count(result => result.Ok), issues);
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _started, 0) == 0 && _sessions.Count == 0) return;
        await Task.WhenAll(_sessions.Values.Select(session => session.StopAsync(ct)));
        await _channels.DrainAndStopAsync(ct);
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

    public async Task<IReadOnlyList<PointValue>> ReadManyFreshAsync(
        IReadOnlyCollection<string> pointIds,
        CancellationToken ct = default)
    {
        var requests = pointIds.Select(pointId => (pointId, session: ResolveSession(pointId))).ToList();
        var groups = requests.GroupBy(item => item.session);
        var results = await Task.WhenAll(groups.Select(async group =>
            await group.Key.ReadManyFreshAsync(group.Select(item => item.pointId).ToArray(), ct)));
        var byId = results.SelectMany(values => values).ToDictionary(value => value.PointId, StringComparer.OrdinalIgnoreCase);
        return pointIds.Where(pointId => byId.ContainsKey(pointId)).Select(pointId => byId[pointId]).ToList();
    }

    public Task<PointValue> WriteAsync(DevicePoint point, object? value, CancellationToken ct = default)
        => ResolveSession(point).WriteAsync(point, value, ct);

    public DeviceRuntimeInfo GetDeviceStatus(string deviceId)
        => _sessions.TryGetValue(deviceId.Trim(), out var session)
            ? session.Status
            : throw new DomainException($"设备不存在：{deviceId}");

    public IReadOnlyList<DeviceRuntimeInfo> ListDeviceStatuses()
        => _sessions.Values.Select(session => session.Status).ToList();

    /// <summary>
    /// 连接测试使用的只读串口占用查询，不会创建、打开或关闭任何资源。
    /// </summary>
    public bool IsSerialPortLeased(string portName) => _channels.IsSerialPortLeased(portName);

    public bool TryGetCachedValue(string pointId, out PointValue value)
    {
        var matches = _sessions.Values.Where(session => session.GetPoint(pointId) is not null).ToList();
        if (matches.Count == 1) return matches[0].TryGetCachedValue(pointId, out value);
        value = default!;
        return false;
    }

    public IReadOnlyList<PointValue> ListCachedValues()
        => _sessions.Values.SelectMany(session => session.ListCachedValues()).ToList();

    public DevicePoint? GetPoint(string pointId)
        => _sessions.Values.Select(session => session.GetPoint(pointId)).FirstOrDefault(point => point is not null);

    private DeviceSession ResolveSession(DevicePoint point)
    {
        if (string.IsNullOrWhiteSpace(point.PointId))
            throw new DomainException("多设备运行时只接受带 PointId 的点位对象，拒绝使用旧地址对象");
        return ResolveSession(point.PointId);
    }

    private static DeviceMode ResolveAggregateMode(IEnumerable<DeviceConfig.DeviceEntry> devices)
    {
        var modes = devices
            .Where(device => device is not null)
            .Select(device => device.DeviceMode)
            .Distinct()
            .ToList();
        return modes.Count switch
        {
            0 => DeviceMode.Simulation,
            1 => modes[0],
            _ => DeviceMode.Mixed
        };
    }

    private static DateTime? Latest(
        IEnumerable<DeviceRuntimeInfo> statuses,
        Func<DeviceRuntimeInfo, DateTime?> selector)
    {
        DateTime? latest = null;
        foreach (var status in statuses)
        {
            var candidate = selector(status);
            if (candidate.HasValue && (!latest.HasValue || candidate.Value > latest.Value))
                latest = candidate;
        }
        return latest;
    }

    private DeviceSession ResolveSession(string pointId)
    {
        var matches = _sessions.Values.Where(session => session.GetPoint(pointId) is not null).ToList();
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new DomainException($"点位不存在：{pointId}"),
            _ => throw new DomainException($"PointId 不唯一：{pointId}")
        };
    }
}
