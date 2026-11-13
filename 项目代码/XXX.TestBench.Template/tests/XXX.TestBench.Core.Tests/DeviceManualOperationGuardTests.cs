using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Products;
using XXX.TestBench.Core.Domain.Records;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Core.Tests;

public sealed class DeviceManualOperationGuardTests
{
    private const string BenchId = "bench-01";

    [Fact]
    public async Task ActiveRunningRecordIsRejectedBeforeDeviceAction()
    {
        await using var operations = new DeviceOperationCoordinator();
        var records = new FakeRecordRepository();
        records.Records.Add(CreateRunningRecord());
        var guard = new DeviceManualOperationGuard(operations, records, BenchId);
        var actionCount = 0;

        var error = await Assert.ThrowsAsync<DomainException>(() => guard.ExecuteAsync(_ =>
        {
            actionCount++;
            return Task.FromResult(true);
        }));

        Assert.Contains("试验运行中", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, actionCount);
    }

    [Fact]
    public async Task ActivityAppearingWhileWaitingForCoordinatorIsRejectedBySecondCheck()
    {
        await using var operations = new DeviceOperationCoordinator();
        var records = new FakeRecordRepository();
        var guard = new DeviceManualOperationGuard(operations, records, BenchId);
        var actionCount = 0;
        var executionLease = await operations.EnterExecutionAsync();

        try
        {
            var pending = guard.ExecuteAsync(_ =>
            {
                actionCount++;
                return Task.FromResult(true);
            });

            // 首次查询已完成，任务此时只能等待共享协调器；活动记录在此窗口内出现。
            await Task.Yield();
            records.Records.Add(CreateRunningRecord());
            await executionLease.DisposeAsync();

            var error = await Assert.ThrowsAsync<DomainException>(() => pending);
            Assert.Contains("试验运行中", error.Message, StringComparison.Ordinal);
            Assert.Equal(0, actionCount);
        }
        finally
        {
            await executionLease.DisposeAsync();
        }
    }

    [Fact]
    public async Task CancellationBeforeCoordinatorDoesNotInvokeAction()
    {
        await using var operations = new DeviceOperationCoordinator();
        var guard = new DeviceManualOperationGuard(operations, new FakeRecordRepository(), BenchId);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var actionCount = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => guard.ExecuteAsync(_ =>
        {
            actionCount++;
            return Task.FromResult(true);
        }, cancellation.Token));

        Assert.Equal(0, actionCount);
    }

    [Fact]
    public async Task RecordQueryFailureIsNotTreatedAsNoActiveRun()
    {
        await using var operations = new DeviceOperationCoordinator();
        var guard = new DeviceManualOperationGuard(operations, new ThrowingRecordRepository(), BenchId);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => guard.ExecuteAsync(_ =>
            Task.FromResult(true)));

        Assert.Equal("记录查询失败", error.Message);
    }

    private static TestRecord CreateRunningRecord()
        => new()
        {
            RecordNumber = "RUN-001",
            BenchId = BenchId,
            ProductModelId = 1,
            ProductIdentity = new ProductIdentity("SN-001", null, null, null),
            DeviceMode = DeviceMode.Simulation
        };

    private sealed class ThrowingRecordRepository : IRecordRepository
    {
        private static Exception Error() => new InvalidOperationException("记录查询失败");
        private static Task Fail() => Task.FromException(Error());
        private static Task<T> Fail<T>() => Task.FromException<T>(Error());

        public Task<bool> ExistsRecordNumberAsync(string recordNumber, CancellationToken ct = default)
            => Fail<bool>();
        public Task AddRecordAsync(TestRecord record, CancellationToken ct = default) => Fail();
        public Task<TestRecord?> GetRecordAsync(int recordId, CancellationToken ct = default)
            => Fail<TestRecord?>();
        public Task<TestRecord?> GetActiveRunningRecordAsync(string benchId, CancellationToken ct = default)
            => Fail<TestRecord?>();
        public Task<IReadOnlyList<TestRecord>> ListRecordsAsync(int? state, CancellationToken ct = default)
            => Fail<IReadOnlyList<TestRecord>>();
        public Task UpdateRecordAsync(TestRecord record, CancellationToken ct = default) => Fail();
        public Task<IReadOnlyList<TestItemResult>> ListItemResultsAsync(int recordId, CancellationToken ct = default)
            => Fail<IReadOnlyList<TestItemResult>>();
        public Task AddItemResultAsync(TestItemResult result, CancellationToken ct = default) => Fail();
        public Task UpdateItemResultAsync(TestItemResult result, CancellationToken ct = default) => Fail();
    }
}
