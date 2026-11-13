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
    /// 统一执行一次页面显式操作。第二个并发操作不排队，直接返回可见拒绝；
    /// 未取得门时绝不清除既有操作的 IsBusy，也绝不释放不属于自己的门。
    /// </summary>
    private async Task<OperationFeedback> RunPageOperationAsync(
        Func<CancellationToken, Task<string>> action,
        string? successFeedback = null,
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
            var visibleMessage = successFeedback ?? successMessage;
            return string.IsNullOrWhiteSpace(visibleMessage)
                ? new OperationFeedback(true, string.Empty)
                : SetFeedback(true, visibleMessage);
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
            {
                IsBusy = false;
                _pageOperationGate.Release();
                NotifyOperationStateChanged();
            }
        }
    }

    /// <summary>
    /// 页面门的返回值适配器，供导入预览等“先计算、后确认”的入口使用。
    /// </summary>
    private async Task<TResult?> RunPageValueOperationAsync<TResult>(
        Func<CancellationToken, Task<TResult>> action,
        Func<TResult, string?>? successFeedback = null,
        CancellationToken ct = default)
        where TResult : class
    {
        ArgumentNullException.ThrowIfNull(action);
        var entered = false;
        try
        {
            entered = await _pageOperationGate.WaitAsync(0, ct);
            if (!entered)
            {
                SetFeedback(false, "已有设备配置操作正在执行，请稍候");
                return null;
            }

            IsBusy = true;
            NotifyOperationStateChanged();
            var result = await action(ct);
            var visibleMessage = successFeedback?.Invoke(result);
            if (!string.IsNullOrWhiteSpace(visibleMessage))
                SetFeedback(true, visibleMessage);
            return result;
        }
        catch (OperationCanceledException)
        {
            SetFeedback(false, "操作已取消");
            return null;
        }
        catch (DomainException ex)
        {
            SetFeedback(false, ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            SetFeedback(false, string.IsNullOrWhiteSpace(ex.Message)
                ? "设备配置操作失败，请查看应用日志"
                : ex.Message);
            return null;
        }
        finally
        {
            if (entered)
            {
                IsBusy = false;
                _pageOperationGate.Release();
                NotifyOperationStateChanged();
            }
        }
    }

    /// <summary>
    /// 所有页面变更入口使用同一实现，避免某个入口绕过页面级互斥。
    /// </summary>
    private Task<OperationFeedback> RunMutationAsync(
        Func<CancellationToken, Task<string>> action,
        CancellationToken ct = default)
        => RunPageOperationAsync(action, ct: ct);

    /// <summary>
    /// 加载核心逻辑由页面门统一包裹，内部不再嵌套调用公开 LoadAsync。
    /// </summary>
    private async Task<string> LoadCoreAsync(CancellationToken ct)
    {
        var state = CaptureUiState();
        StatusMessage = string.Empty;
        if (_services.DeviceConfigurations is not null)
        {
            var snapshot = await _services.DeviceConfigurations.LoadActiveAsync(ct);
            AdoptDeviceConfig(snapshot.Device);
            _services.DevicePoints.AdoptApplied(snapshot.Points);
            AdoptPointSnapshot(snapshot.Points, snapshot.Points.Groups, state, null);
        }
        else
        {
            _allEntries.Clear();
            _allEntries.AddRange(_services.DevicePoints.List().Select(ClonePoint));
            _allGroups.Clear();
            _allGroups.AddRange(_services.DevicePoints.ListGroups().Select(CloneGroup));
            RebuildTree();
        }

        OnPropertyChanged(nameof(HasPoints));
        RefreshCurrentScopeSnapshot();
        RefreshVisiblePoints();
        RefreshMoveGroups();
        NotifyRuntimeSummaryChanges();
        RefreshCachedValues();
        NotifyTreeActions();
        return string.Empty;
    }

    /// <summary>
    /// 供界面更多菜单使用的刷新入口，成功也返回可见反馈。
    /// </summary>
    public Task<OperationFeedback> RefreshAsync(CancellationToken ct = default)
        => RunPageOperationAsync(
            async token =>
            {
                await LoadCoreAsync(token);
                return "设备点位已按当前 Active 快照刷新";
            },
            "设备点位已按当前 Active 快照刷新",
            ct);

    private OperationFeedback SetFeedback(bool succeeded, string message)
    {
        StatusMessage = message ?? string.Empty;
        NotifyOperationStateChanged();
        return new OperationFeedback(succeeded, StatusMessage);
    }

    private void NotifyOperationStateChanged()
    {
        NotifyRuntimeSummaryChanges();
        OnPropertyChanged(nameof(CanStartPageOperation));
        NotifyTreeActions();
    }
}
