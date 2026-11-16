using XXX.TestBench.Core.Domain.Reports;

namespace XXX.TestBench.Core.Execution;

/// <summary>
/// 仿真组合根显式提供的冻结资源和身份。没有默认模板、假授权或硬件放行选项。
/// 报表引擎及真实资源验收完成前，不在生产组合根注册执行入口。
/// </summary>
public sealed record SimulationRunOptions(
    string BenchId,
    string DatabaseInstanceId,
    ReportInputResources Resources,
    string TargetTimeZoneId);
