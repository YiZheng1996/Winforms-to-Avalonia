using XXX.TestBench.Core.Common;

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

/// <summary>
/// 应用主配置信息。
/// </summary>
public sealed class AppConfig
{
    /// <summary>
    /// 配置结构版本号，必须与程序支持的版本一致。
    /// </summary>
    public int SchemaVersion { get; set; }
    /// <summary>
    /// 系统名称。
    /// </summary>
    public required string SystemName { get; set; }
    /// <summary>
    /// 品牌名称。
    /// </summary>
    public required string Brand { get; set; }
    /// <summary>
    /// 界面语言。
    /// </summary>
    public required string Language { get; set; }
    /// <summary>
    /// 功能模块启用开关集合。
    /// </summary>
    public Dictionary<string, bool> Modules { get; set; } = new();
    /// <summary>
    /// 默认文件路径。
    /// </summary>
    public AppPaths DefaultPaths { get; set; } = new();

    /// <summary>
    /// 当前支持的配置版本号。
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// 校验配置，版本号或必填项不合法时直接报错。
    /// </summary>
    public void Validate()
    {
        // 版本不匹配时拒绝启动，避免错误结构被当作合法配置使用。
        if (SchemaVersion != CurrentSchemaVersion)
            throw new ConfigValidationException($"app.json schemaVersion={SchemaVersion} 不受支持（期望 {CurrentSchemaVersion}）");
        // 系统名称不能为空。
        if (string.IsNullOrWhiteSpace(SystemName)) throw new ConfigValidationException("app.json 缺少 systemName");
        // 默认数据库路径不能为空。
        if (string.IsNullOrWhiteSpace(DefaultPaths.Database)) throw new ConfigValidationException("app.json 缺少 defaultPaths.database");
    }
}
