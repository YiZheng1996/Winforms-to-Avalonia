using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Tasks;
using XXX.TestBench.Core.Execution;

namespace XXX.TestBench.Devices.Executors;

/// <summary>
/// 代表性仿真执行器（ExecutorCode=PressureExecutor）：读取配方参数 Voltage（目标值），
/// 从仿真运行时读取 AI_Pressure 点值，按 10% 容差判定通过/失败。
/// 具体项目的试验算法通过实现 ITestItemExecutor 注册，不修改本示例。
/// </summary>
public sealed class SimulationPressureExecutor : ITestItemExecutor
{
    public string ExecutorCode => "PressureExecutor";

    public async Task<ItemExecutionOutcome> ExecuteAsync(ItemExecutionContext context, CancellationToken ct = default)
    {
        if (!context.ParameterValues.TryGetValue("Voltage", out var targetRaw) || !decimal.TryParse(targetRaw, out var target))
            return new ItemExecutionOutcome(ItemResultState.Failed, null, "缺少有效参数 Voltage");

        var points = await context.Runtime.ListPointsAsync(ct);
        var pressurePoint = points.FirstOrDefault(p => p.Code == "AI_Pressure");
        if (pressurePoint is null)
            return new ItemExecutionOutcome(ItemResultState.Failed, null, "仿真运行时缺少点位 AI_Pressure");

        var value = await context.Runtime.ReadAsync(pressurePoint, ct);
        if (value.Quality != PointQuality.Good)
            return new ItemExecutionOutcome(ItemResultState.Failed, value.Value?.ToString(), $"点位质量异常（{value.Quality}）");

        var measured = Convert.ToDecimal(value.Value);
        var tolerance = Math.Max(Math.Abs(target) * 0.1m, 0.05m);
        var passed = Math.Abs(measured - target) <= tolerance;
        return new ItemExecutionOutcome(
            passed ? ItemResultState.Passed : ItemResultState.Failed,
            measured.ToString("0.000"),
            $"实测 {measured:0.000} MPa，目标 {target:0.000} kV（容差 10%）");
    }
}
