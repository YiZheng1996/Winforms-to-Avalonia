using Avalonia;
using System;

namespace ModbusAvalonia
{
    /// <summary>程序入口，负责构建并启动 Avalonia 桌面应用。</summary>
    internal class Program
    {
        [STAThread]
        public static void Main(string[] args) => BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);

        /// <summary>配置 Avalonia 应用构建器（平台检测、日志跟踪）。</summary>
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();
    }
}
