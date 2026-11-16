using XXX.TestBench.Core.Common;

namespace XXX.TestBench.Core.Application;

/// <summary>
/// 试验启动、配置应用等跨边界操作的统一互斥门。
/// </summary>
public sealed class DeviceOperationCoordinator : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _disposed;
    private Guid? _operationId;

    /// <summary>在互斥门内设置跨异步任务的台架占用；释放短期锁不会解除占用。</summary>
    internal void Reserve(Guid operationId)
    {
        if (operationId == Guid.Empty) throw new ArgumentException("操作标识不能为空。", nameof(operationId));
        if (_operationId is { } current && current != operationId)
            throw new DomainException("台架已有准备、运行或待恢复操作。");
        _operationId = operationId;
    }

    /// <summary>仅由所属操作在完成保存和收尾后解除占用。</summary>
    internal void Release(Guid operationId)
    {
        if (_operationId != operationId) throw new DomainException("台架占用身份不匹配。");
        _operationId = null;
    }

    public Task<IAsyncDisposable> EnterExecutionAsync(CancellationToken ct = default)
        => EnterAsync(ct);

    public Task<IAsyncDisposable> EnterConfigurationAsync(CancellationToken ct = default)
        => EnterIdleAsync(ct);

    public Task<IAsyncDisposable> EnterDiagnosticsAsync(CancellationToken ct = default)
        => EnterIdleAsync(ct);

    private async Task<IAsyncDisposable> EnterIdleAsync(CancellationToken ct)
    {
        var lease = await EnterAsync(ct);
        if (_operationId is null) return lease;
        await lease.DisposeAsync();
        throw new DomainException("台架正在准备、运行、收尾或等待恢复，不能修改配置或执行手动操作。");
    }

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
