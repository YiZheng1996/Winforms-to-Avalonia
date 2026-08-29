using System.Buffers.Binary;
using XXX.TestBench.Gateway.Domain;

namespace XXX.TestBench.Gateway.Infrastructure;

public sealed class ReadOnlyGatewayRuntime : IAsyncDisposable
{
    private readonly GatewayOptions _options;
    private readonly IReadOnlyS7Transport _s7;
    private readonly IReadOnlyModbusTransport _modbus;
    private readonly IGatewayLogSink _log;
    private readonly bool _isSimulated;
    private readonly SemaphoreSlim _pollGate = new(1, 1);
    private int _disposed;

    public ReadOnlyGatewayRuntime(
        GatewayOptions options,
        IReadOnlyS7Transport s7,
        IReadOnlyModbusTransport modbus,
        bool isSimulated,
        IGatewayLogSink? log = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        _s7 = s7 ?? throw new ArgumentNullException(nameof(s7));
        _modbus = modbus ?? throw new ArgumentNullException(nameof(modbus));
        _isSimulated = isSimulated;
        _log = log ?? NullGatewayLogSink.Instance;
        CurrentSnapshot = CreateInitialSnapshot(_isSimulated);
    }

    public bool WritesEnabled => false;
    public bool IsSimulated => _isSimulated;
    public GatewayPollSnapshot CurrentSnapshot { get; private set; }

    public async Task<GatewayPollSnapshot> PollOnceAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _pollGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            var timestamp = DateTimeOffset.UtcNow;
            var s7Connected = await EnsureS7ConnectedAsync(cancellationToken).ConfigureAwait(false);
            var modbusConnected = await EnsureModbusConnectedAsync(cancellationToken).ConfigureAwait(false);

            var analog = await ReadS7FloatAsync("SMART.PLC.AI.MAI00", "VD200", cancellationToken).ConfigureAwait(false);
            var digital = await ReadS7BooleanAsync("SMART.PLC.DI.MDI00", "V0.0", cancellationToken).ConfigureAwait(false);
            var modbus = await ReadModbusUInt16Async("Modbus.WSD.CH00", cancellationToken).ConfigureAwait(false);

            var physicalPoints = new[] { analog, digital, modbus };
            var isHealthy = s7Connected
                && modbusConnected
                && physicalPoints.All(x => x.Quality == DataQuality.Good);

            var generation = Math.Max(_s7.ConnectionGeneration, _modbus.ConnectionGeneration);
            var points = new List<GatewayPointSample>(5)
            {
                new(
                    "Gateway.Health.NoError",
                    isHealthy,
                    GatewayPointValueType.Boolean,
                    DataQuality.Good,
                    timestamp,
                    generation,
                    isHealthy ? null : "至少一条只读链路或业务点不可用"),
                new(
                    "Gateway.Health.Simulated",
                    _isSimulated,
                    GatewayPointValueType.Boolean,
                    DataQuality.Good,
                    timestamp,
                    generation),
                analog,
                digital,
                modbus
            };

            var snapshot = new GatewayPollSnapshot(timestamp, _isSimulated, isHealthy, points);
            CurrentSnapshot = snapshot;
            WriteLog(
                GatewayLogLevel.Information,
                "Read-only poll completed",
                new Dictionary<string, string?>
                {
                    ["simulated"] = _isSimulated.ToString(),
                    ["healthy"] = isHealthy.ToString(),
                    ["s7Generation"] = _s7.ConnectionGeneration.ToString(),
                    ["modbusGeneration"] = _modbus.ConnectionGeneration.ToString()
                });
            return snapshot;
        }
        finally
        {
            _pollGate.Release();
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await PollOnceAsync(cancellationToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_options.PollIntervalMs));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            await PollOnceAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await _pollGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await DisconnectAndDisposeAsync(
                "S7",
                _s7.DisconnectAsync,
                _s7.DisposeAsync).ConfigureAwait(false);
            await DisconnectAndDisposeAsync(
                "Modbus",
                _modbus.DisconnectAsync,
                _modbus.DisposeAsync).ConfigureAwait(false);
        }
        finally
        {
            _pollGate.Release();
            _pollGate.Dispose();
        }
    }

    private async Task<bool> EnsureS7ConnectedAsync(CancellationToken cancellationToken)
    {
        if (_s7.IsConnected)
            return true;

        try
        {
            await _s7.ConnectAsync(cancellationToken).ConfigureAwait(false);
            WriteLog(
                GatewayLogLevel.Information,
                "S7 read-only transport connected",
                new Dictionary<string, string?>
                {
                    ["endpoint"] = _s7.EndpointId,
                    ["connectionGeneration"] = _s7.ConnectionGeneration.ToString()
                });
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            WriteLog(
                GatewayLogLevel.Error,
                "S7 read-only transport unavailable",
                new Dictionary<string, string?> { ["endpoint"] = _s7.EndpointId },
                ex);
            return false;
        }
    }

    private async Task<bool> EnsureModbusConnectedAsync(CancellationToken cancellationToken)
    {
        if (_modbus.IsConnected)
            return true;

        try
        {
            await _modbus.ConnectAsync(cancellationToken).ConfigureAwait(false);
            WriteLog(
                GatewayLogLevel.Information,
                "Modbus read-only transport connected",
                new Dictionary<string, string?>
                {
                    ["transport"] = _modbus.TransportId,
                    ["connectionGeneration"] = _modbus.ConnectionGeneration.ToString()
                });
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            WriteLog(
                GatewayLogLevel.Error,
                "Modbus read-only transport unavailable",
                new Dictionary<string, string?> { ["transport"] = _modbus.TransportId },
                ex);
            return false;
        }
    }

    private async Task<GatewayPointSample> ReadS7FloatAsync(
        string pointId,
        string address,
        CancellationToken cancellationToken)
    {
        if (!_s7.IsConnected)
            return BadPoint(pointId, GatewayPointValueType.Float32, _s7.ConnectionGeneration, "S7 未连接");

        try
        {
            var result = await _s7.ReadMemoryAsync(address, 4, cancellationToken).ConfigureAwait(false);
            if (result.Quality != DataQuality.Good || result.Data.Length < 4)
                return await BadS7PointAsync(pointId, GatewayPointValueType.Float32, result.ConnectionGeneration, result.Diagnostic ?? "S7 浮点样本无效").ConfigureAwait(false);

            var value = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(result.Data));
            if (!float.IsFinite(value))
                return await BadS7PointAsync(pointId, GatewayPointValueType.Float32, result.ConnectionGeneration, "S7 浮点样本不是有限值").ConfigureAwait(false);

            return new GatewayPointSample(
                pointId,
                value,
                GatewayPointValueType.Float32,
                DataQuality.Good,
                result.Timestamp,
                result.ConnectionGeneration);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            WriteLog(GatewayLogLevel.Warning, "S7 float read failed", new Dictionary<string, string?> { ["pointId"] = pointId, ["address"] = address }, ex);
            return await BadS7PointAsync(pointId, GatewayPointValueType.Float32, _s7.ConnectionGeneration, ex.Message).ConfigureAwait(false);
        }
    }

    private async Task<GatewayPointSample> ReadS7BooleanAsync(
        string pointId,
        string address,
        CancellationToken cancellationToken)
    {
        if (!_s7.IsConnected)
            return BadPoint(pointId, GatewayPointValueType.Boolean, _s7.ConnectionGeneration, "S7 未连接");

        try
        {
            var result = await _s7.ReadMemoryAsync(address, 1, cancellationToken).ConfigureAwait(false);
            if (result.Quality != DataQuality.Good || result.Data.Length < 1)
                return await BadS7PointAsync(pointId, GatewayPointValueType.Boolean, result.ConnectionGeneration, result.Diagnostic ?? "S7 布尔样本无效").ConfigureAwait(false);

            return new GatewayPointSample(
                pointId,
                result.Data[0] != 0,
                GatewayPointValueType.Boolean,
                DataQuality.Good,
                result.Timestamp,
                result.ConnectionGeneration);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            WriteLog(GatewayLogLevel.Warning, "S7 boolean read failed", new Dictionary<string, string?> { ["pointId"] = pointId, ["address"] = address }, ex);
            return await BadS7PointAsync(pointId, GatewayPointValueType.Boolean, _s7.ConnectionGeneration, ex.Message).ConfigureAwait(false);
        }
    }

    private async Task<GatewayPointSample> ReadModbusUInt16Async(
        string pointId,
        CancellationToken cancellationToken)
    {
        if (!_modbus.IsConnected)
            return BadPoint(pointId, GatewayPointValueType.UInt16, _modbus.ConnectionGeneration, "Modbus 未连接");

        var request = new ModbusRegisterReadRequest(
            string.Equals(_options.ActiveModbusTransport, "Rtu", StringComparison.OrdinalIgnoreCase)
                ? _options.ModbusRtu.SlaveId
                : _options.ModbusTcp.UnitId,
            0,
            1);

        try
        {
            var result = await _modbus.ReadRegistersAsync(request, cancellationToken).ConfigureAwait(false);
            if (result.Quality != DataQuality.Good || result.Registers.Length < 1)
                return await BadModbusPointAsync(pointId, GatewayPointValueType.UInt16, result.ConnectionGeneration, result.Diagnostic ?? "Modbus 寄存器样本无效").ConfigureAwait(false);

            return new GatewayPointSample(
                pointId,
                result.Registers[0],
                GatewayPointValueType.UInt16,
                DataQuality.Good,
                result.Timestamp,
                result.ConnectionGeneration);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            WriteLog(GatewayLogLevel.Warning, "Modbus register read failed", new Dictionary<string, string?> { ["pointId"] = pointId }, ex);
            return await BadModbusPointAsync(pointId, GatewayPointValueType.UInt16, _modbus.ConnectionGeneration, ex.Message).ConfigureAwait(false);
        }
    }

    private static GatewayPollSnapshot CreateInitialSnapshot(bool isSimulated)
    {
        var now = DateTimeOffset.UtcNow;
        var points = P2ReadOnlyPointCatalog.Points
            .Select(point => new GatewayPointSample(
                point.LogicalPoint,
                null,
                point.ValueType,
                DataQuality.Unknown,
                now,
                0,
                "等待首个采样"))
            .ToArray();
        return new GatewayPollSnapshot(now, isSimulated, false, points);
    }

    private static GatewayPointSample BadPoint(
        string pointId,
        GatewayPointValueType valueType,
        long generation,
        string diagnostic) =>
        new(pointId, null, valueType, DataQuality.Bad, DateTimeOffset.UtcNow, generation, diagnostic);

    private async Task<GatewayPointSample> BadS7PointAsync(
        string pointId,
        GatewayPointValueType valueType,
        long generation,
        string diagnostic)
    {
        await DisconnectAfterReadFailureAsync("S7", _s7.IsConnected, _s7.DisconnectAsync, diagnostic).ConfigureAwait(false);
        return BadPoint(pointId, valueType, generation, diagnostic);
    }

    private async Task<GatewayPointSample> BadModbusPointAsync(
        string pointId,
        GatewayPointValueType valueType,
        long generation,
        string diagnostic)
    {
        await DisconnectAfterReadFailureAsync("Modbus", _modbus.IsConnected, _modbus.DisconnectAsync, diagnostic).ConfigureAwait(false);
        return BadPoint(pointId, valueType, generation, diagnostic);
    }

    private async Task DisconnectAfterReadFailureAsync(
        string component,
        bool isConnected,
        Func<Task> disconnect,
        string diagnostic)
    {
        if (!isConnected)
            return;

        try
        {
            await disconnect().ConfigureAwait(false);
            WriteLog(
                GatewayLogLevel.Warning,
                $"{component} transport disconnected after read failure",
                new Dictionary<string, string?> { ["diagnostic"] = diagnostic });
        }
        catch (Exception ex)
        {
            WriteLog(
                GatewayLogLevel.Warning,
                $"{component} transport disconnect after read failure failed",
                new Dictionary<string, string?> { ["diagnostic"] = diagnostic },
                ex);
        }
    }

    private async Task DisconnectAndDisposeAsync(
        string component,
        Func<Task> disconnect,
        Func<ValueTask> dispose)
    {
        try
        {
            await disconnect().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            WriteLog(
                GatewayLogLevel.Warning,
                $"{component} disconnect failed during cleanup",
                new Dictionary<string, string?>(),
                ex);
        }

        try
        {
            await dispose().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            WriteLog(
                GatewayLogLevel.Warning,
                $"{component} dispose failed during cleanup",
                new Dictionary<string, string?>(),
                ex);
        }
    }

    private void WriteLog(
        GatewayLogLevel level,
        string message,
        IReadOnlyDictionary<string, string?>? properties = null,
        Exception? exception = null)
    {
        _log.Write(new GatewayLogEntry(
            DateTimeOffset.UtcNow,
            level,
            "ReadOnlyGatewayRuntime",
            message,
            properties ?? new Dictionary<string, string?>(),
            exception?.ToString()));
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(ReadOnlyGatewayRuntime));
    }
}

public static class ReadOnlyGatewayRuntimeFactory
{
    public static ReadOnlyGatewayRuntime Create(
        GatewaySettingsDocument settings,
        IGatewayLogSink? log = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();

        var options = settings.Gateway;
        var isSimulated = string.Equals(options.TransportMode, "OfflineSimulation", StringComparison.OrdinalIgnoreCase);
        IReadOnlyS7Transport? s7 = null;
        IReadOnlyModbusTransport? modbus = null;
        try
        {
            if (isSimulated)
            {
                s7 = new SimulatedS7ReadOnlyTransport();
                modbus = new SimulatedModbusReadOnlyTransport();
            }
            else
            {
                var s7Options = options.S7Endpoints.Single(x => x.Enabled);
                s7 = new S7NetPlusReadOnlyTransport(s7Options);
                modbus = string.Equals(options.ActiveModbusTransport, "Rtu", StringComparison.OrdinalIgnoreCase)
                    ? new NModbusReadOnlyTransport(options.ModbusRtu)
                    : new NModbusReadOnlyTransport(options.ModbusTcp);
            }

            return new ReadOnlyGatewayRuntime(options, s7, modbus, isSimulated, log);
        }
        catch
        {
            s7?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            modbus?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw;
        }
    }
}
