using XXX.TestBench.Core.Common;

namespace XXX.TestBench.App.ViewModels;

/// <summary>
/// 设备点位页面的单操作门和统一反馈。跨页面互斥仍由 Core 的设备操作协调器负责。
/// </summary>
public sealed partial class DevicePointManagementViewModel
{
    private readonly SemaphoreSlim _pageOperationGate = new(1, 1);

    /// <summary>
    /// 当前页面能否立即发起一次显式变更操作。
    /// </summary>
    public bool CanStartPageOperation => !IsBusy && _pageOperationGate.CurrentCount > 0;

    /// <summary>
    /// 执行一次页面级变更。第二个并发写操作不排队，直接返回可见拒绝。
    /// </summary>
    private async Task<OperationFeedback> RunMutationAsync(
        Func<CancellationToken, Task<string>> action,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        var entered = false;
        try
        {
            entered = await _pageOperationGate.WaitAsync(0, ct);
            if (!entered)
                return SetFeedback(false, "已有设备配置操作正在执行，请稍候");

            IsBusy = true;
            NotifyOperationStateChanged();
            var successMessage = await action(ct);
            return SetFeedback(true, successMessage);
        }
        catch (OperationCanceledException)
        {
            return SetFeedback(false, "操作已取消");
        }
        catch (DomainException ex)
        {
            return SetFeedback(false, ex.Message);
        }
        catch (Exception ex)
        {
            return SetFeedback(false, string.IsNullOrWhiteSpace(ex.Message)
                ? "设备配置操作失败，请查看应用日志"
                : ex.Message);
        }
        finally
        {
            if (entered)
                _pageOperationGate.Release();
            IsBusy = false;
            NotifyOperationStateChanged();
        }
    }


    /// <summary>
    /// 供界面更多菜单使用的刷新入口，成功也返回可见反馈。
    /// </summary>
    public async Task<OperationFeedback> RefreshAsync(CancellationToken ct = default)
    {
        await LoadAsync(ct);
        return string.IsNullOrWhiteSpace(StatusMessage)
            ? SetFeedback(true, "设备点位已按当前 Active 快照刷新")
            : new OperationFeedback(!StatusMessage.StartsWith("刷新设备点位失败", StringComparison.Ordinal), StatusMessage);
    }

    private OperationFeedback SetFeedback(bool succeeded, string message)
    {
        StatusMessage = message ?? string.Empty;
        OnPropertyChanged(nameof(CanStartPageOperation));
        return new OperationFeedback(succeeded, StatusMessage);
    }

    private void NotifyOperationStateChanged()
    {
        OnPropertyChanged(nameof(CanStartPageOperation));
        NotifyTreeActions();
    }
}
