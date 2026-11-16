namespace XXX.TestBench.Core.Configuration;

/// <summary>
/// 通信通道的传输类型。设备驱动和传输方式是两个独立维度。
/// </summary>
public enum ChannelTransportKind
{
    Unknown = 0,
    Tcp = 1,
    Serial = 2,
    Simulation = 3
}
