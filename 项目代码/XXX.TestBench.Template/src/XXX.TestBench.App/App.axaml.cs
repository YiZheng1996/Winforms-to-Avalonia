using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using XXX.TestBench.App.Composition;

namespace XXX.TestBench.App;

public partial class App : Application
{
    private AppComposition? _composition;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var baseDir = AppContext.BaseDirectory;
            var configRoot = Path.Combine(baseDir, "config");
            var dataRoot = Path.Combine(baseDir, "data");
            _composition = AppComposition.Create(configRoot, dataRoot);
            desktop.MainWindow = new MainWindow { DataContext = _composition.Shell };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
