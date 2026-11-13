using XXX.TestBench.Core.Domain.Devices;

namespace XXX.TestBench.Core.Ports;

public interface IDeviceEventSink
{
    event EventHandler<DeviceCommunicationEvent>? EventReceived;
    void Publish(DeviceCommunicationEvent value);
    IReadOnlyList<DeviceCommunicationEvent> Snapshot(int limit = 200);
}
