namespace XXX.TestBench.Core.Domain.Devices;

/// <summary>
/// 设备运行模式。DeviceMode 是启动配置，不是页面随意切换的按钮。
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
    Hardware = 1
}
