using XXX.TestBench.Core.Application;

namespace XXX.TestBench.Core.Ports;

/// <summary>
/// 工艺页面的统一缓存快照读取边界。实现不得触发硬件读取。
/// </summary>
public interface IProcessSnapshotReader
{
    IReadOnlyDictionary<string, ProcessSample> Capture(
        IDeviceRuntime runtime,
        DateTime utcNow);
}
