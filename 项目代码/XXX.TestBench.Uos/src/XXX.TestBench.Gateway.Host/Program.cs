using System.Text.Json;
using System.Text.Json.Serialization;
using XXX.TestBench.Gateway.Domain;
using XXX.TestBench.Gateway.Infrastructure;

return await GatewayHost.RunAsync(args);

internal static class GatewayHost
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<int> RunAsync(string[] args)
    {
        GatewayHostArguments parsed;
        try
        {
            parsed = GatewayHostArguments.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"参数错误: {ex.Message}");
            Console.Error.WriteLine(GatewayHostArguments.Usage);
            return 2;
        }

        if (parsed.ShowHelp)
        {
            Console.WriteLine(GatewayHostArguments.Usage);
            return 0;
        }

        var log = new ConsoleGatewayLogSink();
        try
        {
            var configPath = ResolveConfigPath(parsed.ConfigPath);
            var settings = GatewaySettingsFile.Load(configPath);
            await using var runtime = ReadOnlyGatewayRuntimeFactory.Create(settings, log);

            log.Write(new GatewayLogEntry(
                DateTimeOffset.UtcNow,
                GatewayLogLevel.Information,
                "GatewayHost",
                "Read-only gateway host started",
                new Dictionary<string, string?>
                {
                    ["config"] = configPath,
                    ["transportMode"] = settings.Gateway.TransportMode,
                    ["activeModbusTransport"] = settings.Gateway.ActiveModbusTransport,
                    ["writesEnabled"] = runtime.WritesEnabled.ToString()
                }));

            using var cancellation = new CancellationTokenSource();
            ConsoleCancelEventHandler? cancelHandler = null;
            cancelHandler = (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellation.Cancel();
            };
            Console.CancelKeyPress += cancelHandler;
            try
            {
                if (parsed.Iterations is int iterations)
                {
                    for (var index = 0; index < iterations; index++)
                    {
                        var snapshot = await runtime.PollOnceAsync(cancellation.Token).ConfigureAwait(false);
                        WriteSnapshot(snapshot, index + 1);
                        if (index + 1 < iterations)
                            await Task.Delay(settings.Gateway.PollIntervalMs, cancellation.Token).ConfigureAwait(false);
                    }

                    return runtime.CurrentSnapshot.IsHealthy ? 0 : 1;
                }

                await runtime.RunAsync(cancellation.Token).ConfigureAwait(false);
                return 0;
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                log.Write(new GatewayLogEntry(
                    DateTimeOffset.UtcNow,
                    GatewayLogLevel.Information,
                    "GatewayHost",
                    "Read-only gateway host stopped",
                    new Dictionary<string, string?>()));
                return 0;
            }
            finally
            {
                Console.CancelKeyPress -= cancelHandler;
            }
        }
        catch (Exception ex)
        {
            log.Write(new GatewayLogEntry(
                DateTimeOffset.UtcNow,
                GatewayLogLevel.Error,
                "GatewayHost",
                "Read-only gateway host failed to start",
                new Dictionary<string, string?>(),
                ex.ToString()));
            return 2;
        }
    }

    private static string ResolveConfigPath(string? requestedPath)
    {
        if (!string.IsNullOrWhiteSpace(requestedPath))
            return Path.GetFullPath(requestedPath);

        var candidates = new[]
        {
            Path.Combine(Environment.CurrentDirectory, "config", "gatewaysettings.json"),
            Path.Combine(AppContext.BaseDirectory, "config", "gatewaysettings.json")
        };
        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    private static void WriteSnapshot(GatewayPollSnapshot snapshot, int sequence)
    {
        var output = new
        {
            sequence,
            snapshot.Timestamp,
            snapshot.IsSimulated,
            snapshot.IsHealthy,
            points = snapshot.Points.Select(point => new
            {
                point.PointId,
                point.Value,
                point.ValueType,
                point.Quality,
                point.Timestamp,
                point.ConnectionGeneration,
                point.Diagnostic
            })
        };
        Console.WriteLine(JsonSerializer.Serialize(output, JsonOptions));
    }
}

internal sealed record GatewayHostArguments(
    string? ConfigPath,
    int? Iterations,
    bool ShowHelp)
{
    public const string Usage = "用法: XXX.TestBench.Gateway.Host [--config <path>] [--once | --iterations <n>]\n" +
                                "默认使用 OfflineSimulation；切换 ConfiguredDevices 前必须完成现场 P0/G0。";

    public static GatewayHostArguments Parse(string[] args)
    {
        string? configPath = null;
        int? iterations = null;
        var showHelp = false;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--help":
                case "-h":
                    showHelp = true;
                    break;
                case "--config":
                    if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
                        throw new ArgumentException("--config 必须跟随设置文件路径。");
                    configPath = args[index];
                    break;
                case "--once":
                    if (iterations is not null)
                        throw new ArgumentException("--once 与 --iterations 不能同时使用。");
                    iterations = 1;
                    break;
                case "--iterations":
                    if (++index >= args.Length || !int.TryParse(args[index], out var parsedIterations) || parsedIterations < 1)
                        throw new ArgumentException("--iterations 必须为正整数。");
                    if (iterations is not null)
                        throw new ArgumentException("--once 与 --iterations 不能同时使用。");
                    iterations = parsedIterations;
                    break;
                default:
                    throw new ArgumentException($"不支持的参数: {args[index]}");
            }
        }

        return new GatewayHostArguments(configPath, iterations, showHelp);
    }
}

internal sealed class ConsoleGatewayLogSink : IGatewayLogSink
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly object _gate = new();

    public void Write(GatewayLogEntry entry)
    {
        var output = new
        {
            entry.Timestamp,
            entry.Level,
            entry.Component,
            entry.Message,
            entry.Properties,
            entry.Exception
        };
        lock (_gate)
            Console.WriteLine(JsonSerializer.Serialize(output, JsonOptions));
    }
}
