using XXX.TestBench.Core.Domain.Devices;

namespace XXX.TestBench.Core.Ports;

public interface IDeviceRuntimeFactory
{
    /// <summary>
    /// 按配置模式创建运行时。Hardware 无可用适配器时必须失败，不得回退 Simulation。
    /// </summary>
    Task<IDeviceRuntime> CreateAsync(DeviceMode mode, CancellationToken ct = default);
}
