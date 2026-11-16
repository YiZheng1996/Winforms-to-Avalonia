using System.Text.Json.Serialization;

namespace XXX.TestBench.Core.Configuration;

/// <summary>
/// TCP 通道参数。
/// </summary>
public sealed class TcpChannelParameters
{
    /// <summary>
    /// 可选的本地网卡绑定提示，不用于决定远端 PLC 端点。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string LocalInterface { get; set; } = string.Empty;
}
