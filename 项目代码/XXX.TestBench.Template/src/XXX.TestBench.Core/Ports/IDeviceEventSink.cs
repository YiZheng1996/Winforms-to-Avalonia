using XXX.TestBench.Core.Domain.Devices;

namespace XXX.TestBench.Core.Ports;

/// <summary>
/// 发布设备通信事件，并提供近期事件供界面和诊断页面读取。
/// </summary>
public interface IDeviceEventSink
{
    /// <summary>
    /// 有新事件发布时触发。
    /// </summary>
    event EventHandler<DeviceCommunicationEvent>? EventReceived;

    /// <summary>
    /// 发布一条设备通信事件。
    /// </summary>
    void Publish(DeviceCommunicationEvent value);

    /// <summary>
    /// 返回最近的事件，最新事件排在前面；结果数量不超过 limit。
    /// </summary>
    IReadOnlyList<DeviceCommunicationEvent> Snapshot(int limit = 200);
}
