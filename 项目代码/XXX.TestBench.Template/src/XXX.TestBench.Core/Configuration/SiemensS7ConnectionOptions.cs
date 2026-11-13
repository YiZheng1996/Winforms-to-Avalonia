using XXX.TestBench.Core.Common;

namespace XXX.TestBench.Core.Configuration;

/// <summary>
/// Siemens S7 连接端点。硬件端点归属于设备，不再从共享通道借用目标地址。
/// </summary>
public sealed class SiemensS7ConnectionOptions
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 102;
    public int Rack { get; set; }
    public int Slot { get; set; }

    /// <summary>
    /// 校验 S7 主机、端口、机架和插槽范围。
    /// </summary>
    public IReadOnlyList<ConfigurationIssue> Validate(string path)
    {
        var issues = new List<ConfigurationIssue>();
        if (string.IsNullOrWhiteSpace(Host)) issues.Add(new(path + ".host", "不能为空"));
        if (Port is < 1 or > 65535) issues.Add(new(path + ".port", "必须在 1-65535 范围内"));
        if (Rack is < 0 or > short.MaxValue) issues.Add(new(path + ".rack", $"必须在 0-{short.MaxValue} 范围内"));
        if (Slot is < 0 or > short.MaxValue) issues.Add(new(path + ".slot", $"必须在 0-{short.MaxValue} 范围内"));
        return issues;
    }
}
