using System.Globalization;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Ports;
using XXX.TestBench.Devices.Modbus;

namespace XXX.TestBench.Devices.Runtime;

/// <summary>
/// Modbus TCP/RTU 运行时的公共实现。
///
/// TCP 和 RTU 的差异只有连接资源的所有权：TCP 由单台设备拥有，RTU 由串口通道
/// 共享。本基类统一实现批量规划、功能码选择、类型解码、写入和状态统计，避免两条
/// 链路在超时、字序或点位质量处理上出现不一致。
/// </summary>
public abstract class ModbusDeviceRuntimeBase : IDeviceRuntime
{
    private readonly DeviceConfig.DeviceEntry _device;
    private readonly ChannelEntry _channel;
    private readonly IReadOnlyList<DevicePoint> _points;
    private readonly IReadOnlyDictionary<string, DevicePoint> _pointsById;
    private readonly IClock _clock;
    private readonly string _revision;
    private readonly IDeviceEventSink? _events;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly SemaphoreSlim _ioGate = new(1, 1);
    private DeviceConnectionState _state = DeviceConnectionState.Unknown;
    private string? _lastError;
    private DateTime? _lastSuccessUtc;
    private DateTime? _lastFailureUtc;
    private int _consecutiveFailures;
    private int _disposed;
    private RuntimeActivationReport _activationReport;

    protected ModbusDeviceRuntimeBase(
        DeviceConfig.DeviceEntry device,
        ChannelEntry channel,
        IReadOnlyList<DevicePoint> points,
        IClock clock,
        string revision,
        IDeviceEventSink? events = null)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _points = points ?? throw new ArgumentNullException(nameof(points));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _revision = revision ?? string.Empty;
        _events = events;
        _pointsById = _points
            .Where(point => !string.IsNullOrWhiteSpace(point.PointId))
            .ToDictionary(point => point.PointId, StringComparer.OrdinalIgnoreCase);
        _activationReport = new RuntimeActivationReport(1, 0, Array.Empty<DeviceActivationIssue>());
    }

    protected DeviceConfig.DeviceEntry Device => _device;
    protected ChannelEntry Channel => _channel;
    protected IClock Clock => _clock;
    protected IReadOnlyList<DevicePoint> Points => _points;
    protected abstract string ProtocolDisplayName { get; }
    protected abstract bool HasConnection { get; }
    protected abstract long CurrentConnectionGeneration { get; }
    protected abstract string EndpointText { get; }

    /// <summary>
    /// 取得当前连接；连接不存在时由子类按自己的资源所有权创建。
    /// </summary>
    protected abstract Task<IModbusClient> EnsureConnectionAsync(TimeSpan connectTimeout, CancellationToken ct);

    /// <summary>
    /// 让当前连接失效。TCP 会释放自己的客户端，RTU 会让共享串口连接失效。
    /// </summary>
    protected abstract Task InvalidateConnectionAsync();

    /// <summary>
    /// 停止运行时；RTU 子类不会在这里关闭共享串口，通道排空后再统一关闭。
    /// </summary>
    protected abstract Task StopConnectionAsync(CancellationToken ct);

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
            var health = _state switch
            {
                DeviceConnectionState.Online => DeviceHealth.Healthy,
                DeviceConnectionState.Degraded => DeviceHealth.Degraded,
                // 传输层已经打开但还没有有效点位响应，按“待点位验证”展示，不能伪装成 Online。
                DeviceConnectionState.Connecting => DeviceHealth.Degraded,
                DeviceConnectionState.Offline => DeviceHealth.Disconnected,
                DeviceConnectionState.Faulted => DeviceHealth.Faulted,
                _ => DeviceHealth.Unknown
            };
            return new DeviceRuntimeInfo(
                Name,
                ProtocolDisplayName,
                EndpointText,
                IsSimulation: false,
                health,
                IsConnected: HasConnection && _state is (DeviceConnectionState.Online or DeviceConnectionState.Degraded),
                _lastError,
                _device.Id,
                _device.ChannelId,
                _revision,
                CurrentConnectionGeneration,
                _state,
                DeviceMode.Hardware,
                _lastSuccessUtc,
                _lastFailureUtc,
                _consecutiveFailures,
                null,
                null);
        }
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        await _lifecycle.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            ValidateConfiguration();
            _state = DeviceConnectionState.Connecting;
            _lastError = null;
            Publish(DeviceEventSeverity.Information, "MODBUS_CONNECTING",
                $"正在建立 {ProtocolDisplayName} 传输连接：{EndpointText}");

            try
            {
                await EnsureConnectionAsync(GetConnectTimeout(), ct);
                // 仅确认 Socket/串口已打开；没有发起点位请求，所以状态仍是 Connecting。
                _activationReport = new RuntimeActivationReport(1, 1, Array.Empty<DeviceActivationIssue>());
                Publish(DeviceEventSeverity.Information, "MODBUS_TRANSPORT_OPENED",
                    $"{ProtocolDisplayName} 传输连接已建立，等待首次有效点位响应：{EndpointText}");
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
                await InvalidateConnectionAsync();
                _activationReport = new RuntimeActivationReport(1, 0, new[]
                {
                    new DeviceActivationIssue(_device.Id, _device.Name, ex.Message, ex)
                });
                Publish(DeviceEventSeverity.Error, "MODBUS_CONNECT_FAILED", ex.Message);
                throw ex is DomainException
                    ? ex
                    : new DomainException($"{ProtocolDisplayName} 设备连接失败：{ex.Message}");
            }
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
            await StopConnectionAsync(ct);
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
        return Task.FromResult<IReadOnlyList<DevicePoint>>(_points);
    }

    public Task<PointValue> ReadAsync(DevicePoint point, CancellationToken ct = default)
        => ReadFreshAsync(ResolvePoint(point).PointId, ct);

    public Task<PointValue> ReadFreshAsync(string pointId, CancellationToken ct = default)
        => ReadOneAsync(ResolvePoint(pointId), ct);

    public async Task<IReadOnlyList<PointValue>> ReadManyFreshAsync(
        IReadOnlyCollection<string> pointIds,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pointIds);
        var points = pointIds
            .Select(ResolvePoint)
            .DistinctBy(point => point.PointId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (points.Count == 0)
            return Array.Empty<PointValue>();

        var values = new Dictionary<string, PointValue>(StringComparer.OrdinalIgnoreCase);
        var validPoints = new List<DevicePoint>(points.Count);
        foreach (var point in points)
        {
            if (TryValidatePoint(point, out var reason))
                validPoints.Add(point);
            else
                values[point.PointId] = CreateBadValue(point, reason!);
        }
        if (validPoints.Count == 0)
            return points.Select(point => values[point.PointId]).ToList();

        EnsureReadable();
        await _ioGate.WaitAsync(ct);
        try
        {
            var client = await EnsureConnectionAsync(GetConnectTimeout(), ct);
            var batches = ModbusReadBatchPlanner.Plan(validPoints);
            foreach (var batch in batches)
            {
                ct.ThrowIfCancellationRequested();
                var response = await ReadBatchAsync(client, batch, GetRequestTimeout(), ct);
                ValidateResponseLength(batch, response);
                foreach (var item in batch.Items)
                {
                    try
                    {
                        object raw;
                        if (batch.Area.IsBitArea())
                        {
                            raw = ((bool[])response)[item.OffsetInBatch];
                        }
                        else
                        {
                            var registers = (ushort[])response;
                            raw = ModbusValueCodec.DecodeRegisters(
                                item.Point,
                                registers.AsSpan(item.OffsetInBatch, item.ValueLength));
                        }
                        values[item.Point.PointId] = CreateGoodValue(item.Point, raw);
                    }
                    catch (Exception ex)
                    {
                        values[item.Point.PointId] = CreateBadValue(item.Point, ex.Message);
                        Publish(DeviceEventSeverity.Warning, "MODBUS_POINT_BAD",
                            $"点位“{DisplayPoint(item.Point)}”解码失败：{ex.Message}");
                    }
                }
            }
            RegisterSuccess();
            return points.Select(point => values.TryGetValue(point.PointId, out var value)
                ? value
                : CreateBadValue(point, "Modbus 读取未返回该点位数据")).ToList();
        }
        catch (OperationCanceledException)
        {
            await InvalidateAfterRequestFailureAsync();
            throw;
        }
        catch (Exception ex)
        {
            await InvalidateAfterRequestFailureAsync();
            RegisterFailure(ex);
            throw;
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
        if (!TryValidatePoint(current, out var reason))
            throw new DomainException($"点位“{DisplayPoint(current)}”配置无效：{reason}");
        if (value is null)
            throw new DomainException($"点位“{DisplayPoint(current)}”的写入值不能为空");
        EnsureWritable();

        await _ioGate.WaitAsync(ct);
        try
        {
            var client = await EnsureConnectionAsync(GetConnectTimeout(), ct);
            var address = ModbusAddressParser.Parse(current.Address);
            var requestTimeout = GetRequestTimeout();
            var type = ModbusTypeCapabilities.NormalizeCompatibilityType(address.Area, current.DataType);
            if (type is DevicePointDataType.Bool or DevicePointDataType.Boolean)
            {
                await client.WriteSingleCoilAsync(
                    GetUnitId(),
                    checked((ushort)address.Offset),
                    Convert.ToBoolean(value, CultureInfo.InvariantCulture),
                    requestTimeout,
                    ct);
            }
            else
            {
                var registers = ModbusValueCodec.EncodeRegisters(current, value);
                if (registers.Length == 1)
                {
                    await client.WriteSingleRegisterAsync(
                        GetUnitId(), checked((ushort)address.Offset), registers[0], requestTimeout, ct);
                }
                else
                {
                    await client.WriteMultipleRegistersAsync(
                        GetUnitId(), checked((ushort)address.Offset), registers, requestTimeout, ct);
                }
            }
            RegisterSuccess();
            return CreateGoodValue(current, value);
        }
        catch (OperationCanceledException)
        {
            await InvalidateAfterRequestFailureAsync();
            throw;
        }
        catch (Exception ex)
        {
            await InvalidateAfterRequestFailureAsync();
            RegisterFailure(ex);
            throw new DomainException($"写入 {ProtocolDisplayName} 点位“{DisplayPoint(current)}”失败：{ex.Message}");
        }
        finally
        {
            _ioGate.Release();
        }
    }

    public DevicePoint? GetPoint(string pointId)
        => _pointsById.TryGetValue((pointId ?? string.Empty).Trim(), out var point) ? point : null;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        try
        {
            await StopAsync();
        }
        finally
        {
            _ioGate.Dispose();
            _lifecycle.Dispose();
        }
    }

    private async Task<PointValue> ReadOneAsync(DevicePoint point, CancellationToken ct)
    {
        var values = await ReadManyFreshAsync(new[] { point.PointId }, ct);
        return values[0];
    }

    private DevicePoint ResolvePoint(string pointId)
        => string.IsNullOrWhiteSpace(pointId) || GetPoint(pointId.Trim()) is not { } point
            ? throw new DomainException($"点位 {pointId} 不属于设备 {_device.Code}")
            : point;

    private DevicePoint ResolvePoint(DevicePoint requested)
    {
        ArgumentNullException.ThrowIfNull(requested);
        if (!string.IsNullOrWhiteSpace(requested.PointId)
            && GetPoint(requested.PointId.Trim()) is { } byId)
            return byId;
        if (_points.FirstOrDefault(point =>
                string.Equals(point.Address, requested.Address, StringComparison.OrdinalIgnoreCase)) is { } byAddress)
            return byAddress;
        throw new DomainException($"点位 {requested.PointId}/{requested.Address} 不属于设备 {_device.Code}");
    }

    private async Task<object> ReadBatchAsync(
        IModbusClient client,
        ModbusReadBatch batch,
        TimeSpan requestTimeout,
        CancellationToken ct)
    {
        var unitId = GetUnitId();
        return batch.Area switch
        {
            ModbusArea.Coil => await client.ReadCoilsAsync(unitId, batch.Start, batch.Count, requestTimeout, ct),
            ModbusArea.DiscreteInput => await client.ReadDiscreteInputsAsync(unitId, batch.Start, batch.Count, requestTimeout, ct),
            ModbusArea.HoldingRegister => await client.ReadHoldingRegistersAsync(unitId, batch.Start, batch.Count, requestTimeout, ct),
            ModbusArea.InputRegister => await client.ReadInputRegistersAsync(unitId, batch.Start, batch.Count, requestTimeout, ct),
            _ => throw new DomainException($"Modbus 数据区不受支持：{batch.Area}")
        };
    }

    private static void ValidateResponseLength(ModbusReadBatch batch, object response)
    {
        var actual = batch.Area.IsBitArea()
            ? ((bool[])response).Length
            : ((ushort[])response).Length;
        if (actual < batch.Count)
            throw new DomainException($"Modbus {batch.Area.ToCode()} 读响应长度不足：期望 {batch.Count}，实际 {actual}");
    }

    private void ValidateConfiguration()
    {
        if (_channel.TransportKind is not (ChannelTransportKind.Tcp or ChannelTransportKind.Serial))
            throw new DomainException($"{ProtocolDisplayName} 设备不能使用当前通道传输类型：{_channel.TransportKind}");
        if (!ModbusAddressParser.TryParse("C:0", out _, out _))
            throw new DomainException("Modbus 地址解析器初始化失败");
        if (!GetUnitId(out var unitId))
            throw new DomainException("Modbus 站号必须在 1-247 范围内，首版不支持广播站号 0");
        _ = unitId;
        if (_channel.TransportKind == ChannelTransportKind.Tcp && _device.ModbusTcp is null)
            throw new DomainException("Modbus TCP 缺少设备专属端点");
        if (_channel.TransportKind == ChannelTransportKind.Serial && _channel.Serial is null)
            throw new DomainException("Modbus RTU 通道缺少串口参数");
    }

    private bool TryValidatePoint(DevicePoint point, out string? reason)
    {
        if (!ModbusAddressParser.TryParse(point.Address, out var address, out reason))
            return false;
        var type = ModbusTypeCapabilities.NormalizeCompatibilityType(address.Area, point.DataType);
        return ModbusTypeCapabilities.TryValidate(address.Area, type, point.IsWritable, point.DecodeOptions, out reason);
    }

    private void EnsureReadable()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_state is not (DeviceConnectionState.Connecting or DeviceConnectionState.Online
            or DeviceConnectionState.Degraded or DeviceConnectionState.Offline))
            throw new DomainException($"设备 {_device.Code} 当前不可读取：{_lastError ?? "请先建立传输连接"}");
    }

    private void EnsureWritable()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_state is not (DeviceConnectionState.Online or DeviceConnectionState.Degraded))
            throw new DomainException($"设备 {_device.Code} 当前未完成点位验证，暂不允许写入");
    }

    private TimeSpan GetConnectTimeout()
        => TimeSpan.FromMilliseconds(Math.Max(1, (_device.Timing ?? new DeviceTimingOptions()).ConnectTimeoutMs));

    private TimeSpan GetRequestTimeout()
        => TimeSpan.FromMilliseconds(Math.Max(1, (_device.Timing ?? new DeviceTimingOptions()).RequestTimeoutMs));

    private byte GetUnitId()
        => GetUnitId(out var unitId)
            ? checked((byte)unitId)
            : throw new DomainException("Modbus 站号必须在 1-247 范围内");

    private bool GetUnitId(out int unitId)
    {
        unitId = _device.ModbusUnitId ?? 0;
        return unitId is >= 1 and <= 247;
    }

    private async Task InvalidateAfterRequestFailureAsync()
    {
        try { await InvalidateConnectionAsync(); }
        finally { _state = DeviceConnectionState.Offline; }
    }

    private void RegisterSuccess()
    {
        _lastSuccessUtc = _clock.UtcNow;
        _lastError = null;
        _consecutiveFailures = 0;
        if (_state is DeviceConnectionState.Connecting or DeviceConnectionState.Degraded
            or DeviceConnectionState.Offline)
        {
            _state = DeviceConnectionState.Online;
            Publish(DeviceEventSeverity.Information, "MODBUS_ONLINE", $"{ProtocolDisplayName} 首次有效点位响应成功");
        }
    }

    private void RegisterFailure(Exception ex)
    {
        _lastFailureUtc = _clock.UtcNow;
        _lastError = ex.Message;
        _consecutiveFailures++;
        if (_state is DeviceConnectionState.Online or DeviceConnectionState.Connecting)
            _state = DeviceConnectionState.Degraded;
        Publish(DeviceEventSeverity.Warning, "MODBUS_REQUEST_FAILED", ex.Message);
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
            CurrentConnectionGeneration,
            _revision,
            rawValue);

    private PointValue CreateBadValue(DevicePoint point, string reason)
        => new(
            point.Code,
            point.Address,
            PointQuality.Bad,
            null,
            _clock.UtcNow,
            point.PointId,
            _device.Id,
            CurrentConnectionGeneration,
            _revision,
            null);

    private static string DisplayPoint(DevicePoint point)
        => string.IsNullOrWhiteSpace(point.Name) ? point.Code : point.Name;

    private void Publish(DeviceEventSeverity severity, string code, string message)
        => _events?.Publish(new DeviceCommunicationEvent(
            _clock.UtcNow,
            severity,
            _device.ChannelId,
            _device.Id,
            ProtocolDisplayName,
            code,
            message));
}
