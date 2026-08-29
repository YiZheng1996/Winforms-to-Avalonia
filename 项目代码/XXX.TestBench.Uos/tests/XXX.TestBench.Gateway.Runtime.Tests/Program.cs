using XXX.TestBench.Gateway.Domain;
using XXX.TestBench.Gateway.Infrastructure;

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static async Task AssertCanceled(Func<CancellationToken, Task> action)
{
    using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
    try
    {
        await action(cancellation.Token);
        throw new InvalidOperationException("预期操作被取消，但操作已完成。");
    }
    catch (OperationCanceledException)
    {
    }
}

var settings = new GatewaySettingsDocument();
var log = new RecordingLogSink();
var configuredDocument = new GatewaySettingsDocument();
configuredDocument.Gateway.TransportMode = "ConfiguredDevices";
await using (var constructed = ReadOnlyGatewayRuntimeFactory.Create(configuredDocument))
{
    Assert(!constructed.CurrentSnapshot.IsHealthy, "ConfiguredDevices 构造阶段不应伪造健康状态。");
    Assert(!constructed.WritesEnabled, "ConfiguredDevices 构造阶段暴露了写入能力。");
}

await using (var runtime = new ReadOnlyGatewayRuntime(
                  settings.Gateway,
                  new SimulatedS7ReadOnlyTransport(),
                 new SimulatedModbusReadOnlyTransport(),
                 isSimulated: true,
                 log))
{
    Assert(runtime.CurrentSnapshot.Points.All(x => x.Quality == DataQuality.Unknown), "首个采样前没有保持 Unknown。");
    var snapshot = await runtime.PollOnceAsync();
    Assert(snapshot.IsSimulated && snapshot.IsHealthy, "离线仿真首次采样未达到健康状态。");
    Assert((float?)snapshot.Find("SMART.PLC.AI.MAI00")?.Value == 42.5f, "S7 浮点解码错误。");
    Assert((bool?)snapshot.Find("SMART.PLC.DI.MDI00")?.Value == true, "S7 DI00 解码错误。");
    Assert((ushort?)snapshot.Find("Modbus.WSD.CH00")?.Value == 1234, "Modbus 寄存器解码错误。");
    Assert(!runtime.WritesEnabled, "Phase C Runtime 暴露了写入能力。");
    Assert(log.Entries.Any(x => x.Message == "Read-only poll completed"), "没有记录结构化轮询日志。");
}

var configuredOptions = GatewayOptions.CreateDefault();
configuredOptions.TransportMode = "ConfiguredDevices";

var readFailureS7 = new TestS7Transport { FailReads = true };
var readFailureModbus = new TestModbusTransport { FailReads = true };
await using (var runtime = new ReadOnlyGatewayRuntime(configuredOptions, readFailureS7, readFailureModbus, false))
{
    var failed = await runtime.PollOnceAsync();
    Assert(!failed.IsHealthy, "读异常时仍报告健康。");
    Assert(failed.Find("SMART.PLC.AI.MAI00")?.Quality == DataQuality.Bad, "S7 读异常没有降级 AI00。");
    Assert(failed.Find("Modbus.WSD.CH00")?.Quality == DataQuality.Bad, "Modbus 读异常没有降级 CH00。");
    Assert(!readFailureS7.IsConnected && !readFailureModbus.IsConnected, "读异常后没有断开故障传输。");

    readFailureS7.FailReads = false;
    readFailureModbus.FailReads = false;
    var recovered = await runtime.PollOnceAsync();
    Assert(recovered.IsHealthy, "读异常后的下一轮没有恢复健康。");
    Assert(recovered.Find("SMART.PLC.AI.MAI00")?.Quality == DataQuality.Good, "S7 重连后 AI00 没有恢复 Good。");
    Assert(recovered.Find("Modbus.WSD.CH00")?.Quality == DataQuality.Good, "Modbus 重连后 CH00 没有恢复 Good。");
    Assert(readFailureS7.ConnectionGeneration == 2 && readFailureModbus.ConnectionGeneration == 2, "读异常恢复没有建立新的连接代次。");
}

var failingS7 = new TestS7Transport { FailConnect = true };
var workingModbus = new TestModbusTransport();
var failureLog = new RecordingLogSink();
await using (var runtime = new ReadOnlyGatewayRuntime(configuredOptions, failingS7, workingModbus, false, failureLog))
{
    var snapshot = await runtime.PollOnceAsync();
    Assert(!snapshot.IsHealthy, "S7 连接失败时仍报告健康。");
    Assert(snapshot.Find("SMART.PLC.AI.MAI00")?.Quality == DataQuality.Bad, "S7 连接失败没有降级 AI00。");
    Assert(snapshot.Find("Modbus.WSD.CH00")?.Quality == DataQuality.Good, "部分成功结果没有保留。");
    Assert(failureLog.Entries.Any(x => x.Level == GatewayLogLevel.Error), "连接失败没有结构化错误日志。");
}

var cancellationS7 = new TestS7Transport { BlockConnect = true };
var cancellationModbus = new TestModbusTransport();
await using (var runtime = new ReadOnlyGatewayRuntime(configuredOptions, cancellationS7, cancellationModbus, false))
{
    await AssertCanceled(runtime.PollOnceAsync);
}
Assert(cancellationS7.DisposeCalled && cancellationModbus.DisposeCalled, "取消后两个传输均未完成清理。");

var cleanupS7 = new TestS7Transport { ThrowOnDisconnect = true };
var cleanupModbus = new TestModbusTransport();
var cleanupLog = new RecordingLogSink();
await using (var runtime = new ReadOnlyGatewayRuntime(configuredOptions, cleanupS7, cleanupModbus, false, cleanupLog))
{
    await runtime.PollOnceAsync();
}
Assert(cleanupS7.DisposeCalled && cleanupModbus.DisposeCalled, "首个传输清理失败时未继续清理另一个传输。");
Assert(cleanupLog.Entries.Any(x => x.Level == GatewayLogLevel.Warning), "清理失败没有记录警告日志。");

var readStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
var allowRead = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
var blockingS7 = new TestS7Transport { ReadStarted = readStarted, AllowRead = allowRead };
var blockingModbus = new TestModbusTransport();
await using (var runtime = new ReadOnlyGatewayRuntime(configuredOptions, blockingS7, blockingModbus, false))
{
    var poll = runtime.PollOnceAsync();
    await readStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
    var dispose = runtime.DisposeAsync().AsTask();
    await Task.Delay(50);
    Assert(!dispose.IsCompleted, "轮询未结束时 Runtime 提前释放了传输。");
    allowRead.TrySetResult(true);
    await poll;
    await dispose;
}

var artifactRoot = Environment.GetEnvironmentVariable("XXX_TESTBENCH_TEST_ARTIFACTS")
    ?? Path.Combine("D:\\Codex相关", "migration-build", "runtime-test-artifacts");
var artifactDirectory = Path.Combine(artifactRoot, Guid.NewGuid().ToString("N"));
var settingsPath = Path.Combine(artifactDirectory, "gatewaysettings.json");
try
{
    GatewaySettingsFile.Save(settingsPath, settings);
    var loaded = GatewaySettingsFile.Load(settingsPath);
    Assert(loaded.Gateway.TransportMode == "OfflineSimulation", "设置文件往返后 TransportMode 改变。");

    settings.Gateway.PollIntervalMs = 500;
    GatewaySettingsFile.Save(settingsPath, settings);
    Assert(Directory.GetFiles(artifactDirectory, "gatewaysettings.json.previous.*").Length == 1, "设置保存没有保留上一版文件。");
}
finally
{
    if (Directory.Exists(artifactDirectory))
        Directory.Delete(artifactDirectory, recursive: true);
}

Console.WriteLine("PASS gateway-runtime simulation=failure=cancellation=cleanup settings=atomic-predecessor");

sealed class RecordingLogSink : IGatewayLogSink
{
    public List<GatewayLogEntry> Entries { get; } = [];

    public void Write(GatewayLogEntry entry) => Entries.Add(entry);
}

sealed class TestS7Transport : IReadOnlyS7Transport
{
    public bool FailConnect { get; init; }
    public bool BlockConnect { get; init; }
    public bool FailReads { get; set; }
    public bool ThrowOnDisconnect { get; init; }
    public TaskCompletionSource<bool>? ReadStarted { get; init; }
    public TaskCompletionSource<bool>? AllowRead { get; init; }
    public bool IsConnected { get; private set; }
    public bool DisposeCalled { get; private set; }
    public string EndpointId => "Test-S7";
    public long ConnectionGeneration { get; private set; }
    public int ConnectCount { get; private set; }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (BlockConnect)
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (FailConnect)
            throw new IOException("test S7 connect failure");
        IsConnected = true;
        ConnectionGeneration++;
        ConnectCount++;
    }

    public async Task<S7RawReadResult> ReadMemoryAsync(string address, int byteCount, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ReadStarted is not null)
        {
            ReadStarted.TrySetResult(true);
            await (AllowRead ?? throw new InvalidOperationException("测试读操作缺少释放信号。"))
                .Task.WaitAsync(cancellationToken);
        }

        if (FailReads)
            return new S7RawReadResult(address, [], DataQuality.Bad, DateTimeOffset.UtcNow, ConnectionGeneration, "test S7 read failure");

        var data = address.Equals("VD200", StringComparison.OrdinalIgnoreCase)
            ? new byte[] { 0x42, 0x2A, 0x00, 0x00 }
            : new byte[] { 1 };
        return new S7RawReadResult(address, data[..byteCount], DataQuality.Good, DateTimeOffset.UtcNow, ConnectionGeneration);
    }

    public Task DisconnectAsync()
    {
        if (ThrowOnDisconnect)
            throw new IOException("test S7 disconnect failure");
        IsConnected = false;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        DisposeCalled = true;
        IsConnected = false;
        return ValueTask.CompletedTask;
    }
}

sealed class TestModbusTransport : IReadOnlyModbusTransport
{
    public bool FailReads { get; set; }
    public bool IsConnected { get; private set; }
    public bool DisposeCalled { get; private set; }
    public string TransportId => "Test-Modbus";
    public long ConnectionGeneration { get; private set; }
    public int ConnectCount { get; private set; }

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IsConnected = true;
        ConnectionGeneration++;
        ConnectCount++;
        return Task.CompletedTask;
    }

    public Task<ModbusRegisterReadResult> ReadRegistersAsync(ModbusRegisterReadRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (FailReads)
            return Task.FromResult(new ModbusRegisterReadResult(request, [], DataQuality.Bad, DateTimeOffset.UtcNow, ConnectionGeneration, "test Modbus read failure"));

        return Task.FromResult(new ModbusRegisterReadResult(
            request,
            [1234],
            DataQuality.Good,
            DateTimeOffset.UtcNow,
            ConnectionGeneration));
    }

    public Task DisconnectAsync()
    {
        IsConnected = false;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        DisposeCalled = true;
        IsConnected = false;
        return ValueTask.CompletedTask;
    }
}
