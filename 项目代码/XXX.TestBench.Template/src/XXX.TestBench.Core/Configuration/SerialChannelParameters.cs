namespace XXX.TestBench.Core.Configuration;

/// <summary>
/// 串口通道参数。
/// </summary>
public sealed class SerialChannelParameters
{
    public string PortName { get; set; } = string.Empty;
    public int BaudRate { get; set; }
    public int DataBits { get; set; } = 8;
    public string Parity { get; set; } = "None";
    public string StopBits { get; set; } = "One";
}
