using XXX.TestBench.Core.Common;

namespace XXX.TestBench.Core.Application;

/// <summary>
/// 试验启动、配置应用等跨边界操作的统一互斥门。
/// </summary>
public sealed class DeviceOperationCoordinator : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _disposed;

    public Task<IAsyncDisposable> EnterExecutionAsync(CancellationToken ct = default)
        => EnterAsync(ct);

    public Task<IAsyncDisposable> EnterConfigurationAsync(CancellationToken ct = default)
        => EnterAsync(ct);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _gate.Dispose();
        await ValueTask.CompletedTask;
    }

    private async Task<IAsyncDisposable> EnterAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _gate.WaitAsync(ct);
        return new Lease(_gate);
    }

    private sealed class Lease(SemaphoreSlim gate) : IAsyncDisposable
    {
        private int _released;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                gate.Release();
            return ValueTask.CompletedTask;
        }
    }
}
