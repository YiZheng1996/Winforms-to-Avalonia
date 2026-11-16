using XXX.TestBench.Core.Domain.Reports;

namespace XXX.TestBench.Core.Ports;

/// <summary>
/// 从已持久化的报表记录和冻结资源快照构造计算请求。
/// 实现不得读取当前产品配置替代运行时保存的输入与资源版本。
/// </summary>
public interface IReportCalculationRequestFactory
{
    Task<ReportCalculationRequest> CreateAsync(
        ReportRecord report,
        CancellationToken cancellationToken = default);
}
