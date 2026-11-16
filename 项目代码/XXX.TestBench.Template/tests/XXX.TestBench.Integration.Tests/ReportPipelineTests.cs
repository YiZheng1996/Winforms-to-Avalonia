using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Records;
using XXX.TestBench.Core.Domain.Reports;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Infrastructure.Logging;
using XXX.TestBench.Infrastructure.Persistence.Repositories;

namespace XXX.TestBench.Integration.Tests;

public sealed class ReportPipelineTests
{
    [Fact]
    public async Task Dispatcher_StartsPendingQueue_AndWaitsForCancellationBeforeDisposal()
    {
        await using var f = await ReportPipelineFixture.CreateAsync();
        f.Engine.Block = true;
        var dispatcher = new ReportTaskDispatcher(f.Worker, f.Run.Records, f.Storage, "test-node",
            f.Run.Options.DatabaseInstanceId, new FileLogger(Path.Combine(f.Run.Env.Root, "logs")), TimeSpan.FromMilliseconds(50));
        try { await f.Engine.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
        finally { await dispatcher.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)); }
        Assert.Equal(ReportStatus.Generating, (await f.Run.Reports.GetAsync(f.Report.Id))!.Status);
        Assert.Empty(await f.RecoverAsync());
        Assert.Equal(1, f.Engine.Calls);
    }

    [Fact]
    public async Task OtherNodeCannotClaim_AndDisabledUserCannotRetry()
    {
        await using var f = await ReportPipelineFixture.CreateAsync();
        Assert.Empty(await f.Worker.ProcessPendingAsync("other-node"));
        Assert.Equal(0, f.Engine.Calls);
        f.Engine.Fail = true;
        await f.Worker.ProcessPendingAsync("test-node");
        var users = new UserRepository(f.Run.Database);
        var user = (await users.GetByLoginNameAsync("admin"))!;
        user.IsEnabled = false;
        await users.UpdateAsync(user);
        await Assert.ThrowsAsync<AuthorizationException>(() => f.Retries.RetryAsync(f.Report.Id, f.Run.Token));
        Assert.Single(await f.Run.Reports.ListByRecordAsync(f.Report.TestRecordId));
    }

    [Fact]
    public async Task ConcurrentDispatch_CalculatesOnce_AndPublishesImmutableReceipt()
    {
        await using var f = await ReportPipelineFixture.CreateAsync();
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => f.Worker.ProcessPendingAsync("test-node")));
        Assert.Equal(1, f.Engine.Calls);
        var report = (await f.Run.Reports.GetAsync(f.Report.Id))!;
        Assert.Equal(ReportStatus.Completed, report.Status);
        var record = (await f.Run.Records.GetRecordAsync(report.TestRecordId))!;
        Assert.Equal(EvaluationState.Qualified, record.EvaluationState);
        Assert.Equal(report.RevisionId, record.CurrentReportRevisionId);
        var manifest = await f.Storage.ReadPublishedAsync(Guid.Parse(report.RevisionId!));
        Assert.Contains(manifest!.Artifacts, a => a.LogicalName == "completion.json");
        Assert.Empty(await f.RecoverAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublicationThenCommitFault_RecoversWithoutRecalculation(bool lostAcknowledgement)
    {
        await using var f = await ReportPipelineFixture.CreateAsync();
        if (lostAcknowledgement) f.Transactions.FailAfterCommitNumber = 1;
        else f.Transactions.FailBeforeCommitNumber = 1;
        await f.Worker.ProcessPendingAsync("test-node");
        Assert.Equal(lostAcknowledgement ? ReportStatus.Completed : ReportStatus.Generating,
            (await f.Run.Reports.GetAsync(f.Report.Id))!.Status);
        await f.RecoverAsync();
        Assert.Equal(ReportStatus.Completed, (await f.Run.Reports.GetAsync(f.Report.Id))!.Status);
        Assert.Equal(1, f.Engine.Calls);
        Assert.Empty(await f.RecoverAsync());
    }

    [Fact]
    public async Task FailedRetry_PreservesOriginal_AndIsIdempotent()
    {
        await using var f = await ReportPipelineFixture.CreateAsync();
        f.Engine.Fail = true;
        await f.Worker.ProcessPendingAsync("test-node");
        Assert.Equal(EvaluationState.Error, (await f.Run.Records.GetRecordAsync(f.Report.TestRecordId))!.EvaluationState);
        var retry = await f.Retries.RetryAsync(f.Report.Id, f.Run.Token);
        Assert.Equal(retry.Id, (await f.Retries.RetryAsync(f.Report.Id, f.Run.Token)).Id);
        Assert.Equal(2, retry.RevisionNo);
        Assert.Equal(f.Report.InputHash, retry.InputHash);
        Assert.Equal(EvaluationState.Pending, (await f.Run.Records.GetRecordAsync(f.Report.TestRecordId))!.EvaluationState);
        f.Engine.Fail = false;
        await f.Worker.ProcessPendingAsync("test-node");
        Assert.Equal(ReportStatus.Completed, (await f.Run.Reports.GetAsync(retry.Id))!.Status);
        Assert.Equal(ReportStatus.Failed, (await f.Run.Reports.GetAsync(f.Report.Id))!.Status);
        Assert.Equal(2, (await f.Run.Reports.ListByRecordAsync(f.Report.TestRecordId)).Count);
        Assert.Equal(2, f.Engine.Calls);
    }

    [Fact]
    public async Task InvalidSession_AndGeneratingTask_CannotRetry()
    {
        await using var f = await ReportPipelineFixture.CreateAsync();
        await Assert.ThrowsAsync<AuthorizationException>(() => f.Retries.RetryAsync(f.Report.Id, "invalid"));
        await f.Run.Reports.TryClaimAsync(f.Report.RevisionId!, "test-node", ReportStatus.Pending, Guid.NewGuid().ToString("D"));
        await Assert.ThrowsAsync<DomainException>(() => f.Retries.RetryAsync(f.Report.Id, f.Run.Token));
        Assert.Empty(await f.RecoverAsync());
        Assert.Equal(ReportStatus.Generating, (await f.Run.Reports.GetAsync(f.Report.Id))!.Status);
        Assert.Equal(0, f.Engine.Calls);
    }

    [Fact]
    public async Task CorruptArchive_AndWrongDatabase_DoNotCompleteOrRecalculate()
    {
        await using var f = await ReportPipelineFixture.CreateAsync();
        f.Transactions.FailBeforeCommitNumber = 1;
        await f.Worker.ProcessPendingAsync("test-node");
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Worker.RecoverPublishedAsync("test-node",
            Guid.NewGuid().ToString("D"), f.Run.Records, f.Storage));
        var file = Directory.GetFiles(Path.Combine(f.Run.Env.Root, "archive"), "calculated.xlsx", SearchOption.AllDirectories).Single();
        await File.AppendAllTextAsync(file, "corrupt");
        await Assert.ThrowsAsync<InvalidDataException>(() => f.RecoverAsync());
        Assert.Equal(ReportStatus.Generating, (await f.Run.Reports.GetAsync(f.Report.Id))!.Status);
        Assert.Equal(1, f.Engine.Calls);
    }
}
