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
    /// 执行器代码。
    /// </summary>
    public string ExecutorCode => "PressureExecutor";

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

        var points = await context.Runtime.ListPointsAsync(ct);
        var pressurePoint = points.FirstOrDefault(p => p.Code == "AI_Pressure");
        if (pressurePoint is null)
            return new ItemExecutionOutcome(ItemResultState.Failed, null, "仿真运行时缺少点位 AI_Pressure");

        var read = await context.Runtime.ReadAsync(pressurePoint, ct);
        if (read.Quality != PointQuality.Good)
            return new ItemExecutionOutcome(ItemResultState.Failed, read.Value?.ToString(), $"点位质量异常（{read.Quality}）");

        var summary = $"目标 {effective.TestVoltageV:0.0}V / {effective.ProtectCurrentMa:0.0}mA / {effective.TestTimeSeconds}s";
        var text = $"仿真执行通过：参数快照解析校验成功，AI_Pressure 当前值 {read.Value}";
        return new ItemExecutionOutcome(ItemResultState.Passed, summary, text);
    }
}
