namespace XXX.TestBench.App.Composition;

/// <summary>
/// 安装尚未登记时使用的明确启动状态，供应用打开首次配置界面。
/// </summary>
public sealed class InstallationSettingsMissingException : InvalidOperationException
{
    public InstallationSettingsMissingException(string settingsPath)
        : base($"未找到安装级设置：{settingsPath}。请通过首次启动配置选择新建数据库或登记已有库；不会自动采用 app.json 路径或创建空库。")
    {
        SettingsPath = settingsPath;
    }

    public string SettingsPath { get; }
}
