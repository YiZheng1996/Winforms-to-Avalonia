namespace XXX.TestBench.Core.Domain.Devices;

/// <summary>
/// 设备通信事件的严重程度。
/// </summary>
public enum DeviceEventSeverity
{
    /// <summary>正常状态或过程信息。</summary>
    Information,

    /// <summary>需要关注但未阻断通信的情况。</summary>
    Warning,

    /// <summary>通信或点位处理失败。</summary>
    Error
}

/// <summary>
/// 一条结构化设备通信事件，供界面和诊断日志按时间追踪。
/// </summary>
/// <param name="TimestampUtc">事件发生时间（UTC）。</param>
/// <param name="Severity">事件严重程度。</param>
/// <param name="ChannelId">关联通道编号。</param>
/// <param name="DeviceId">关联设备编号。</param>
/// <param name="Source">产生事件的运行组件。</param>
/// <param name="EventCode">用于筛选和统计的稳定事件代码。</param>
/// <param name="Message">面向操作人员或维护人员的说明。</param>
public sealed record DeviceCommunicationEvent(
    DateTime TimestampUtc,
    DeviceEventSeverity Severity,
    string ChannelId,
    string DeviceId,
    string Source,
    string EventCode,
    string Message);
