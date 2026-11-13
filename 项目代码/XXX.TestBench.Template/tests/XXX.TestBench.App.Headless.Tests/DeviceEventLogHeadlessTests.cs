using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using XXX.TestBench.App.ViewModels;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.App.Headless.Tests;

public sealed class DeviceEventLogHeadlessTests
{
    [AvaloniaFact]
    public void AttachDetachAndCollapseUseLocalizedDisplayRowsWithoutStaleCallbacks()
    {
        var sink = new FakeEventSink();
        using var log = new DeviceEventLogViewModel(sink);
        var first = Event(DeviceEventSeverity.Warning, "S7_REQUEST_TIMEOUT", "设备请求超时");

        Assert.False(log.IsExpanded);
        Assert.Equal("展开", log.ToggleText);

        sink.Publish(first);
        Assert.Empty(log.VisibleEvents);

        log.ToggleExpanded();
        log.Attach();
        Assert.Single(log.VisibleEvents);
        sink.Publish(first);
        log.Detach();
        RunUiJobs();

        Assert.Single(log.VisibleEvents);
        sink.Clear();

        var second = Event(DeviceEventSeverity.Error, "UNKNOWN_CODE", "连接失败");
        log.Attach();
        sink.Publish(second);
        RunUiJobs();

        var row = Assert.Single(log.VisibleEvents);
        Assert.Equal("错误", row.SeverityText);
        Assert.Equal("设备事件", row.EventNameText);
        Assert.Equal("连接失败", row.MessageText);
        Assert.Contains("事件代码：UNKNOWN_CODE", row.DetailsText, StringComparison.Ordinal);
        Assert.Contains("时间（UTC）", row.DetailsText, StringComparison.Ordinal);

        log.ToggleExpanded();
        sink.Publish(Event(DeviceEventSeverity.Information, "S7_CONNECTED", "已连接"));
        Assert.Single(log.VisibleEvents);
        log.ToggleExpanded();
        Assert.Equal(2, log.VisibleEvents.Count);
    }

    private static void RunUiJobs()
        => Dispatcher.UIThread.RunJobs(DispatcherPriority.Background);

    private static DeviceCommunicationEvent Event(
        DeviceEventSeverity severity,
        string code,
        string message)
        => new(
            new DateTime(2026, 9, 15, 1, 2, 3, DateTimeKind.Utc),
            severity,
            "channel-1",
            "device-1",
            "测试来源",
            code,
            message);

    private sealed class FakeEventSink : IDeviceEventSink
    {
        private readonly List<DeviceCommunicationEvent> _events = new();
        public event EventHandler<DeviceCommunicationEvent>? EventReceived;

        public void Publish(DeviceCommunicationEvent value)
        {
            _events.Insert(0, value);
            EventReceived?.Invoke(this, value);
        }

        public IReadOnlyList<DeviceCommunicationEvent> Snapshot(int limit = 200)
            => _events.Take(limit).ToList();

        public void Clear() => _events.Clear();
    }
}
