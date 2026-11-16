using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Execution;
using XXX.TestBench.Core.Domain.Records;
using XXX.TestBench.Core.Domain.Reports;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Devices;
using XXX.TestBench.Infrastructure.Logging;
using XXX.TestBench.Infrastructure.Persistence;
using XXX.TestBench.Infrastructure.Time;

namespace XXX.TestBench.Integration.Tests;

public sealed class TestRunCoordinatorTests
{
    [Theory]
    [InlineData(RunScope.Auto, 2)]
    [InlineData(RunScope.Single, 1)]
    public async Task Start_CompletesFrozenScope_AndAtomicallyQueuesReport(RunScope scope, int expectedCount)
    {
        await using var f = await SimulationRunFixture.CreateAsync(2);
        var request = await f.PrepareAsync(scope);
        var handle = await f.Coordinator.StartAsync(request, f.Token);
        await f.Coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(BenchPhase.Idle, f.Coordinator.Snapshot.RunState);
        var record = (await f.Records.GetRecordAsync(handle.RecordId))!;
        Assert.Equal(RecordState.Completed, record.State);
        Assert.Equal(EvaluationState.Pending, record.EvaluationState);
        Assert.Null(record.Conclusion);
        Assert.Equal("车01", record.ProductIdentity.VehicleNumber);
        var items = await f.Records.ListItemResultsAsync(record.Id);
        Assert.Equal(expectedCount, items.Count);
        Assert.Equal(expectedCount, f.Executor.Calls);
        Assert.All(items, item => { Assert.Equal(ItemResultState.Succeeded, item.State); Assert.Single(item.Measurements); });
        var report = Assert.Single(await f.Reports.ListByRecordAsync(record.Id));
        Assert.Equal(ReportStatus.Pending, report.Status);
        Assert.Equal(record.InputHash, report.InputHash);
        var input = ReportInputSnapshotSerializer.Deserialize(record.ReportInputJson!);
        Assert.Equal("000123", input.Metadata.ProductNumber);
        Assert.Equal(scope, input.Scope);
        Assert.Empty(await f.Evidence.ReadAllAsync());
    }

    [Fact]
    public async Task DuplicateStart_ReturnsSameHandleAndNeverRunsTwice()
    {
        await using var f = await SimulationRunFixture.CreateAsync();
        f.Executor.Block = true;
        var request = await f.PrepareAsync();
        var handles = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => f.Coordinator.StartAsync(request, f.Token)));
        Assert.Single(handles.Distinct());
        await f.Executor.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, f.Executor.Calls);
        await Assert.ThrowsAsync<DomainException>(() => f.Operations.EnterConfigurationAsync());
        await Assert.ThrowsAsync<DomainException>(() => f.Operations.EnterDiagnosticsAsync());
        f.Executor.Continue.TrySetResult();
        await f.Coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(handles[0], await f.Coordinator.StartAsync(request, f.Token));
        Assert.Single(await f.Records.ListRecordsAsync(null));
        Assert.Single(await f.Reports.ListByRecordAsync(handles[0].RecordId));
    }

    [Fact]
    public async Task Stop_DuringItem_PersistsAbortedAndProcessOnly_Once()
    {
        await using var f = await SimulationRunFixture.CreateAsync(2);
        f.Executor.Block = true;
        var request = await f.PrepareAsync();
        var handle = await f.Coordinator.StartAsync(request, f.Token);
        await f.Executor.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.WhenAll(f.Coordinator.StopAsync(), f.Coordinator.StopAsync()).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(RecordState.Aborted, (await f.Records.GetRecordAsync(handle.RecordId))!.State);
        Assert.Equal(ReportPurpose.ProcessOnly, Assert.Single(await f.Reports.ListByRecordAsync(handle.RecordId)).Purpose);
        Assert.Equal(1, f.Executor.Calls);
        Assert.Equal(BenchPhase.Idle, f.Coordinator.Snapshot.RunState);
        Assert.Empty(await f.Evidence.ReadAllAsync());
    }

    [Fact]
    public async Task Stop_AfterCompletion_DoesNotRewriteTerminal()
    {
        await using var f = await SimulationRunFixture.CreateAsync();
        var handle = await f.Coordinator.StartAsync(await f.PrepareAsync(), f.Token);
        await f.Coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        await f.Coordinator.StopAsync();
        Assert.Equal(RecordState.Completed, (await f.Records.GetRecordAsync(handle.RecordId))!.State);
        Assert.Single(await f.Reports.ListByRecordAsync(handle.RecordId));
    }

    [Fact]
    public async Task StopAndCompletion_Race_AlwaysHasOneConsistentTerminal()
    {
        await using var f = await SimulationRunFixture.CreateAsync();
        f.Executor.Block = true;
        var handle = await f.Coordinator.StartAsync(await f.PrepareAsync(), f.Token);
        await f.Executor.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.WhenAll(Task.Run(() => f.Executor.Continue.TrySetResult()), Task.Run(() => f.Coordinator.StopAsync()))
            .WaitAsync(TimeSpan.FromSeconds(10));
        await f.Coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        var record = (await f.Records.GetRecordAsync(handle.RecordId))!;
        Assert.Contains(record.State, new[] { RecordState.Completed, RecordState.Aborted });
        var report = Assert.Single(await f.Reports.ListByRecordAsync(handle.RecordId));
        Assert.Equal(record.State == RecordState.Completed ? ReportPurpose.ProductEvaluation : ReportPurpose.ProcessOnly, report.Purpose);
        Assert.Equal(BenchPhase.Idle, f.Coordinator.Snapshot.RunState);
        Assert.Empty(await f.Evidence.ReadAllAsync());
    }

    [Fact]
    public async Task ParameterChange_InvalidatesPreparation_AndCancelReleasesConfigurationGate()
    {
        await using var f = await SimulationRunFixture.CreateAsync();
        var request = await f.PrepareAsync();
        await Assert.ThrowsAsync<DomainException>(() => f.Operations.EnterConfigurationAsync());
        var parameter = (await f.Parameters.GetProjectAsync())!;
        parameter.TestTimeSeconds++;
        await f.Parameters.SaveProjectAsync(parameter);
        await Assert.ThrowsAsync<DomainException>(() => f.Coordinator.StartAsync(request, f.Token));
        Assert.Empty(await f.Records.ListRecordsAsync(null));
        await f.Coordinator.StopAsync();
        await using var lease = await f.Operations.EnterConfigurationAsync();
    }

    [Fact]
    public async Task ReadyReservation_BlocksManualWriteAndRuntimeReplacement()
    {
        await using var f = await SimulationRunFixture.CreateAsync();
        await f.PrepareAsync();
        using var logger = new FileLogger(System.IO.Path.Combine(f.Env.Root, "logs"));
        var audit = new SqliteAuditLog(f.Database);
        var role = new Role { Name = "测试管理员" };
        role.Permissions.UnionWith(Enum.GetValues<PermissionCode>());
        var actor = new UserContext { UserId = 1, LoginName = "admin", DisplayName = "测试管理员", Role = role };
        var output = (await f.Runtime.ListPointsAsync()).Single(point => point.IsWritable);
        var pipeline = new DeviceWritePipeline(audit, logger, f.Operations);
        await Assert.ThrowsAsync<DomainException>(() => pipeline.ExecuteAsync(new(actor, output, true,
            f.Runtime.Mode, f.Runtime, true, true, f.Runtime.ActiveRevision)));
        var modes = new DeviceModeController(new DeviceRuntimeFactory(await f.Configurations.LoadActiveAsync(),
            new SystemClock()), logger, audit, f.Operations);
        await Assert.ThrowsAsync<DomainException>(() => modes.InitializeAsync());
        Assert.Null(modes.Runtime);
        await f.Coordinator.StopAsync();
    }

    [Fact]
    public async Task StartCommitFailure_RollsBackAllSlots_AndDoesNotExecute()
    {
        await using var f = await SimulationRunFixture.CreateAsync(2);
        f.Transactions.FailBeforeCommitNumber = 1;
        await Assert.ThrowsAsync<DomainException>(() => Start(f));
        Assert.Empty(await f.Records.ListRecordsAsync(null));
        Assert.Equal(0, f.Executor.Calls);
        Assert.Single(await f.Evidence.ReadAllAsync());
        Assert.Equal(BenchPhase.RecoveryRequired, f.Coordinator.Snapshot.RunState);
    }

    [Fact]
    public async Task Reconnect_DuringRun_InvalidatesPermit_AndDoesNotExecuteNextItem()
    {
        await using var f = await SimulationRunFixture.CreateAsync(2);
        f.Executor.Block = true;
        var handle = await f.Coordinator.StartAsync(await f.PrepareAsync(), f.Token);
        await f.Executor.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await f.Runtime.StopAsync();
        await f.Runtime.StartAsync();
        f.Executor.Continue.TrySetResult();
        await f.Coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(RecordState.Failed, (await f.Records.GetRecordAsync(handle.RecordId))!.State);
        Assert.Equal(1, f.Executor.Calls);
        Assert.Equal(ReportPurpose.ProcessOnly, Assert.Single(await f.Reports.ListByRecordAsync(handle.RecordId)).Purpose);
    }

    [Fact]
    public async Task MissingRequiredMeasurement_IsExecutionFailure_NotProductFailure()
    {
        await using var f = await SimulationRunFixture.CreateAsync();
        f.Executor.OmitMeasurements = true;
        var handle = await f.Coordinator.StartAsync(await f.PrepareAsync(), f.Token);
        await f.Coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        var record = (await f.Records.GetRecordAsync(handle.RecordId))!;
        Assert.Equal(RecordState.Failed, record.State);
        Assert.Equal(EvaluationState.NotEvaluated, record.EvaluationState);
        Assert.Null(record.Conclusion);
    }

    [Fact]
    public async Task LostStartCommitReply_DoesNotExecute_AndRestartRetainsRecovery()
    {
        await using var f = await SimulationRunFixture.CreateAsync();
        f.Transactions.FailAfterCommitNumber = 1;
        await Assert.ThrowsAsync<DomainException>(() => Start(f));
        Assert.Equal(0, f.Executor.Calls);
        Assert.Single(await f.Records.ListRecordsAsync(null));
        Assert.Equal(BenchPhase.RecoveryRequired, f.Coordinator.Snapshot.RunState);
        Assert.Single(await f.Evidence.ReadAllAsync());
        var restarted = f.NewCoordinator();
        await Assert.ThrowsAsync<DomainException>(() => restarted.PrepareAsync(f.Token, f.ModelId,
            new("000124", null, null, null), RunScope.Auto));
        Assert.Equal(BenchPhase.RecoveryRequired, restarted.Snapshot.RunState);
        Assert.Equal(0, f.Executor.Calls);
    }

    [Fact]
    public async Task LostFinalCommitReply_IsVerifiedWithoutDuplicateReport()
    {
        await using var f = await SimulationRunFixture.CreateAsync();
        f.Transactions.FailAfterCommitNumber = 2;
        var handle = await Start(f);
        await f.Coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(BenchPhase.Idle, f.Coordinator.Snapshot.RunState);
        Assert.Single(await f.Reports.ListByRecordAsync(handle.RecordId));
        Assert.Empty(await f.Evidence.ReadAllAsync());
    }

    [Fact]
    public async Task FinalCommitFailure_KeepsRunningRecordAndRecoveryFence()
    {
        await using var f = await SimulationRunFixture.CreateAsync();
        f.Transactions.FailBeforeCommitNumber = 2;
        var handle = await Start(f);
        await f.Coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(RecordState.Running, (await f.Records.GetRecordAsync(handle.RecordId))!.State);
        Assert.Empty(await f.Reports.ListByRecordAsync(handle.RecordId));
        Assert.Equal(BenchPhase.RecoveryRequired, f.Coordinator.Snapshot.RunState);
        await Assert.ThrowsAsync<DomainException>(() => f.Operations.EnterConfigurationAsync());
    }

    private static async Task<Core.Execution.RunHandle> Start(SimulationRunFixture f)
        => await f.Coordinator.StartAsync(await f.PrepareAsync(), f.Token);
}
