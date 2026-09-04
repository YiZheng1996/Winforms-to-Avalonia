using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Execution;

namespace XXX.TestBench.Devices.Executors;

/// <summary>
/// 执行器注册表（关联逻辑类）。未注册的执行器代码明确失败，不允许运行时编译任意 C#。
/// </summary>
public sealed class ExecutorFactory : ITestItemExecutorFactory
{
    /// <summary>
    /// 已注册执行器字典，键为执行器代码。
    /// </summary>
    private readonly IReadOnlyDictionary<string, ITestItemExecutor> _executors;

    /// <summary>
    /// 创建执行器工厂并注册内置执行器。
    /// </summary>
    public ExecutorFactory()
    {
        _executors = new Dictionary<string, ITestItemExecutor>(StringComparer.Ordinal)
        {
            [new SimulationPressureExecutor().ExecutorCode] = new SimulationPressureExecutor()
        };
    }

    /// <summary>
    /// 按代码返回执行器，未注册时报错。
    /// </summary>
    public ITestItemExecutor Get(string executorCode)
        => _executors.TryGetValue(executorCode, out var executor)
            ? executor
            : throw new DomainException($"未注册执行器：{executorCode}");

    /// <summary>
    /// 已注册的执行器代码集合。
    /// </summary>
    public IReadOnlyCollection<string> Codes => _executors.Keys.ToList();
}
