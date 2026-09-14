using XXX.TestBench.Core.Domain.Devices;

namespace XXX.TestBench.Core.Ports;

/// <summary>
/// 设备运行时工厂边界。
/// </summary>
public interface IDeviceRuntimeFactory
{
    /// <summary>
    /// 按设备配置创建运行时；每台设备的 DeviceMode 独立决定是否使用仿真。
    /// </summary>
    Task<IDeviceRuntime> CreateAsync(CancellationToken ct = default)
        => CreateAsync(DeviceMode.Simulation, ct);

    /// <summary>
    /// 旧的显式模式入口，仅保留给已有基础设施调用方；新代码应使用无模式重载。
    /// </summary>
    Task<IDeviceRuntime> CreateAsync(DeviceMode mode, CancellationToken ct = default);
}
