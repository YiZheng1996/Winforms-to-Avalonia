using System.Collections.Concurrent;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Core.Tests;

public sealed class DevicePointDiagnosticReaderTests
{
    [Fact]
    public async Task ResultsKeepActualQualityAndDiscardMismatchedIdentity()
    {
        await using var operations = new DeviceOperationCoordinator();
        var runtime = new ControlledRuntime();
        runtime.Responses["p-good"] = Value("p-good", PointQuality.Good, 1);
        runtime.Responses["p-stale"] = Value("p-stale", PointQuality.Stale, 2);
        runtime.Responses["p-bad"] = Value("p-bad", PointQuality.Bad, 3);
        runtime.Responses["p-wrong"] = Value("other", PointQuality.Good, 4);
        runtime.ReadHandler = (pointId, _) =>
            pointId == "p-fail"
                ? Task.FromException<PointValue>(new InvalidOperationException("设备读取失败"))
                : Task.FromResult(runtime.Responses[pointId]);
        var reader = CreateReader(operations, runtime);

        var result = await reader.ReadAsync(
        [
            Target("p-good", "C1"),
            Target("p-stale", "C1"),
            Target("p-bad", "C1"),
            Target("p-wrong", "C1"),
            Target("p-fail", "C1")
        ]);

        Assert.Equal(1, result.GoodCount);
        Assert.Equal(1, result.StaleCount);
        Assert.Equal(1, result.BadCount);
        Assert.Equal(1, result.DiscardedCount);
        Assert.Equal(1, result.FailedCount);
        Assert.False(result.Succeeded);
        Assert.Equal(DevicePointDiagnosticResultKind.Discarded, result.Items[3].Kind);
        Assert.Equal(DevicePointDiagnosticResultKind.Failed, result.Items[4].Kind);
    }

    [Fact]
    public async Task RevisionAndConnectionGenerationChangesDiscardReadResult()
    {
        await using var operations = new DeviceOperationCoordinator();
        var runtime = new ControlledRuntime();
        runtime.ReadHandler = async (pointId, _) =>
        {
            await Task.Yield();
            runtime.ActiveRevision = "r2";
            runtime.ConnectionGeneration = 2;
            return Value(pointId, PointQuality.Good, 5, revision: "r1", generation: 1);
        };
        var reader = CreateReader(operations, runtime);

        var result = await reader.ReadAsync([Target("p-1", "C1")]);

        Assert.Equal(DevicePointDiagnosticResultKind.Discarded, result.Items[0].Kind);
        Assert.Contains("变化", result.Items[0].Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SameChannelIsSerialAndDifferentChannelsAreLimitedToFour()
    {
        await using var operations = new DeviceOperationCoordinator();
        var runtime = new ControlledRuntime();
        var inFlightByChannel = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var maxByChannel = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var globalInFlight = 0;
        var maxGlobal = 0;
        runtime.ReadHandler = async (pointId, ct) =>
        {
            var channel = runtime.Channels[pointId];
            var channelInFlight = inFlightByChannel.AddOrUpdate(channel, 1, (_, count) => count + 1);
            maxByChannel.AddOrUpdate(channel, channelInFlight, (_, current) => Math.Max(current, channelInFlight));
            var currentGlobal = Interlocked.Increment(ref globalInFlight);
            InterlockedMax(ref maxGlobal, currentGlobal);
            try
            {
                await Task.Delay(25, ct);
                return Value(pointId, PointQuality.Good, 1);
            }
            finally
            {
                inFlightByChannel.AddOrUpdate(channel, 0, (_, count) => count - 1);
                Interlocked.Decrement(ref globalInFlight);
            }
        };
        var reader = CreateReader(operations, runtime);
        var targets = Enumerable.Range(0, 8)
            .Select(index => Target($"p-{index}", index < 2 ? "C1" : $"C{index}"))
            .ToList();
        foreach (var target in targets)
            runtime.Channels[target.PointId] = target.ChannelId;

        var result = await reader.ReadAsync(targets);

        Assert.Equal(8, result.GoodCount);
        Assert.All(maxByChannel.Values, value => Assert.Equal(1, value));
        Assert.InRange(maxGlobal, 1, 4);
    }

    [Fact]
    public async Task CancellationReturnsCompletedItemsAndMarksRemainingTargets()
    {
        await using var operations = new DeviceOperationCoordinator();
        var runtime = new ControlledRuntime();
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.ReadHandler = async (pointId, ct) =>
        {
            started.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return Value(pointId, PointQuality.Good, 1);
        };
        var reader = CreateReader(operations, runtime);
        using var cancellation = new CancellationTokenSource();
        var pending = reader.ReadAsync(
            Enumerable.Range(0, 6)
                .Select(index => Target($"p-{index}", $"C{index}"))
                .ToList(),
            cancellation.Token);

        await started.Task;
        cancellation.Cancel();
        var result = await pending;

        Assert.Equal(6, result.Items.Count);
        Assert.Equal(6, result.CancelledCount);
        Assert.Equal(0, result.GoodCount);
        Assert.True(runtime.Reads.Count <= 4);
    }

    [Fact]
    public async Task MissingRuntimePointIsDiscardedWithoutStartingDeviceRead()
    {
        await using var operations = new DeviceOperationCoordinator();
        var runtime = new ControlledRuntime();
        runtime.MissingPoints.Add("p-missing");
        runtime.ReadHandler = (_, _) =>
            throw new InvalidOperationException("缺失点位不应进入读取器");
        var reader = CreateReader(operations, runtime);

        var result = await reader.ReadAsync([Target("p-missing", "C1")]);

        Assert.Equal(DevicePointDiagnosticResultKind.Discarded, result.Items[0].Kind);
        Assert.Contains("未执行读取", result.Items[0].Error, StringComparison.Ordinal);
        Assert.Empty(runtime.Reads);
    }

    private static DevicePointDiagnosticReader CreateReader(
        DeviceOperationCoordinator operations,
        ControlledRuntime runtime)
        => new(
            new DeviceManualOperationGuard(operations, new FakeRecordRepository(), "diagnostic-test-bench"),
            () => runtime,
            maxConcurrentChannels: 4);

    private static DevicePointDiagnosticTarget Target(string pointId, string channelId)
        => new(pointId, "D1", pointId, $"DB1.DBD{pointId}", "Float32", channelId);

    private static PointValue Value(
        string pointId,
        PointQuality quality,
        object value,
        string revision = "r1",
        long generation = 1)
        => new(
            pointId,
            "DB1.DBD0",
            quality,
            value,
            DateTime.UtcNow,
            pointId,
            "D1",
            generation,
            revision);

    private static void InterlockedMax(ref int location, int value)
    {
        while (true)
        {
            var current = Volatile.Read(ref location);
            if (current >= value || Interlocked.CompareExchange(ref location, value, current) == current)
                return;
        }
    }

    private sealed class ControlledRuntime : IDeviceRuntime
    {
        private readonly DeviceRuntimeInfo _baseStatus = new(
            "测试运行时",
            "Fake",
            "fake://runtime",
            true,
            DeviceHealth.Healthy,
            true,
            null,
            DeviceId: "D1",
            ChannelId: "C1",
            Revision: "r1",
            ConnectionGeneration: 1,
            ConnectionState: DeviceConnectionState.Online,
            Mode: DeviceMode.Simulation);

        public ConcurrentDictionary<string, PointValue> Responses { get; } = new(StringComparer.OrdinalIgnoreCase);
        public ConcurrentDictionary<string, string> Channels { get; } = new(StringComparer.OrdinalIgnoreCase);
        public ConcurrentQueue<string> Reads { get; } = new();
        public HashSet<string> MissingPoints { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Func<string, CancellationToken, Task<PointValue>>? ReadHandler { get; set; }
        public string ActiveRevision { get; set; } = "r1";
        public SignalBindingsConfig SignalBindings => new();
        public RuntimeActivationReport ActivationReport => new(1, 1, Array.Empty<DeviceActivationIssue>());
        public long ConnectionGeneration { get; set; } = 1;
        public string Name => "测试运行时";
        public DeviceMode Mode => DeviceMode.Simulation;
        public bool IsSimulation => true;
        public DeviceRuntimeInfo Status => _baseStatus with
        {
            Revision = ActiveRevision,
            ConnectionGeneration = ConnectionGeneration
        };

        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task<IReadOnlyList<DevicePoint>> ListPointsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DevicePoint>>(Channels.Keys.Select(pointId => new DevicePoint(
                pointId,
                DevicePointProtocol.SiemensS7,
                "DB1.DBD0",
                DevicePointDataType.Float32,
                false,
                WriteRiskLevel.Normal,
                null,
                null,
                null,
                null,
                PointId: pointId,
                DeviceId: "D1",
                Revision: ActiveRevision)).ToList());

        public Task<PointValue> ReadAsync(DevicePoint point, CancellationToken ct = default)
            => Task.FromResult(Value(point.PointId, PointQuality.Good, 1));

        public Task<PointValue> WriteAsync(DevicePoint point, object? value, CancellationToken ct = default)
            => Task.FromResult(Value(point.PointId, PointQuality.Good, value ?? 0));

        public async Task<PointValue> ReadFreshAsync(string pointId, CancellationToken ct = default)
        {
            Reads.Enqueue(pointId);
            if (ReadHandler is not null)
                return await ReadHandler(pointId, ct);
            return Responses[pointId];
        }

        public DevicePoint? GetPoint(string pointId)
            => MissingPoints.Contains(pointId)
                ? null
                : new DevicePoint(
                    pointId,
                    DevicePointProtocol.SiemensS7,
                    "DB1.DBD0",
                    DevicePointDataType.Float32,
                    false,
                    WriteRiskLevel.Normal,
                    null,
                    null,
                    null,
                    null,
                    pointId,
                    "D1",
                    ActiveRevision,
                    pointId,
                    string.Empty);

        public DeviceRuntimeInfo GetDeviceStatus(string deviceId)
        {
            if (!string.Equals(deviceId, _baseStatus.DeviceId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"设备 {deviceId} 不属于测试运行时");
            return _baseStatus with
            {
                Revision = ActiveRevision,
                ConnectionGeneration = ConnectionGeneration
            };
        }

        public bool TryGetCachedValue(string pointId, out PointValue value)
        {
            value = default!;
            return false;
        }

        public IReadOnlyList<PointValue> ListCachedValues() => Array.Empty<PointValue>();
        public IReadOnlyList<DeviceRuntimeInfo> ListDeviceStatuses() => new[] { Status };
    }
}
