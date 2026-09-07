using XXX.TestBench.Core.Common;

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
/// <summary>
/// TCP 通道参数。
/// </summary>
public sealed class TcpChannelParameters
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
}

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

/// <summary>
/// 仿真通道参数。该类型是显式配置标记，不打开 TCP 或串口。
/// </summary>
public sealed class SimulationChannelParameters
{
    public string InstanceKey { get; set; } = string.Empty;
}

/// <summary>
/// 通道配置。通道是串口/TCP 等共享资源的唯一所有者。
/// </summary>
public sealed class ChannelEntry
{
    public string Id { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public ChannelTransportKind TransportKind { get; set; }
    public bool Enabled { get; set; } = true;
    public int TimeoutMs { get; set; } = 1000;
    public int RetryCount { get; set; }
    public TcpChannelParameters? Tcp { get; set; }
    public SerialChannelParameters? Serial { get; set; }
    public SimulationChannelParameters? Simulation { get; set; }

    /// <summary>
    /// 校验通道自身字段以及“传输类型与参数恰好匹配”约束。
    /// </summary>
    public IReadOnlyList<ConfigurationIssue> Validate(string path)
    {
        var issues = new List<ConfigurationIssue>();
        if (!Guid.TryParse(Id, out _)) issues.Add(new(path + ".id", "必须是有效 GUID"));
        if (string.IsNullOrWhiteSpace(Code)) issues.Add(new(path + ".code", "不能为空"));
        if (string.IsNullOrWhiteSpace(Name)) issues.Add(new(path + ".name", "不能为空"));
        if (TimeoutMs <= 0) issues.Add(new(path + ".timeoutMs", "必须大于 0"));
        if (RetryCount < 0) issues.Add(new(path + ".retryCount", "不能小于 0"));

        var selected = new[]
        {
            Tcp is not null,
            Serial is not null,
            Simulation is not null
        }.Count(value => value);
        if (selected != 1)
            issues.Add(new(path, "TCP、Serial、Simulation 参数必须恰好配置一个"));

        switch (TransportKind)
        {
            case ChannelTransportKind.Tcp:
                if (Tcp is null) issues.Add(new(path + ".tcp", "TCP 通道必须配置 TCP 参数"));
                if (Serial is not null || Simulation is not null) issues.Add(new(path, "TCP 通道不能同时配置其他传输参数"));
                if (Tcp is not null)
                {
                    if (string.IsNullOrWhiteSpace(Tcp.Host)) issues.Add(new(path + ".tcp.host", "不能为空"));
                    if (Tcp.Port is < 1 or > 65535) issues.Add(new(path + ".tcp.port", "必须在 1-65535 范围内"));
                }
                break;
            case ChannelTransportKind.Serial:
                if (Serial is null) issues.Add(new(path + ".serial", "串口通道必须配置串口参数"));
                if (Tcp is not null || Simulation is not null) issues.Add(new(path, "串口通道不能同时配置其他传输参数"));
                ValidateSerial(path, Serial, issues);
                break;
            case ChannelTransportKind.Simulation:
                if (Simulation is null) issues.Add(new(path + ".simulation", "仿真通道必须配置仿真参数"));
                if (Tcp is not null || Serial is not null) issues.Add(new(path, "仿真通道不能同时配置 TCP 或串口参数"));
                if (Simulation is not null && string.IsNullOrWhiteSpace(Simulation.InstanceKey))
                    issues.Add(new(path + ".simulation.instanceKey", "不能为空"));
                break;
            default:
                issues.Add(new(path + ".transportKind", "不受支持"));
                break;
        }

        return issues;
    }

    private static void ValidateSerial(string path, SerialChannelParameters? serial, ICollection<ConfigurationIssue> issues)
    {
        if (serial is null) return;
        if (string.IsNullOrWhiteSpace(serial.PortName)) issues.Add(new(path + ".serial.portName", "不能为空"));
        if (serial.BaudRate <= 0) issues.Add(new(path + ".serial.baudRate", "必须大于 0"));
        if (serial.DataBits is < 5 or > 8) issues.Add(new(path + ".serial.dataBits", "必须在 5-8 范围内"));
        if (string.IsNullOrWhiteSpace(serial.Parity)) issues.Add(new(path + ".serial.parity", "不能为空"));
        if (string.IsNullOrWhiteSpace(serial.StopBits)) issues.Add(new(path + ".serial.stopBits", "不能为空"));
    }
}
