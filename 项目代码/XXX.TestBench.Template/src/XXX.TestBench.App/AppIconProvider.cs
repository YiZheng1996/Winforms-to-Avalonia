using Avalonia.Controls;
using Avalonia.Platform;

namespace XXX.TestBench.App;

/// <summary>
/// 统一提供桌面窗口使用的小尺寸 ico，避免主窗体与弹窗出现不同图标。
/// </summary>
internal static class AppIconProvider
{
    /// <summary>
    /// 应用图标资源地址。
    /// </summary>
    private static readonly Uri IconUri = new("avares://XXX.TestBench.App/Assets/app-icon.ico");

    /// <summary>
    /// 加载并返回应用图标；加载失败时返回空。
    /// </summary>
    public static WindowIcon? Create()
    {
        try
        {
            using var iconStream = AssetLoader.Open(IconUri);
            return new WindowIcon(iconStream);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"加载应用图标失败：{ex}");
            return null;
        }
    }
}
