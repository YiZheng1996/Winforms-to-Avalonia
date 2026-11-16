namespace XXX.TestBench.Core.Configuration;

/// <summary>
/// 仿真通道参数。该类型是显式配置标记，不打开 TCP 或串口。
/// </summary>
public sealed class SimulationChannelParameters
{
    public string InstanceKey { get; set; } = string.Empty;
}
