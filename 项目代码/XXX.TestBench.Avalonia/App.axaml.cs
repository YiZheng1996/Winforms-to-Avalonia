using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using XXX.TestBench.Avalonia.Composition;

namespace XXX.TestBench.Avalonia;

public partial class App : Application
{
    private AppComposition? _composition;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _composition = AppComposition.Create();
            desktop.MainWindow = new MainWindow
            {
                DataContext = _composition.MainWindow
            };
            desktop.Exit += (_, _) => _composition.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _ = _composition.MainWindow.StartAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
