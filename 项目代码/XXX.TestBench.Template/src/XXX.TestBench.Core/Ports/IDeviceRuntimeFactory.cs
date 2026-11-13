using XXX.TestBench.Core.Domain.Devices;

namespace XXX.TestBench.Core.Ports;

/// <summary>
/// 设备运行时工厂边界。
/// </summary>
public interface IDeviceRuntimeFactory
{
    Task<IDeviceRuntime> CreateAsync(CancellationToken ct = default);
}
