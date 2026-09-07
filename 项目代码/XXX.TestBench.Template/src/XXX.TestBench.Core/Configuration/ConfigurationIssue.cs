namespace XXX.TestBench.Core.Configuration;

/// <summary>
/// 可定位到配置字段的校验问题。
/// </summary>
public sealed record ConfigurationIssue(string Path, string Message)
{
    public override string ToString() => $"{Path}：{Message}";
}
