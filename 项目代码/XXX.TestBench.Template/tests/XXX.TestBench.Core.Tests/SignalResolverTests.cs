using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Execution;
using XXX.TestBench.Core.Ports;
using Xunit;

namespace XXX.TestBench.Core.Tests;

public sealed class SignalResolverTests
{
    [Fact]
    public async Task Preflight_ResolvesFixedBinding_AndReadsFreshSample()
    {
        var clock = new FixedClock();
        var point = CreatePoint();
        var runtime = new SignalRuntime(point, clock);
        var resolver = new SignalResolver(runtime,
            new Dictionary<string, string> { ["Pressure"] = point.PointId },
            () => clock.UtcNow);

        var result = await resolver.PreflightAsync(new[]
        {
            new RequiredSignal("Pressure", SignalAccessKind.Read, DevicePointDataType.Decimal, "MPa", TimeSpan.FromSeconds(2))
        });

        Assert.Equal(point.PointId, result["Pressure"].PointId);
        Assert.Equal(1, runtime.FreshReadCount);
    }

    [Fact]
    public async Task Preflight_RejectsStaleOrBadSample()
    {
        var clock = new FixedClock();
        var point = CreatePoint();
        var runtime = new SignalRuntime(point, clock) { Quality = PointQuality.Stale };
        var resolver = new SignalResolver(runtime,
            new Dictionary<string, string> { ["Pressure"] = point.PointId },
            () => clock.UtcNow);

        await Assert.ThrowsAsync<SignalDependencyException>(() => resolver.PreflightAsync(new[]
        {
            new RequiredSignal("Pressure", SignalAccessKind.Read, DevicePointDataType.Decimal, "MPa", TimeSpan.FromSeconds(2))
        }));
    }

    [Fact]
    public async Task Resolve_RejectsTypeAndUnitMismatch()
    {
        var point = CreatePoint();
        var runtime = new SignalRuntime(point, new FixedClock());
        var resolver = new SignalResolver(runtime,
            new Dictionary<string, string> { ["Pressure"] = point.PointId });

        var error = await Assert.ThrowsAsync<SignalDependencyException>(() => resolver.ResolveAsync(new[]
        {
            new RequiredSignal("Pressure", SignalAccessKind.Read, DevicePointDataType.Float32, "bar", TimeSpan.FromSeconds(2))
        }));

        Assert.Contains("类型不匹配", error.Message, StringComparison.Ordinal);
        Assert.Contains("单位不匹配", error.Message, StringComparison.Ordinal);
    }

    private static DevicePoint CreatePoint()
        => new(
            "AI_Pressure",
            DevicePointProtocol.Simulation,
            "sim.pressure",
            DevicePointDataType.Decimal,
            "MPa",
            false,
            WriteRiskLevel.Normal,
            null,
            null,
            null,
            null,
            Name: "压力",
            PointId: "30000000-0000-5000-8000-000000000001",
            DeviceId: "20000000-0000-5000-8000-000000000001",
            DriverKey: DriverKeyCatalog.Simulation,
            Revision: "r1");

    private sealed class SignalRuntime(DevicePoint point, FixedClock clock) : IDeviceRuntime
    {
        public PointQuality Quality { get; set; } = PointQuality.Good;
        public int FreshReadCount { get; private set; }
        public string Name => "signal-test";
        public DeviceMode Mode => DeviceMode.Simulation;
        public bool IsSimulation => true;
        public DeviceRuntimeInfo Status => new(Name, "simulation", "sim://signal-test", true,
            DeviceHealth.Healthy, true, null, point.DeviceId, "channel-1", "r1", 1, DeviceConnectionState.Online);
        public string ActiveRevision => "r1";
        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task<IReadOnlyList<DevicePoint>> ListPointsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DevicePoint>>(new[] { point });
        public Task<PointValue> ReadAsync(DevicePoint requested, CancellationToken ct = default)
            => Task.FromResult(CreateValue());
        public Task<PointValue> ReadFreshAsync(string pointId, CancellationToken ct = default)
        {
            FreshReadCount++;
            return Task.FromResult(CreateValue());
        }
        public Task<PointValue> WriteAsync(DevicePoint requested, object? value, CancellationToken ct = default)
            => Task.FromResult(CreateValue(value));
        public DevicePoint? GetPoint(string pointId)
            => string.Equals(pointId, point.PointId, StringComparison.OrdinalIgnoreCase) ? point : null;
        public DeviceRuntimeInfo GetDeviceStatus(string deviceId) => Status with { DeviceId = deviceId };

        private PointValue CreateValue(object? value = null)
            => new(point.Code, point.Address, Quality, value ?? 1.5m, clock.UtcNow, point.PointId, point.DeviceId, 1, "r1");
    }
}
