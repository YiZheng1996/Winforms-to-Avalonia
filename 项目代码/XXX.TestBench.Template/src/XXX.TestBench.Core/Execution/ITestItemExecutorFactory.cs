using XXX.TestBench.Core.Common;

namespace XXX.TestBench.Core.Execution;

/// <summary>
/// 试验项点执行器工厂。
/// </summary>
public interface ITestItemExecutorFactory
{
    /// <summary>
    /// 按执行器代码解析执行器；未注册时抛 DomainException。
    /// </summary>
    ITestItemExecutor Get(string executorCode);

    /// <summary>
    /// 已注册的执行器代码集合，供项点管理界面选择“关联逻辑类”。
    /// </summary>
    IReadOnlyCollection<string> Codes { get; }
}
