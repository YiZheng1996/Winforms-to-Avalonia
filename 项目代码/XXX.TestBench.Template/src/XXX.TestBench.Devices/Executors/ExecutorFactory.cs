using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Execution;

namespace XXX.TestBench.Devices.Executors;

/// <summary>执行器注册表。未注册的执行器代码明确失败，不允许运行时编译任意 C#。</summary>
public sealed class ExecutorFactory : ITestItemExecutorFactory
{
    private readonly IReadOnlyDictionary<string, ITestItemExecutor> _executors;

    public ExecutorFactory()
    {
        _executors = new Dictionary<string, ITestItemExecutor>(StringComparer.Ordinal)
        {
            [new SimulationPressureExecutor().ExecutorCode] = new SimulationPressureExecutor()
        };
    }

    public ITestItemExecutor Get(string executorCode)
        => _executors.TryGetValue(executorCode, out var executor)
            ? executor
            : throw new DomainException($"未注册执行器：{executorCode}");
}
