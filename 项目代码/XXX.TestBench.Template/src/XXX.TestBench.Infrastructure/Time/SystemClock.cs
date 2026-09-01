using XXX.TestBench.Core.Common;

namespace XXX.TestBench.Infrastructure.Time;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
