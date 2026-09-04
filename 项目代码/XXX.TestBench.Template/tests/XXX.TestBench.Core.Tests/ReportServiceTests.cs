using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Domain.Products;
using XXX.TestBench.Core.Domain.Records;
using XXX.TestBench.Core.Domain.Reports;
using XXX.TestBench.Core.Domain.TestPoints;
using Xunit;

namespace XXX.TestBench.Core.Tests;

public class ReportServiceTests
{
    private static (ReportService Service, FakeReportRepository Reports, FakeReportGenerator Generator) Create(bool recordCompleted = true)
    {
        var clock = new FixedClock();
        var records = new FakeRecordRepository();
        var products = new FakeProductRepository();
        var users = new FakeUserRepository();
        var reports = new FakeReportRepository();
        var generator = new FakeReportGenerator();
        var audit = new FakeAuditLog();

        var type = new ProductType { Name = "压力试验", CreatedAtUtc = clock.UtcNow };
        products.AddTypeAsync(type).Wait();
        var model = new ProductModel { ProductTypeId = type.Id, Name = "型号1", CreatedAtUtc = clock.UtcNow };
        products.AddModelAsync(model).Wait();

        var sequence = new List<SequenceItem>
        {
            new(1, "耐压试验", "PressureExecutor", "PassFail", 1)
        };
        var record = new TestRecord
        {
            Id = 1,
            RecordNumber = "R-20260901-0001",
            ProductModelId = model.Id,
            ProductIdentity = new ProductIdentity("SN001", null, null, null),
            ParameterSnapshot = "{\"snapshot\":1}",
            SequenceSnapshot = SequenceSnapshot.ToJson(sequence),
            DeviceMode = DeviceMode.Simulation,
            OperatorUserId = 1,
            StartedAtUtc = clock.UtcNow
        };
        if (recordCompleted) record.Complete(clock.UtcNow, "全部通过（1/1）");
        records.AddRecordAsync(record).Wait();
        var itemResult = new TestItemResult { Id = 1, RecordId = 1, TestItemPointId = 1 };
        itemResult.Start(clock.UtcNow);
        itemResult.SetResult(ItemResultState.Passed, "10.0", "实测 10.0", clock.UtcNow);
        records.AddItemResultAsync(itemResult).Wait();

        users.AddAsync(new User { Id = 1, LoginName = "op", DisplayName = "操作员", PasswordHash = "x", RoleId = 1, CreatedAtUtc = clock.UtcNow }).Wait();

        var service = new ReportService(records, products, users, reports, generator, clock, audit);
        return (service, reports, generator);
    }

    [Fact]
    public async Task Generate_CompletedRecord_WritesDataAndReportRecord()
    {
        var (service, reports, generator) = Create();
        var actor = TestContexts.With(PermissionCode.GenerateReports);

        var report = await service.GenerateAsync(actor, 1, "assets/report-templates/standard.xlsx", "D:\\out");

        Assert.Equal(ReportStatus.Completed, report.Status);
        Assert.Equal("D:\\out\\out.xlsx", report.OutputPath);
        Assert.Single(generator.Calls);
        Assert.Equal("R-20260901-0001", generator.Calls[0].RecordNumber);
        Assert.Equal("耐压试验", generator.Calls[0].FlowText);
        Assert.Single(generator.Calls[0].Items);
        Assert.Single(reports.Records);
    }

    [Fact]
    public async Task Generate_RunningRecord_IsRejected()
    {
        var (service, _, _) = Create(recordCompleted: false);
        var actor = TestContexts.With(PermissionCode.GenerateReports);
        await Assert.ThrowsAsync<DomainException>(() => service.GenerateAsync(actor, 1, "t.xlsx", "D:\\out"));
    }

    [Fact]
    public async Task Generate_GeneratorFailure_MarksReportFailed_AndThrows()
    {
        var (service, reports, generator) = Create();
        var actor = TestContexts.With(PermissionCode.GenerateReports);
        generator.Fail = true;

        await Assert.ThrowsAsync<DomainException>(() => service.GenerateAsync(actor, 1, "t.xlsx", "D:\\out"));
        Assert.Equal(ReportStatus.Failed, reports.Records[0].Status);
        Assert.NotNull(reports.Records[0].Error);
    }

    [Fact]
    public async Task Generate_RequiresGenerateReportsPermission()
    {
        var (service, _, _) = Create();
        var operatorActor = TestContexts.With(PermissionCode.ExecuteTests);
        await Assert.ThrowsAsync<AuthorizationException>(() => service.GenerateAsync(operatorActor, 1, "t.xlsx", "D:\\out"));
    }
}
