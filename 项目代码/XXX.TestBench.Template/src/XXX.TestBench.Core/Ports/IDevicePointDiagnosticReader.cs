using XXX.TestBench.Core.Application;

namespace XXX.TestBench.Core.Ports;

/// <summary>
/// 手工点位诊断读取端口。接口位于端口层，保证 App 只依赖 UI 无关的目标和结果 DTO。
/// </summary>
public interface IDevicePointDiagnosticReader
{
    Task<DevicePointDiagnosticReadResult> ReadAsync(
        IReadOnlyList<DevicePointDiagnosticTarget> targets,
        CancellationToken ct = default);
}
