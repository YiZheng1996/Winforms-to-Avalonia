using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Core.Application;

/// <summary>
/// 安装级报表后台任务。启动查证已发布版本，然后定期读取持久 Pending，覆盖完成通知丢失的情况。
/// 组合根须持有安装锁，并在释放数据库/安装锁前等待 DisposeAsync 完成。
/// </summary>
public sealed class ReportTaskDispatcher : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _task;

    public ReportTaskDispatcher(ReportGenerationWorker worker, IRecordRepository records,
        IReportArchiveReader archives, string ownerNodeId, string databaseInstanceId,
        IAppLogger logger, TimeSpan pollInterval)
    {
        if (pollInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(pollInterval));
        _task = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(pollInterval);
            try
            {
                do
                {
                    // 两步独立报告错误：损坏的遗留归档不阻止其他 Pending 任务处理。
                    try { await worker.RecoverPublishedAsync(ownerNodeId, databaseInstanceId, records, archives, _stop.Token); }
                    catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
                    catch (Exception ex) { logger.Error("报表归档恢复未完成，原任务保持待核查状态。", ex); }
                    try { await worker.ProcessPendingAsync(ownerNodeId, _stop.Token); }
                    catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
                    catch (Exception ex) { logger.Error("报表调度失败，将按持久状态继续查证。", ex); }
                } while (await timer.WaitForNextTickAsync(_stop.Token));
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await _task.ConfigureAwait(false);
        _stop.Dispose();
    }
}
