namespace XXX.TestBench.Core.Domain.Devices;

public enum DeviceEventSeverity
{
    Information,
    Warning,
    Error
}

public sealed record DeviceCommunicationEvent(
    DateTime TimestampUtc,
    DeviceEventSeverity Severity,
    string ChannelId,
    string DeviceId,
    string Source,
    string EventCode,
    string Message);
