using Avalonia.Threading;
using XXX.TestBench.Gateway.Domain;

namespace XXX.TestBench.Avalonia.Composition;

public sealed class UiGatewayLogSink : IGatewayLogSink
{
    public event Action<GatewayLogEntry>? EntryWritten;

    public void Write(GatewayLogEntry entry)
    {
        void Publish() => EntryWritten?.Invoke(entry);
        if (Dispatcher.UIThread.CheckAccess())
            Publish();
        else
            Dispatcher.UIThread.Post(Publish);
    }
}
