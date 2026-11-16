using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace ModbusAvalonia
{
    /// <summary>Avalonia 应用入口，负责加载 XAML 并启动主窗口。</summary>
    public partial class App : Application
    {
        /// <summary>加载 Avalonia XAML 资源。</summary>
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        /// <summary>框架初始化完成后，创建并显示主窗口。</summary>
        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow();
            }
            base.OnFrameworkInitializationCompleted();
        }
    }
}
