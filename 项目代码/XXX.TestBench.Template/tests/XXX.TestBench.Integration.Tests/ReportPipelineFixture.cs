using System.Security.Cryptography;
using System.Text;
using ClosedXML.Excel;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Domain.Reports;
using XXX.TestBench.Core.Ports;
using XXX.TestBench.Infrastructure.Configuration;
using XXX.TestBench.Infrastructure.Persistence;
using XXX.TestBench.Infrastructure.Persistence.Repositories;
using XXX.TestBench.Infrastructure.Reports;

namespace XXX.TestBench.Integration.Tests;

/// <summary>真实 SQLite/资源映射/文件归档，计算器是明确的离线替身，不冒充 LibreOffice 重算验收。</summary>
internal sealed class ReportPipelineFixture : IAsyncDisposable
{
    public SimulationRunFixture Run { get; private set; } = null!;
    public FileReportStorage Storage { get; private set; } = null!;
    public ReportGenerationWorker Worker { get; private set; } = null!;
    public ReportRetryService Retries { get; private set; } = null!;
    public SimulationRunFixture.CommitFaultFactory Transactions { get; private set; } = null!;
    public ControlledCalculationEngine Engine { get; } = new();
    public ReportRecord Report { get; private set; } = null!;

    public static async Task<ReportPipelineFixture> CreateAsync()
    {
        var f = new ReportPipelineFixture { Run = await SimulationRunFixture.CreateAsync() };
        try { await f.InitializeAsync(); return f; }
        catch { await f.DisposeAsync(); throw; }
    }

    private async Task InitializeAsync()
    {
        var resources = new FileModelResourceStore(Path.Combine(Run.Env.Root, "resources"));
        using var template = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Report");
            sheet.Cell("B1").FormulaA1 = "IF(A1=\"000123\",\"合格\",\"不合格\")";
            workbook.SaveAs(template);
        }
        template.Position = 0;
        var templateHash = await resources.ImportAsync("test-only/template.xlsx", template);
        const string mapping = """
            {"SchemaVersion":1,"TemplateId":"test-only","Version":"1","Purpose":"ProductEvaluation",
             "SupportedRunScopes":["Auto"],
             "Inputs":[{"Source":{"kind":"metadata","Field":"ProductNumber"},
               "Target":{"WorksheetName":"Report","CellAddress":"A1"},"ValueKind":"Text","Unit":null,"Required":true}],
             "ProductConclusion":{"Target":{"WorksheetName":"Report","CellAddress":"B1"},
               "QualifiedValues":["合格"],"UnqualifiedValues":["不合格"],
               "FormulaTargets":[{"WorksheetName":"Report","CellAddress":"B1"}]}}
            """;
        using var mappingStream = new MemoryStream(Encoding.UTF8.GetBytes(mapping));
        var mappingHash = await resources.ImportAsync("test-only/mapping.json", mappingStream);
        Run.UseReportResources(new ReportInputResources { TemplateKey = "test-only/template.xlsx",
            MappingKey = "test-only/mapping.json", TemplateHash = templateHash, MappingHash = mappingHash });
        var start = await Run.PrepareAsync();
        var handle = await Run.Coordinator.StartAsync(start, Run.Token);
        await Run.Coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Report = Assert.Single(await Run.Reports.ListByRecordAsync(handle.RecordId));
        Storage = new FileReportStorage(Path.Combine(Run.Env.Root, "archive"));
        var workRoot = Path.Combine(Run.Env.Root, "report-work");
        Transactions = new(new SqliteUnitOfWorkFactory(Run.Database));
        Worker = new ReportGenerationWorker(Run.Reports,
            new FileReportCalculationRequestFactory(Run.Records, resources, Run.Options.DatabaseInstanceId,
                workRoot, TimeSpan.FromSeconds(10)), Engine, Storage, Transactions, TimeProvider.System, workRoot);
        Retries = new ReportRetryService(Run.Authentication, new UserRepository(Run.Database), Run.Reports,
            new SqliteUnitOfWorkFactory(Run.Database), new SqliteAuditLog(Run.Database), "test-node", TimeProvider.System);
    }

    public Task<IReadOnlyList<ReportRecord>> RecoverAsync() => Worker.RecoverPublishedAsync("test-node",
        Run.Options.DatabaseInstanceId, Run.Records, Storage);

    public ValueTask DisposeAsync() => Run.DisposeAsync();

    internal sealed class ControlledCalculationEngine : IReportCalculationEngine
    {
        public int Calls;
        public bool Fail;
        public bool Block;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<ReportCalculationResult> CalculateAsync(ReportCalculationRequest request, CancellationToken ct = default)
        {
            Interlocked.Increment(ref Calls);
            Entered.TrySetResult();
            if (Block) await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            // 核对真实请求构造器保留了前导零，未覆盖公式。
            using (var workbook = new XLWorkbook(request.InputWorkbookPath))
            {
                Assert.Equal("000123", workbook.Worksheet("Report").Cell("A1").GetString());
                Assert.True(workbook.Worksheet("Report").Cell("B1").HasFormula);
            }
            var folder = Path.Combine(request.WorkDirectoryPath, $".report-engine-{request.RevisionId:N}-test", "output");
            Directory.CreateDirectory(folder);
            var output = Path.Combine(folder, "input.xlsx");
            File.Copy(request.InputWorkbookPath, output);
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(output))).ToLowerInvariant();
            return new ReportCalculationResult(!Fail, request.RevisionId, request.InputHash,
                "TestDouble", "1", output, hash, null, null, "ExcelFormula", "合格",
                Fail ? "Calculation" : null, Fail ? "离线故障注入" : null);
        }
    }
}
