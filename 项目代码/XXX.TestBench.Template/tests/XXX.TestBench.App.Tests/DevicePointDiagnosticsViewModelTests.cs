using XXX.TestBench.App.ViewModels;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.App.Tests;

public sealed class DevicePointDiagnosticsViewModelTests
{
    [Fact]
    public async Task ReadSelected_UsesCurrentRuntimeAndMapsResult()
    {
        using var harness = AppTestHarness.Create(grouped: true);
        var diagnostics = await CreateForRuntimeAsync(harness, take: 1);
        diagnostics.SelectedPoint = diagnostics.Points[0];

        var feedback = await diagnostics.ReadSelectedAsync();

        Assert.True(feedback.Succeeded, feedback.Message);
        var row = diagnostics.Points[0];
        Assert.Equal("成功", row.StatusText);
        Assert.NotEqual("未读取", row.ValueText);
        Assert.DoesNotContain("未知", row.QualityText, StringComparison.Ordinal);
        Assert.NotEqual("—", row.TimestampText);
    }

    [Fact]
    public async Task ReadScope_CompletesEveryTarget()
    {
        using var harness = AppTestHarness.Create(grouped: true);
        var diagnostics = await CreateForRuntimeAsync(harness, take: 3);
        var expected = diagnostics.Points.Count;
        Assert.True(expected > 0);

        var feedback = await diagnostics.ReadScopeAsync();

        Assert.True(feedback.Succeeded, feedback.Message);
        Assert.Equal(expected, diagnostics.CompletedCount);
        Assert.Equal(expected, diagnostics.TotalCount);
        Assert.All(diagnostics.Points, row => Assert.Contains(row.StatusText, new[] { "成功", "读取失败" }));
    }

    [Fact]
    public async Task SimulationStatus_IsExplicit()
    {
        using var harness = AppTestHarness.Create(grouped: true);
        var diagnostics = await CreateForRuntimeAsync(harness, take: 1);

        await diagnostics.LoadAsync();

        Assert.Contains("仿真模式", diagnostics.RuntimeSummary, StringComparison.Ordinal);
        Assert.Contains("不代表现场硬件", diagnostics.FeedbackText, StringComparison.Ordinal);
    }


    [Fact]
    public async Task RevisionChange_DiscardsLateResult()
    {
        using var harness = AppTestHarness.Create(grouped: true);
        var runtime = new ControlledRuntime
        {
            Revision = "runtime-current",
            ReturnedRevision = "runtime-old"
        };
        var controller = new XXX.TestBench.Core.Application.DeviceModeController(
            new ControlledRuntimeFactory(runtime),
            new NullLogger(),
            harness.Services.AuditLog);
        var initialized = await controller.InitializeAsync();
        Assert.True(initialized.Ok, initialized.Error);
        var services = harness.Services with { DeviceModes = controller };
        var diagnostics = new DevicePointDiagnosticsViewModel(
            services,
            "测试",
            new[]
            {
                new DevicePointDiagnosticTarget(
                    "point-1",
                    "device-1",
                    "测试点",
                    "sim.test",
                    "小数",
                    "channel-1")
            });
        diagnostics.SelectedPoint = diagnostics.Points[0];

        var feedback = await diagnostics.ReadSelectedAsync();

        Assert.False(feedback.Succeeded);
        Assert.Contains("丢弃", feedback.Message, StringComparison.Ordinal);
        Assert.Equal("已丢弃", diagnostics.Points[0].StatusText);
        Assert.Contains("旧 Revision", diagnostics.Points[0].ErrorText, StringComparison.Ordinal);
    }

    private sealed class ControlledRuntimeFactory(ControlledRuntime runtime)
        : XXX.TestBench.Core.Ports.IDeviceRuntimeFactory
    {
        public Task<XXX.TestBench.Core.Ports.IDeviceRuntime> CreateAsync(
            DeviceMode mode,
            CancellationToken ct = default)
            => Task.FromResult<XXX.TestBench.Core.Ports.IDeviceRuntime>(runtime);
    }

    private sealed class ControlledRuntime : XXX.TestBench.Core.Ports.IDeviceRuntime
    {
        public string Revision { get; init; } = "runtime-current";
        public string ReturnedRevision { get; init; } = "runtime-current";
        public string Name => "测试运行时";
        public DeviceMode Mode => DeviceMode.Simulation;
        public bool IsSimulation => true;
        public DeviceRuntimeInfo Status => new(
            Name, "Simulation", "sim", true, DeviceHealth.Healthy, true,
            null, "device-1", "channel-1", Revision, 1,
            DeviceConnectionState.Online, DeviceMode.Simulation);
        public string ActiveRevision => Revision;

        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<PointValue> ReadAsync(DevicePoint point, CancellationToken ct = default)
            => ReadFreshAsync(point.PointId, ct);
        public Task<PointValue> WriteAsync(DevicePoint point, object? value, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<DevicePoint>> ListPointsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DevicePoint>>(
                new[] { new DevicePoint("TEST", DevicePointProtocol.Simulation, "sim.test",
                    DevicePointDataType.Decimal, false, WriteRiskLevel.Normal,
                    null, null, null, null, "测试点", string.Empty, "point-1", "device-1") });
        public Task<PointValue> ReadFreshAsync(string pointId, CancellationToken ct = default)
            => Task.FromResult(new PointValue(
                "TEST", "sim.test", PointQuality.Good, 1.2m, DateTime.UtcNow,
                pointId, "device-1", 1, ReturnedRevision));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NullLogger : XXX.TestBench.Core.Ports.IAppLogger
    {
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, Exception? exception = null) { }
    }

    private static async Task<DevicePointDiagnosticsViewModel> CreateForRuntimeAsync(
        AppTestHarness harness,
        int take)
    {
        var runtime = harness.Services.DeviceModes.Runtime
            ?? throw new InvalidOperationException("设备运行时未初始化");
        var points = (await runtime.ListPointsAsync()).Take(take).ToList();
        var targets = points.Select(point =>
        {
            var status = runtime.GetDeviceStatus(point.DeviceId);
            return new DevicePointDiagnosticTarget(
                point.PointId,
                point.DeviceId,
                string.IsNullOrWhiteSpace(point.Name) ? point.Code : point.Name,
                point.Address,
                point.DataType.ToString(),
                status.ChannelId);
        }).ToList();
        var viewModel = new DevicePointDiagnosticsViewModel(harness.Services, "测试范围", targets);
        await viewModel.LoadAsync();
        return viewModel;
    }
}
