using XXX.TestBench.Core.Common;

namespace XXX.TestBench.Core.Configuration;

/// <summary>
/// Modbus TCP 的设备专属端点。
///
/// TCP 通道只负责调度和生命周期，不再持有远端目标地址；因此 Modbus TCP
/// 和 S7 一样，把 Host/Port 放在设备项中，避免同一 TCP 通道上的设备互相串用地址。
/// </summary>
public sealed class ModbusTcpConnectionOptions
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 502;

    /// <summary>
    /// 校验 Modbus TCP 主机和端口范围。
    /// </summary>
    public IReadOnlyList<ConfigurationIssue> Validate(string path)
    {
        var issues = new List<ConfigurationIssue>();
        if (string.IsNullOrWhiteSpace(Host))
            issues.Add(new(path + ".host", "不能为空"));
        if (Port is < 1 or > 65535)
            issues.Add(new(path + ".port", "必须在 1-65535 范围内"));
        return issues;
    }
}
