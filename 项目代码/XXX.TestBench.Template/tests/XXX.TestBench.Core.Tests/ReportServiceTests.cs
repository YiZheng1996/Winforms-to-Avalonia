using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Domain.Products;
using XXX.TestBench.Core.Domain.Reports;
using XXX.TestBench.Core.Domain.Tasks;
using Xunit;

namespace XXX.TestBench.Core.Tests;

public class ReportServiceTests
{
    private static (ReportService Service, FakeReportRepository Reports, FakeReportGenerator Generator, FakeTaskRepository TaskRepo) Create(bool recordCompleted = true)
    {
        var clock = new FixedClock();
        var tasks = new FakeTaskRepository();
        var products = new FakeProductRepository();
        var definitions = new FakeTestDefinitionRepository();
        var users = new FakeUserRepository();
        var reports = new FakeReportRepository();
        var generator = new FakeReportGenerator();
        var audit = new FakeAuditLog();

        var type = new ProductType { Code = "PT", Name = "压力试验", CreatedAtUtc = clock.UtcNow };
        products.AddTypeAsync(type).Wait();
        var model = new ProductModel { ProductTypeId = type.Id, Code = "M1", Name = "型号1", CreatedAtUtc = clock.UtcNow };
        products.AddModelAsync(model).Wait();

        var task = new TestTask { Id = 1, TaskNumber = "T-20260901-0001", ProductModelId = model.Id, ProductIdentity = new ProductIdentity("SN001", null, null, null), CreatedByUserId = 1, CreatedAtUtc = clock.UtcNow };
        tasks.AddAsync(task).Wait();
        var record = new TestRecord { Id = 1, TaskId = 1, ParameterSnapshot = "{\"snapshot\":1}", DeviceMode = Domain.Devices.DeviceMode.Simulation, OperatorUserId = 1, StartedAtUtc = clock.UtcNow };
        if (recordCompleted) record.Complete(clock.UtcNow, "全部通过（1/1）");
        tasks.AddRecordAsync(record).Wait();
        var itemResult = new TestItemResult { Id = 1, RecordId = 1, TestItemDefinitionId = 1 };
        itemResult.Start(clock.UtcNow);
        itemResult.SetResult(ItemResultState.Passed, "10.0", "实测 10.0", clock.UtcNow);
        tasks.AddItemResultAsync(itemResult).Wait();

        users.AddAsync(new User { Id = 1, LoginName = "op", DisplayName = "操作员", PasswordHash = "x", RoleId = 1, CreatedAtUtc = clock.UtcNow }).Wait();
        definitions.Items.Add(new Domain.TestDefinitions.TestItemDefinition { Id = 1, Code = "PRESSURE", Name = "耐压试验", ExecutorCode = "PressureExecutor", ResultKind = "PassFail", CreatedAtUtc = clock.UtcNow });

        var service = new ReportService(tasks, products, definitions, users, reports, generator, clock, audit);
        return (service, reports, generator, tasks);
    }

    [Fact]
    public async Task Generate_CompletedRecord_WritesDataAndReportRecord()
    {
        var (service, reports, generator, _) = Create();
        var actor = TestContexts.With(PermissionCode.GenerateReports);

        var report = await service.GenerateAsync(actor, 1, "assets/report-templates/standard.xlsx", "D:\\out");

        Assert.Equal(ReportStatus.Completed, report.Status);
        Assert.Equal("D:\\out\\out.xlsx", report.OutputPath);
        Assert.Single(generator.Calls);
        Assert.Equal("T-20260901-0001", generator.Calls[0].TaskNumber);
        Assert.Equal("固定流程", generator.Calls[0].FlowText);
        Assert.Single(generator.Calls[0].Items);
        Assert.Single(reports.Records);
    }

    [Fact]
    public async Task Generate_RunningRecord_IsRejected()
    {
        var (service, _, _, _) = Create(recordCompleted: false);
        var actor = TestContexts.With(PermissionCode.GenerateReports);
        await Assert.ThrowsAsync<DomainException>(() => service.GenerateAsync(actor, 1, "t.xlsx", "D:\\out"));
    }

    [Fact]
    public async Task Generate_GeneratorFailure_MarksReportFailed_AndThrows()
    {
        var (service, reports, generator, _) = Create();
        var actor = TestContexts.With(PermissionCode.GenerateReports);
        generator.Fail = true;

        await Assert.ThrowsAsync<DomainException>(() => service.GenerateAsync(actor, 1, "t.xlsx", "D:\\out"));
        Assert.Equal(ReportStatus.Failed, reports.Records[0].Status);
        Assert.NotNull(reports.Records[0].Error);
    }

    [Fact]
    public async Task Generate_RequiresGenerateReportsPermission()
    {
        var (service, _, _, _) = Create();
        var operatorActor = TestContexts.With(PermissionCode.ExecuteTests);
        await Assert.ThrowsAsync<AuthorizationException>(() => service.GenerateAsync(operatorActor, 1, "t.xlsx", "D:\\out"));
    }
}
