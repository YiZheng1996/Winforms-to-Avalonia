namespace XXX.TestBench.Core.Domain.Devices;

/// <summary>
/// 设备运行模式。模式配置在设备级维护；Mixed 仅表示当前运行时包含不同模式的设备。
/// </summary>
public enum DeviceMode
{
    /// <summary>
    /// 模拟模式。
    /// </summary>
    Simulation = 0,
    /// <summary>
    /// 硬件模式。
    /// </summary>
    Hardware = 1,
    /// <summary>
    /// 当前运行时同时包含仿真设备和硬件设备。
    /// </summary>
    Mixed = 2
}
