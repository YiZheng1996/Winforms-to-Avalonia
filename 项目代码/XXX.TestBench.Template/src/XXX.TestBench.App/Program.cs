using Avalonia;

namespace XXX.TestBench.App;

/// <summary>
/// 程序入口。
/// </summary>
internal static class Program
{
    [STAThread]
    /// <summary>
    /// 程序入口，启动桌面应用。
    /// </summary>
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    /// <summary>
    /// 构建应用实例。
    /// </summary>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
