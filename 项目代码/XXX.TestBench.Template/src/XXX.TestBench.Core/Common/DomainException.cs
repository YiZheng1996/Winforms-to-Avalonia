namespace XXX.TestBench.Core.Common;

/// <summary>领域规则违反时抛出的异常。</summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}
