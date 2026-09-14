using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Infrastructure.Logging;

/// <summary>
/// 设备通信事件环形缓存。UI 读取结构化事件，不解析文本日志文件。
/// </summary>
public sealed class InMemoryDeviceEventHub : IDeviceEventSink
{
    private const int Capacity = 500;
    private readonly object _gate = new();
    private readonly Queue<DeviceCommunicationEvent> _events = new();

    public event EventHandler<DeviceCommunicationEvent>? EventReceived;

    public void Publish(DeviceCommunicationEvent value)
    {
        ArgumentNullException.ThrowIfNull(value);
        EventHandler<DeviceCommunicationEvent>? handlers;
        lock (_gate)
        {
            _events.Enqueue(value);
            while (_events.Count > Capacity)
                _events.Dequeue();
            handlers = EventReceived;
        }

        if (handlers is null) return;
        foreach (EventHandler<DeviceCommunicationEvent> handler in handlers.GetInvocationList())
        {
            try { handler(this, value); } catch { /* UI 订阅者异常不能阻断通信线程 */ }
        }
    }

    public IReadOnlyList<DeviceCommunicationEvent> Snapshot(int limit = 200)
    {
        lock (_gate)
        {
            var take = Math.Clamp(limit, 0, Capacity);
            return _events.Reverse().Take(take).ToList();
        }
    }
}
