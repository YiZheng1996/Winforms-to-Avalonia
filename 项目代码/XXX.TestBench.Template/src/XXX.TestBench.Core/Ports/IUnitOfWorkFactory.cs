namespace XXX.TestBench.Core.Ports;

/// <summary>
/// 工作单元工厂，负责创建工作单元。
/// </summary>
public interface IUnitOfWorkFactory
{
    /// <summary>
    /// 新建一个工作单元。
    /// </summary>
    IUnitOfWork Create();
}
