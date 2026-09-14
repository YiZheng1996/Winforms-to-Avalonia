using S7.Net;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Ports;
using XXX.TestBench.Devices.Drivers;
using XXX.TestBench.Devices.Siemens;

namespace XXX.TestBench.Devices.Runtime;

/// <summary>
/// 基于 S7NetPlus 的单设备运行时。S7NetPlus 只在 IS7PlcClient 适配器后使用。
/// </summary>
public sealed class S7NetPlusDeviceRuntime : IDeviceRuntime
{
    private readonly DeviceConfig.DeviceEntry _device;
    private readonly ChannelEntry _channel;
    private readonly IReadOnlyList<DevicePoint> _points;
    private readonly IReadOnlyDictionary<string, DevicePoint> _pointsById;
    private readonly IReadOnlyDictionary<string, DevicePoint> _pointsByAddress;
    private readonly IClock _clock;
    private readonly string _revision;
    private readonly IS7PlcClientFactory _clientFactory;
    private readonly IDeviceEventSink? _events;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly SemaphoreSlim _ioGate = new(1, 1);
    private IS7PlcClient? _client;
    private DeviceConnectionState _state = DeviceConnectionState.Unknown;
    private string? _lastError;
    private long _connectionGeneration;
    private DateTime? _lastSuccessUtc;
    private DateTime? _lastFailureUtc;
    private int _consecutiveFailures;
    private bool _disposed;
    private RuntimeActivationReport _activationReport;

    public S7NetPlusDeviceRuntime(
        DeviceConfig.DeviceEntry device,
        ChannelEntry channel,
        IReadOnlyList<DevicePoint> points,
        IClock clock,
        string revision,
        IS7PlcClientFactory? clientFactory = null,
        IDeviceEventSink? events = null)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _points = points ?? throw new ArgumentNullException(nameof(points));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _revision = revision ?? string.Empty;
        _clientFactory = clientFactory ?? new S7NetPlusPlcClientFactory();
        _events = events;
        _pointsById = _points
            .Where(point => !string.IsNullOrWhiteSpace(point.PointId))
            .ToDictionary(point => point.PointId, StringComparer.OrdinalIgnoreCase);
        _pointsByAddress = _points
            .Where(point => !string.IsNullOrWhiteSpace(point.Address))
            .ToDictionary(point => point.Address, StringComparer.OrdinalIgnoreCase);
        _activationReport = new RuntimeActivationReport(1, 0, Array.Empty<DeviceActivationIssue>());
    }

    public string Name => _device.Name;
    public DeviceMode Mode => DeviceMode.Hardware;
    public bool IsSimulation => false;
    public string ActiveRevision => _revision;
    public SignalBindingsConfig SignalBindings => new();
    public RuntimeActivationReport ActivationReport => _activationReport;

    public DeviceRuntimeInfo Status
    {
        get
        {
            var endpoint = ResolveEndpoint();
            var health = _state switch
            {
                DeviceConnectionState.Online => DeviceHealth.Healthy,
                DeviceConnectionState.Degraded => DeviceHealth.Degraded,
                DeviceConnectionState.Faulted => DeviceHealth.Faulted,
                DeviceConnectionState.Offline => DeviceHealth.Disconnected,
                _ => DeviceHealth.Unknown
            };
            return new DeviceRuntimeInfo(
                Name,
                "西门子 S7",
                $"{endpoint.Host}:{endpoint.Port} rack={endpoint.Rack} slot={endpoint.Slot}",
                IsSimulation: false,
                health,
                IsConnected: _client?.IsConnected == true && _state is (DeviceConnectionState.Online or DeviceConnectionState.Degraded),
                _lastError,
                _device.Id,
                _device.ChannelId,
                _revision,
                _connectionGeneration,
                _state,
                DeviceMode.Hardware,
                _lastSuccessUtc,
                _lastFailureUtc,
                _consecutiveFailures,
                null,
                _client?.MaxPduSize);
        }
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        await _lifecycle.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_client?.IsConnected == true)
            {
                _state = DeviceConnectionState.Online;
                _activationReport = new RuntimeActivationReport(1, 1, Array.Empty<DeviceActivationIssue>());
                return;
            }

            ValidateConfiguration();
            var endpoint = ResolveEndpoint();
            var cpu = ResolveCpuType(_device.Model);
            _state = DeviceConnectionState.Connecting;
            _lastError = null;
            Publish(DeviceEventSeverity.Information, "S7_CONNECTING", $"正在建立 S7 会话：{endpoint.Host}:{endpoint.Port} rack={endpoint.Rack} slot={endpoint.Slot}");
            await DisposeClientAsync();
            var timing = _device.Timing ?? new DeviceTimingOptions();
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(timing.ConnectTimeoutMs);
            _client = _clientFactory.Create(cpu, endpoint.Host, endpoint.Port, endpoint.Rack, endpoint.Slot,
                timing.RequestTimeoutMs);
            try
            {
                await _client.OpenAsync(connectCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && connectCts.IsCancellationRequested)
            {
                throw new DomainException($"西门子 S7 设备连接超时（{timing.ConnectTimeoutMs} ms）：{endpoint.Host}:{endpoint.Port}");
            }

            _connectionGeneration++;
            _state = DeviceConnectionState.Online;
            _consecutiveFailures = 0;
            _lastError = null;
            _activationReport = new RuntimeActivationReport(1, 1, Array.Empty<DeviceActivationIssue>());
            Publish(DeviceEventSeverity.Information, "S7_CONNECTED", $"TCP/S7 会话建立成功：{endpoint.Host}:{endpoint.Port}");
        }
        catch (OperationCanceledException)
        {
            _state = DeviceConnectionState.Unknown;
            throw;
        }
        catch (Exception ex)
        {
            _state = DeviceConnectionState.Faulted;
            _lastError = ex.Message;
            _lastFailureUtc = _clock.UtcNow;
            Publish(DeviceEventSeverity.Error, "S7_CONNECT_FAILED", ex.Message);
            await DisposeClientAsync();
            var issue = new DeviceActivationIssue(_device.Id, _device.Name, ex.Message, ex);
            _activationReport = new RuntimeActivationReport(1, 0, new[] { issue });
            if (ex is DomainException) throw;
            throw new DomainException($"西门子 S7 设备连接失败：{ex.Message}");
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        await _lifecycle.WaitAsync(ct);
        try
        {
            await DisposeClientAsync();
            _state = DeviceConnectionState.Offline;
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public Task<IReadOnlyList<DevicePoint>> ListPointsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_points);
    }

    public Task<PointValue> ReadAsync(DevicePoint point, CancellationToken ct = default)
        => ReadPointAsync(ResolvePoint(point), ct);

    public Task<PointValue> ReadFreshAsync(string pointId, CancellationToken ct = default)
        => ReadPointAsync(ResolvePoint(pointId), ct);

    public async Task<IReadOnlyList<PointValue>> ReadManyFreshAsync(
        IReadOnlyCollection<string> pointIds,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pointIds);
        var points = pointIds.Select(ResolvePoint).DistinctBy(point => point.PointId, StringComparer.OrdinalIgnoreCase).ToList();
        if (points.Count == 0) return Array.Empty<PointValue>();
        var validPoints = new List<DevicePoint>(points.Count);
        var values = new Dictionary<string, PointValue>(StringComparer.OrdinalIgnoreCase);
        foreach (var point in points)
        {
            if (TryValidatePoint(point, out var reason))
            {
                validPoints.Add(point);
                continue;
            }

            values[point.PointId] = CreateBadValue(point, reason!);
            Publish(DeviceEventSeverity.Warning, "S7_POINT_BAD", $"点位“{DisplayPoint(point)}”配置无效：{reason}");
        }
        if (validPoints.Count == 0)
            return points.Select(point => values[point.PointId]).ToList();
        EnsureOperational();

        await _ioGate.WaitAsync(ct);
        try
        {
            var pduSize = _client?.MaxPduSize ?? 960;
            var blocks = S7ReadBatchPlanner.Plan(validPoints, pduSize);
            foreach (var block in blocks)
            {
                ct.ThrowIfCancellationRequested();
                byte[] bytes;
                try
                {
                    bytes = await _client!.ReadBytesAsync(block.Area, block.DbNumber, block.StartByte,
                        block.ByteCount, ct);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    RegisterFailure(ex);
                    var eventCode = ex is TimeoutException ? "S7_REQUEST_TIMEOUT" : "S7_POINT_BAD";
                    foreach (var item in block.Items)
                        Publish(DeviceEventSeverity.Warning, eventCode, $"点位“{DisplayPoint(item.Point)}”读取失败：{ex.Message}");
                    // 把通信异常交给 DeviceSession 做唯一一层重试；不能在此处伪造一批 Bad
                    // 值并让上层误以为请求已经完成。
                    throw;
                }

                foreach (var item in block.Items)
                {
                    try
                    {
                        var raw = DecodeRaw(item.Point, item.Address, bytes, item.OffsetInBlock);
                        values[item.Point.PointId] = CreateGoodValue(item.Point, raw);
                    }
                    catch (Exception ex)
                    {
                        values[item.Point.PointId] = CreateBadValue(item.Point, ex.Message);
                        Publish(DeviceEventSeverity.Warning, "S7_POINT_BAD",
                            $"点位“{DisplayPoint(item.Point)}”解码失败：{ex.Message}");
                    }
                }
                RegisterSuccess();
            }
            return points.Select(point => values.TryGetValue(point.PointId, out var value)
                ? value
                : CreateBadValue(point, "S7 读取未返回该点位数据")).ToList();
        }
        finally
        {
            _ioGate.Release();
        }
    }

    public async Task<PointValue> WriteAsync(DevicePoint point, object? value, CancellationToken ct = default)
    {
        var current = ResolvePoint(point);
        if (!current.IsWritable)
            throw new DomainException($"点位“{DisplayPoint(current)}”不可写");
        ValidatePoints(new[] { current });
        EnsureOperational();
        var address = S7AddressParser.Parse(current.Address);
        // DeviceWritePipeline 已完成工程值→原始值换算；协议运行时只负责写入原始值。
        if (value is null)
            throw new DomainException($"点位“{DisplayPoint(current)}”的写入原始值不能为空");
        var rawValue = value;

        await _ioGate.WaitAsync(ct);
        try
        {
            if (current.DataType is DevicePointDataType.Boolean or DevicePointDataType.Bool)
            {
                if (address.Shape != S7AddressShape.Bit || !address.BitIndex.HasValue)
                    throw new DomainException($"点位“{DisplayPoint(current)}”的布尔地址不是位地址");
                await _client!.WriteBitAsync(address.Area, address.DbNumber ?? 0, address.StartByte,
                    address.BitIndex.Value, Convert.ToBoolean(rawValue), ct);
            }
            else
            {
                var bytes = S7NetPlusValueCodec.Encode(current, rawValue);
                await _client!.WriteBytesAsync(address.Area, address.DbNumber ?? 0, address.StartByte, bytes, ct);
            }
            RegisterSuccess();
            return CreateGoodValue(current, rawValue);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            RegisterFailure(ex);
            throw new DomainException($"写入 S7 点位“{DisplayPoint(current)}”失败：{ex.Message}");
        }
        finally
        {
            _ioGate.Release();
        }
    }

    public DevicePoint? GetPoint(string pointId)
        => _pointsById.TryGetValue(pointId.Trim(), out var point) ? point : null;

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try { await StopAsync(); }
        finally
        {
            _ioGate.Dispose();
            _lifecycle.Dispose();
        }
    }

    private async Task<PointValue> ReadPointAsync(DevicePoint point, CancellationToken ct)
    {
        var values = await ReadManyFreshAsync(new[] { point.PointId }, ct);
        return values[0];
    }

    private void ValidateConfiguration()
    {
        if (_channel.TransportKind != ChannelTransportKind.Tcp)
            throw new DomainException($"S7 设备 {_device.Code} 必须使用 TCP 通道");
        var endpoint = ResolveEndpoint();
        if (string.IsNullOrWhiteSpace(endpoint.Host))
            throw new DomainException($"S7 设备 {_device.Code} 没有配置 PLC IP 地址");
        if (endpoint.Port is < 1 or > 65535)
            throw new DomainException($"S7 设备 {_device.Code} 的 PLC 端口无效：{endpoint.Port}");
        if (_points.Count == 0)
            throw new DomainException($"S7 设备 {_device.Code} 没有点位，不能启动硬件运行时");
        ValidatePoints(_points);
    }

    private void ValidatePoints(IEnumerable<DevicePoint> points)
    {
        foreach (var point in points)
        {
            if (!TryValidatePoint(point, out var reason))
                throw new DomainException($"点位“{DisplayPoint(point)}”配置无效：{reason}");
        }
    }

    private static bool TryValidatePoint(DevicePoint point, out string? reason)
    {
        if (!S7AddressParser.TryParse(point.Address, out var address, out reason))
            return false;
        if (!SiemensS7TypeCapabilities.IsShapeCompatible(address!.Shape, point.DataType, out reason))
            return false;
        reason = null;
        return true;
    }

    private object DecodeRaw(DevicePoint point, S7Address address, byte[] bytes, int offset)
    {
        if (offset < 0 || offset + address.ByteCount > bytes.Length)
            throw new DomainException($"点位“{DisplayPoint(point)}”在 S7 读块中的偏移超出返回数据");
        if (address.Shape == S7AddressShape.Bit)
            return S7NetPlusValueCodec.DecodeBit(point, bytes[offset], address.BitIndex ?? 0);
        return S7NetPlusValueCodec.Decode(point, bytes.AsSpan(offset, address.ByteCount));
    }

    private PointValue CreateGoodValue(DevicePoint point, object rawValue)
        => new(
            point.Code,
            point.Address,
            PointQuality.Good,
            DevicePointValueConverter.ToEngineering(point, rawValue),
            _clock.UtcNow,
            point.PointId,
            _device.Id,
            _connectionGeneration,
            _revision,
            rawValue);

    private PointValue CreateBadValue(DevicePoint point, string message)
        => new(
            point.Code,
            point.Address,
            PointQuality.Bad,
            null,
            _clock.UtcNow,
            point.PointId,
            _device.Id,
            _connectionGeneration,
            _revision,
            null);

    private void RegisterSuccess()
    {
        _lastSuccessUtc = _clock.UtcNow;
        _consecutiveFailures = 0;
        _lastError = null;
        if (_state == DeviceConnectionState.Degraded)
            _state = DeviceConnectionState.Online;
    }

    private void RegisterFailure(Exception ex)
    {
        _lastFailureUtc = _clock.UtcNow;
        _consecutiveFailures++;
        _lastError = ex.Message;
        if (_client?.IsConnected != true)
            _state = DeviceConnectionState.Offline;
        else if (_state == DeviceConnectionState.Online)
            _state = DeviceConnectionState.Degraded;
    }

    private void EnsureOperational()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_client?.IsConnected != true || _state is not (DeviceConnectionState.Online or DeviceConnectionState.Degraded))
        {
            var detail = string.IsNullOrWhiteSpace(_lastError) ? string.Empty : $" {_lastError}";
            throw new DomainException($"西门子 S7 设备当前不可用，请先确认 PLC 已连接。{detail}");
        }
    }

    private DevicePoint ResolvePoint(DevicePoint requested)
    {
        ArgumentNullException.ThrowIfNull(requested);
        DevicePoint? current = null;
        if (!string.IsNullOrWhiteSpace(requested.PointId))
            _pointsById.TryGetValue(requested.PointId, out current);
        if (current is null && !string.IsNullOrWhiteSpace(requested.Address))
            _pointsByAddress.TryGetValue(requested.Address, out current);
        if (current is null)
            throw new DomainException($"点位 {requested.PointId}/{requested.Address} 不在 S7 设备 {_device.Code}");
        if (!string.IsNullOrWhiteSpace(requested.DeviceId)
            && !string.Equals(requested.DeviceId, _device.Id, StringComparison.OrdinalIgnoreCase))
            throw new DomainException($"点位“{DisplayPoint(requested)}”不属于 S7 设备 {_device.Code}");
        return current;
    }

    private DevicePoint ResolvePoint(string pointId)
        => string.IsNullOrWhiteSpace(pointId) || !_pointsById.TryGetValue(pointId.Trim(), out var point)
            ? throw new DomainException($"点位 {pointId} 不在 S7 设备 {_device.Code}")
            : point;

    private (string Host, int Port, int Rack, int Slot) ResolveEndpoint()
    {
        var options = _device.SiemensS7;
        var host = options?.Host?.Trim();
        if (string.IsNullOrWhiteSpace(host)) host = _device.Address?.Trim();
        if (string.IsNullOrWhiteSpace(host)) host = _channel.Tcp?.Host?.Trim();
        var port = options?.Port is > 0 and <= 65535
            ? options.Port
            : _channel.Tcp?.Port is > 0 and <= 65535 ? _channel.Tcp.Port : 102;
        return (host ?? string.Empty, port, options?.Rack ?? 0, options?.Slot ?? 0);
    }

    private static CpuType ResolveCpuType(string? model)
    {
        var value = model ?? string.Empty;
        if (value.Contains("1500", StringComparison.OrdinalIgnoreCase)) return CpuType.S71500;
        if (value.Contains("200", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("1200", StringComparison.OrdinalIgnoreCase)) return CpuType.S7200Smart;
        return CpuType.S71200;
    }

    private async Task DisposeClientAsync()
    {
        if (_client is null) return;
        var client = _client;
        _client = null;
        await client.DisposeAsync();
    }

    private static string DisplayPoint(DevicePoint point)
        => string.IsNullOrWhiteSpace(point.Name) ? point.Code : point.Name;

    private void Publish(DeviceEventSeverity severity, string code, string message)
    {
        _events?.Publish(new DeviceCommunicationEvent(
            _clock.UtcNow,
            severity,
            _device.ChannelId,
            _device.Id,
            "S7NetPlusDeviceRuntime",
            code,
            message));
    }
}
