using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Records;
using XXX.TestBench.Core.Execution;

namespace XXX.TestBench.Devices.Executors;

/// <summary>
/// 代表性仿真执行器（ExecutorCode=PressureExecutor）：读取直编参数快照中的
/// 试验电压/保护电流/试验时间并完成参数解析校验，随后读取 AI_Pressure 仿真点位
/// 以佐证设备链路可用。具体项目的真实算法通过实现 ITestItemExecutor 注册。
/// </summary>
public sealed class SimulationPressureExecutor : ITestItemExecutor
{
    /// <summary>
    /// 现有仿真执行逻辑实际读取的点位编码；它现在作为固定 SignalKey 对外声明。
    /// </summary>
    public const string PressureSignalKey = "AI_Pressure";

    /// <summary>
    /// 执行器代码。
    /// </summary>
    public string ExecutorCode => "PressureExecutor";

    /// <summary>
    /// 压力执行器的必需输入信号。实际点位由项目级绑定映射到 PointId。
    /// </summary>
    public IReadOnlyList<RequiredSignal> RequiredSignals { get; } =
    [
        new RequiredSignal(PressureSignalKey, SignalAccessKind.Read,
            DevicePointDataType.Decimal, "MPa", TimeSpan.FromSeconds(2))
    ];

    /// <summary>
    /// 校验参数快照并读取仿真压力点位，演示一次完整执行。
    /// </summary>
    public async Task<ItemExecutionOutcome> ExecuteAsync(ItemExecutionContext context, CancellationToken ct = default)
    {
        var effective = context.EffectiveParameters;
        if (effective is null)
            return new ItemExecutionOutcome(ItemResultState.Failed, null, "缺少有效试验参数快照");

        // 校验已在启动前由 TestParameterValidator 完成，此处仅做防御性复核。
        var validation = XXX.TestBench.Core.Domain.TestParameters.TestParameterValidator.Validate(effective);
        if (!validation.IsValid)
            return new ItemExecutionOutcome(ItemResultState.Failed, null, validation.Error);

        PointValue read;
        if (context.SignalResolver is not null)
        {
            if (context.ResolvedSignals is null)
                return new ItemExecutionOutcome(ItemResultState.Failed, null, "缺少本次试验的信号解析快照");
            read = await context.SignalResolver.ReadFreshAsync(
                PressureSignalKey, context.ResolvedSignals, ct);
        }
        else
        {
            // 仅保留给 v1 兼容运行时；v2 必须经过固定绑定和 PointId 路由。
            var points = await context.Runtime.ListPointsAsync(ct);
            var pressurePoint = points.FirstOrDefault(p => p.Code == PressureSignalKey);
            if (pressurePoint is null)
                return new ItemExecutionOutcome(ItemResultState.Failed, null, $"仿真运行时缺少点位 {PressureSignalKey}");
            read = await context.Runtime.ReadAsync(pressurePoint, ct);
        }
        if (read.Quality != PointQuality.Good)
            return new ItemExecutionOutcome(ItemResultState.Failed, read.Value?.ToString(), $"点位质量异常（{read.Quality}）");

        var summary = $"目标 {effective.TestVoltageV:0.0}V / {effective.ProtectCurrentMa:0.0}mA / {effective.TestTimeSeconds}s";
        var text = $"仿真执行通过：参数快照解析校验成功，AI_Pressure 当前值 {read.Value}";
        return new ItemExecutionOutcome(ItemResultState.Passed, summary, text);
    }
}
