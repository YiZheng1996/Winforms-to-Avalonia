namespace XXX.TestBench.Core.Configuration;

/// <summary>
/// 应用默认文件路径集合。
/// </summary>
public sealed class AppPaths
{
    /// <summary>
    /// 试验数据库文件路径。
    /// </summary>
    public string Database { get; set; } = string.Empty;

    /// <summary>
    /// 报表导出目录。
    /// </summary>
    public string ReportOutput { get; set; } = string.Empty;

    /// <summary>
    /// 报表模板目录。
    /// </summary>
    public string ReportTemplates { get; set; } = string.Empty;
}
