namespace XXX.TestBench.Core.Ports;

/// <summary>
/// 应用日志接口，提供信息、警告、错误三级记录。
/// </summary>
public interface IAppLogger
{
    /// <summary>
    /// 记录一条普通信息。
    /// </summary>
    void Info(string message);
    /// <summary>
    /// 记录一条警告。
    /// </summary>
    void Warn(string message);
    /// <summary>
    /// 记录一条错误及关联异常。
    /// </summary>
    void Error(string message, Exception? exception = null);
}
