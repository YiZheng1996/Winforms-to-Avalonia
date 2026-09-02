using XXX.TestBench.Core.Common;

namespace XXX.TestBench.Core.Execution;

public interface ITestItemExecutorFactory
{
    /// <summary>
    /// 按执行器代码解析执行器；未注册时抛 DomainException。
    /// </summary>
    ITestItemExecutor Get(string executorCode);
}
