using NModbus;
using NModbus.IO;

namespace XXX.TestBench.Devices.Modbus;

/// <summary>
/// NModbus 到本项目最小客户端边界的适配器。
///
/// NModbus 3.x 的 API 没有为每个异步操作提供 CancellationToken，因此这里用
/// WaitAsync 做上层取消/超时保护，并在保护触发后立即释放传输资源。这样即使
/// 底层调用尚未返回，也不会被后续请求继续复用，符合 Modbus 首版的失效策略。
/// </summary>
public sealed class NModbusClientAdapter : IModbusClient
{
    private readonly IModbusMaster _master;
    private int _disposed;

    public NModbusClientAdapter(IModbusMaster master)
        => _master = master ?? throw new ArgumentNullException(nameof(master));

    public Task<bool[]> ReadCoilsAsync(byte unitId, ushort start, ushort count, TimeSpan requestTimeout, CancellationToken ct)
        => ExecuteAsync(() => _master.ReadCoilsAsync(unitId, start, count), requestTimeout, ct);

    public Task<bool[]> ReadDiscreteInputsAsync(byte unitId, ushort start, ushort count, TimeSpan requestTimeout, CancellationToken ct)
        => ExecuteAsync(() => _master.ReadInputsAsync(unitId, start, count), requestTimeout, ct);

    public Task<ushort[]> ReadHoldingRegistersAsync(byte unitId, ushort start, ushort count, TimeSpan requestTimeout, CancellationToken ct)
        => ExecuteAsync(() => _master.ReadHoldingRegistersAsync(unitId, start, count), requestTimeout, ct);

    public Task<ushort[]> ReadInputRegistersAsync(byte unitId, ushort start, ushort count, TimeSpan requestTimeout, CancellationToken ct)
        => ExecuteAsync(() => _master.ReadInputRegistersAsync(unitId, start, count), requestTimeout, ct);

    public Task WriteSingleCoilAsync(byte unitId, ushort address, bool value, TimeSpan requestTimeout, CancellationToken ct)
        => ExecuteAsync(() => _master.WriteSingleCoilAsync(unitId, address, value), requestTimeout, ct);

    public Task WriteSingleRegisterAsync(byte unitId, ushort address, ushort value, TimeSpan requestTimeout, CancellationToken ct)
        => ExecuteAsync(() => _master.WriteSingleRegisterAsync(unitId, address, value), requestTimeout, ct);

    public Task WriteMultipleRegistersAsync(byte unitId, ushort address, ushort[] values, TimeSpan requestTimeout, CancellationToken ct)
        => ExecuteAsync(() => _master.WriteMultipleRegistersAsync(unitId, address, values), requestTimeout, ct);

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return ValueTask.CompletedTask;
        _master.Transport.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, TimeSpan requestTimeout, CancellationToken ct)
    {
        PrepareTransport(requestTimeout);
        try
        {
            return await operation().WaitAsync(requestTimeout, ct);
        }
        catch (TimeoutException)
        {
            await DisposeAsync();
            throw;
        }
        catch (OperationCanceledException)
        {
            await DisposeAsync();
            throw;
        }
    }

    private async Task ExecuteAsync(Func<Task> operation, TimeSpan requestTimeout, CancellationToken ct)
    {
        PrepareTransport(requestTimeout);
        try
        {
            await operation().WaitAsync(requestTimeout, ct);
        }
        catch (TimeoutException)
        {
            await DisposeAsync();
            throw;
        }
        catch (OperationCanceledException)
        {
            await DisposeAsync();
            throw;
        }
    }

    private void PrepareTransport(TimeSpan requestTimeout)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var milliseconds = checked((int)Math.Max(1, requestTimeout.TotalMilliseconds));
        if (_master.Transport is ModbusTransport transport)
        {
            // 重试由 DeviceSession 统一控制，避免 NModbus 内部重试和业务重试叠加。
            transport.Retries = 0;
            transport.ReadTimeout = milliseconds;
            transport.WriteTimeout = milliseconds;
        }
    }
}
