using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using XXX.TestBench.App.ViewModels.Process;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.App.ViewModels;

public sealed partial class ProcessMonitorViewModel
{
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    /// <summary>
    /// 页面宿主提供危险操作确认。未提供时高风险输出会被拒绝，不会默认确认。
    /// </summary>
    public Func<string, string, CancellationToken, Task<bool>>? ConfirmationHandler { get; set; }

    [RelayCommand(CanExecute = nameof(CanOpenInlet))]
    private Task OpenInletAsync()
        => ExecuteDigitalOutputAsync(InletValve, true, "进气阀");

    [RelayCommand(CanExecute = nameof(CanCloseInlet))]
    private Task CloseInletAsync()
        => ExecuteDigitalOutputAsync(InletValve, false, "进气阀");

    [RelayCommand(CanExecute = nameof(CanOpenExhaust))]
    private Task OpenExhaustAsync()
        => ExecuteDigitalOutputAsync(ExhaustValve, true, "排气阀");

    [RelayCommand(CanExecute = nameof(CanCloseExhaust))]
    private Task CloseExhaustAsync()
        => ExecuteDigitalOutputAsync(ExhaustValve, false, "排气阀");

    [RelayCommand(CanExecute = nameof(CanApplyPressureSetpoint))]
    private Task ApplyPressureSetpointAsync()
        => ExecuteAnalogOutputAsync(PressureSetpoint);

    private bool CanOpenInlet() => InletValve.CanOperate;
    private bool CanCloseInlet() => InletValve.CanOperate;
    private bool CanOpenExhaust() => ExhaustValve.CanOperate;
    private bool CanCloseExhaust() => ExhaustValve.CanOperate;
    private bool CanApplyPressureSetpoint() => PressureSetpoint.CanApply;

    [RelayCommand(CanExecute = nameof(CanWrite))]
    public async Task WriteAsync()
    {
        StatusMessage = string.Empty;
        try
        {
            if (SelectedPoint is null || !SelectedPoint.IsWritable)
                throw new DomainException("请选择可写点位");
            var runtime = _services.DeviceModes.Runtime
                ?? throw new DomainException("设备运行时未初始化");
            var point = runtime.GetPoint(SelectedPoint.PointId)
                ?? throw new DomainException("点位不存在");
            object? value = bool.TryParse(WriteValue, out var boolean)
                ? boolean
                : decimal.TryParse(WriteValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var decimalValue)
                    ? decimalValue
                    : WriteValue;
            var activeRun = await _services.RecordRepository.GetActiveRunningRecordAsync() is not null;
            await _services.WritePipeline.ExecuteAsync(new WriteCommand(
                _actor,
                point,
                value,
                runtime.Mode,
                runtime,
                activeRun,
                RiskConfirmed,
                ExpectedRevision: runtime.ActiveRevision));
            StatusMessage = $"已写入 {SelectedPoint.Code}";
            await RefreshAsync();
        }
        catch (DeviceWriteUncertainException)
        {
            StatusMessage = "结果待核对";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private bool CanWrite() => CanWriteSelectedPoint;

    private async Task ExecuteDigitalOutputAsync(
        DigitalOutputPointViewModel output,
        bool target,
        string displayName)
    {
        if (!await _writeGate.WaitAsync(0))
            return;
        output.SetWriteState(ProcessWriteState.Confirming);
        try
        {
            var runtime = _services.DeviceModes.Runtime
                ?? throw new DomainException("设备运行时未初始化");
            var point = ResolveCurrentPoint(output);
            await EnsureActiveRunAsync();
            if (point.RiskLevel == WriteRiskLevel.HighRisk
                && !await ConfirmAsync(
                    "确认输出操作",
                    $"即将{(target ? "打开" : "关闭")}{displayName}，请确认现场条件和操作意图。"))
            {
                output.SetWriteState(ProcessWriteState.Failed, "已取消操作");
                return;
            }

            output.SetWriteState(ProcessWriteState.Writing);
            var submittedUtc = DateTime.UtcNow;
            await _services.WritePipeline.ExecuteAsync(new WriteCommand(
                _actor,
                point,
                target,
                runtime.Mode,
                runtime,
                ActiveRunOk: true,
                RiskConfirmed: true,
                InterlockCheck: CreateInterlockCheck(runtime),
                ExpectedRevision: runtime.ActiveRevision));

            output.SetWriteState(ProcessWriteState.AwaitingFeedback);
            if (!output.FeedbackSource?.IsBound ?? true)
            {
                output.SetWriteState(ProcessWriteState.Succeeded, "指令已确认，动作未验证");
                StatusMessage = $"{displayName}指令已确认，动作未验证";
                return;
            }

            if (await WaitForDigitalFeedbackAsync(
                    runtime,
                    output.FeedbackSource!,
                    target,
                    submittedUtc,
                    TimeSpan.FromSeconds(3),
                    CancellationToken.None))
            {
                output.SetWriteState(ProcessWriteState.Succeeded, target ? "反馈已到位" : "反馈已关闭");
                StatusMessage = $"{displayName}反馈已到位";
            }
            else
            {
                output.SetWriteState(ProcessWriteState.Failed, "反馈未到位");
                StatusMessage = $"{displayName}反馈未到位";
            }
        }
        catch (OperationCanceledException)
        {
            output.SetWriteState(ProcessWriteState.Uncertain, "结果待核对");
            StatusMessage = "结果待核对";
        }
        catch (DeviceWriteUncertainException)
        {
            output.SetWriteState(ProcessWriteState.Uncertain, "结果待核对");
            StatusMessage = "结果待核对";
        }
        catch (DeviceWriteAuditException)
        {
            output.SetWriteState(ProcessWriteState.Uncertain, "结果待核对");
            StatusMessage = "结果待核对";
        }
        catch (Exception ex)
        {
            output.SetWriteState(ProcessWriteState.Failed, ToCustomerMessage(ex));
            StatusMessage = ToCustomerMessage(ex);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task ExecuteAnalogOutputAsync(AnalogOutputPointViewModel output)
    {
        if (!await _writeGate.WaitAsync(0))
            return;
        output.SetWriteState(ProcessWriteState.Confirming);
        try
        {
            if (!output.TryGetTarget(out var target))
                throw new DomainException(output.ValidationMessage);
            var runtime = _services.DeviceModes.Runtime
                ?? throw new DomainException("设备运行时未初始化");
            var point = ResolveCurrentPoint(output);
            await EnsureActiveRunAsync();
            if (point.RiskLevel == WriteRiskLevel.HighRisk
                && !await ConfirmAsync(
                    "确认调压操作",
                    $"即将把调压目标设为 {target.ToString(CultureInfo.InvariantCulture)} MPa，请确认现场条件和操作意图。"))
            {
                output.SetWriteState(ProcessWriteState.Failed, "已取消操作");
                return;
            }

            output.SetWriteState(ProcessWriteState.Writing);
            var submittedUtc = DateTime.UtcNow;
            await _services.WritePipeline.ExecuteAsync(new WriteCommand(
                _actor,
                point,
                target,
                runtime.Mode,
                runtime,
                ActiveRunOk: true,
                RiskConfirmed: true,
                InterlockCheck: CreateInterlockCheck(runtime),
                ExpectedRevision: runtime.ActiveRevision));

            output.SetWriteState(ProcessWriteState.AwaitingFeedback);
            if (!PressureSetpointReadback.IsBound)
            {
                output.SetWriteState(ProcessWriteState.Succeeded, "指令已确认，未配置独立回读");
                StatusMessage = "调压指令已确认，未配置独立回读";
                return;
            }

            if (await WaitForAnalogFeedbackAsync(
                    runtime,
                    PressureSetpointReadback,
                    target,
                    submittedUtc,
                    TimeSpan.FromSeconds(3),
                    CancellationToken.None))
            {
                output.SetWriteState(ProcessWriteState.Succeeded, "设定回读已确认");
                StatusMessage = "调压设定回读已确认";
            }
            else
            {
                output.SetWriteState(ProcessWriteState.Failed, "设定回读未确认");
                StatusMessage = "设定回读未确认";
            }
        }
        catch (OperationCanceledException)
        {
            output.SetWriteState(ProcessWriteState.Uncertain, "结果待核对");
            StatusMessage = "结果待核对";
        }
        catch (DeviceWriteUncertainException)
        {
            output.SetWriteState(ProcessWriteState.Uncertain, "结果待核对");
            StatusMessage = "结果待核对";
        }
        catch (DeviceWriteAuditException)
        {
            output.SetWriteState(ProcessWriteState.Uncertain, "结果待核对");
            StatusMessage = "结果待核对";
        }
        catch (Exception ex)
        {
            output.SetWriteState(ProcessWriteState.Failed, ToCustomerMessage(ex));
            StatusMessage = ToCustomerMessage(ex);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private DevicePoint ResolveCurrentPoint(ProcessPointViewModel point)
        => !point.IsBound
            ? throw new DomainException($"{point.DisplayName}未绑定")
            : (_services.DeviceModes.Runtime?.GetPoint(point.PointId)
                ?? throw new DomainException($"{point.DisplayName}绑定点位不存在"));

    private async Task EnsureActiveRunAsync()
    {
        if (await _services.RecordRepository.GetActiveRunningRecordAsync() is null)
            throw new DomainException("当前状态不允许手动操作");
    }

    private async Task<bool> ConfirmAsync(string title, string message)
    {
        if (ConfirmationHandler is null)
            return false;
        return await ConfirmationHandler(title, message, CancellationToken.None);
    }

    private Func<CancellationToken, Task> CreateInterlockCheck(IDeviceRuntime runtime)
        => ct =>
        {
            ct.ThrowIfCancellationRequested();
            if (!runtime.IsSimulation)
                throw new DomainException("现场联锁未配置，硬件输出已禁用");
            if (SafetyDoor.State != ProcessDataState.Good || SafetyDoor.IsActive != true
                || ClampReady.State != ProcessDataState.Good || ClampReady.IsActive != true)
                throw new DomainException("安全门或夹紧到位未确认，禁止输出");
            return Task.CompletedTask;
        };

    private static async Task<bool> WaitForDigitalFeedbackAsync(
        IDeviceRuntime runtime,
        DigitalInputPointViewModel feedback,
        bool expected,
        DateTime submittedUtc,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow <= deadline)
        {
            if (runtime.TryGetCachedValue(feedback.PointId, out var value)
                && value.Quality == PointQuality.Good
                && value.TimestampUtc >= submittedUtc
                && IsCurrentSample(runtime, value, feedback.PointId)
                && TryConvertBool(value.Value, out var actual)
                && actual == expected)
                return true;
            await Task.Delay(50, ct);
        }
        return false;
    }

    private static async Task<bool> WaitForAnalogFeedbackAsync(
        IDeviceRuntime runtime,
        AnalogInputPointViewModel feedback,
        decimal expected,
        DateTime submittedUtc,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        const decimal epsilon = 0.000001m;
        while (DateTime.UtcNow <= deadline)
        {
            if (runtime.TryGetCachedValue(feedback.PointId, out var value)
                && value.Quality == PointQuality.Good
                && value.TimestampUtc >= submittedUtc
                && IsCurrentSample(runtime, value, feedback.PointId)
                && TryConvertDecimal(value.Value, out var actual)
                && Math.Abs(actual - expected) <= Math.Max(0.0005m, epsilon))
                return true;
            await Task.Delay(50, ct);
        }
        return false;
    }

    private static bool IsCurrentSample(IDeviceRuntime runtime, PointValue value, string pointId)
    {
        var point = runtime.GetPoint(pointId);
        if (point is null)
            return false;
        if (!string.IsNullOrWhiteSpace(runtime.ActiveRevision)
            && !string.Equals(value.Revision, runtime.ActiveRevision, StringComparison.Ordinal))
            return false;
        var status = runtime.GetDeviceStatus(point.DeviceId);
        return value.ConnectionGeneration == 0
            || status.ConnectionGeneration == 0
            || value.ConnectionGeneration == status.ConnectionGeneration;
    }

    private static bool TryConvertBool(object? value, out bool result)
    {
        if (value is bool boolean)
        {
            result = boolean;
            return true;
        }
        if (value is string text && bool.TryParse(text, out result))
            return true;
        try
        {
            result = value is not null
                && Convert.ToDecimal(value, CultureInfo.InvariantCulture) != 0m;
            return value is not null;
        }
        catch (Exception) when (value is not null)
        {
            result = false;
            return false;
        }
    }

    private static bool TryConvertDecimal(object? value, out decimal result)
    {
        try
        {
            if (value is not null)
            {
                result = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                return true;
            }
        }
        catch (Exception) when (value is not null)
        {
        }
        result = 0m;
        return false;
    }

    private static string ToCustomerMessage(Exception ex)
        => ex is DomainException ? ex.Message : "操作未完成，请查看日志并核对现场状态";
}
