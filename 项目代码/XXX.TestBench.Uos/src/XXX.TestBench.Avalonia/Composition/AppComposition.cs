using XXX.TestBench.Avalonia.ViewModels;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Gateway.Domain;
using XXX.TestBench.Gateway.Infrastructure;

namespace XXX.TestBench.Avalonia.Composition;

public sealed class AppComposition : IAsyncDisposable
{
    private readonly ReadOnlyGatewayRuntime? _runtime;

    private AppComposition(MainWindowViewModel mainWindow, ReadOnlyGatewayRuntime? runtime)
    {
        MainWindow = mainWindow;
        _runtime = runtime;
    }

    public MainWindowViewModel MainWindow { get; }

    public static AppComposition Create()
    {
        var log = new UiGatewayLogSink();
        ReadOnlyGatewayRuntime? runtime = null;
        try
        {
            var configPath = ConfigLocator.Resolve();
            var settings = GatewaySettingsFile.Load(configPath);
            runtime = ReadOnlyGatewayRuntimeFactory.Create(settings, log);
            var viewModel = new MainWindowViewModel(
                new TestBenchStateMachine(),
                runtime,
                log,
                configPath,
                settings.Gateway.TransportMode);
            return new AppComposition(viewModel, runtime);
        }
        catch (Exception ex)
        {
            runtime?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            var viewModel = new MainWindowViewModel(
                new TestBenchStateMachine(),
                runtime: null,
                log,
                ConfigLocator.Resolve(),
                transportMode: "Unavailable",
                startupError: $"配置或 Gateway 初始化失败：{ex.Message}");
            return new AppComposition(viewModel, runtime: null);
        }
    }

    public async ValueTask DisposeAsync()
    {
        MainWindow.Dispose();
        if (_runtime is not null)
            await _runtime.DisposeAsync().ConfigureAwait(false);
    }
}

internal static class ConfigLocator
{
    public static string Resolve()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "config", "gatewaysettings.json"),
            Path.Combine(Environment.CurrentDirectory, "config", "gatewaysettings.json")
        };
        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }
}
