using System.Security.Cryptography;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Products;
using XXX.TestBench.Core.Domain.Reports;
using XXX.TestBench.Core.Domain.TestParameters;
using XXX.TestBench.Core.Domain.TestPoints;
using XXX.TestBench.Core.Execution;
using XXX.TestBench.Core.Ports;
using XXX.TestBench.Devices;
using XXX.TestBench.Devices.Executors;
using XXX.TestBench.Infrastructure.Configuration;
using XXX.TestBench.Infrastructure.Identity;
using XXX.TestBench.Infrastructure.Persistence;
using XXX.TestBench.Infrastructure.Persistence.Repositories;
using XXX.TestBench.Infrastructure.Reports;
using XXX.TestBench.Infrastructure.Services;
using XXX.TestBench.Infrastructure.Time;

namespace XXX.TestBench.Integration.Tests;

/// <summary>使用临时 SQLite、真实仓储与纯仿真运行时，所有产物由测试 TEMP 隔离。</summary>
internal sealed class SimulationRunFixture : IAsyncDisposable
{
    public TestEnv Env { get; } = TestEnv.Create();
    public DeviceOperationCoordinator Operations { get; } = new();
    public SqliteConnectionFactory Database { get; private set; } = null!;
    public RecordRepository Records { get; private set; } = null!;
    public ReportRepository Reports { get; private set; } = null!;
    public TestParameterRepository Parameters { get; private set; } = null!;
    public OperationFenceStore Evidence { get; private set; } = null!;
    public DeviceConfigurationStore Configurations { get; private set; } = null!;
    public IDeviceRuntime Runtime { get; private set; } = null!;
    public TestRunCoordinator Coordinator { get; private set; } = null!;
    public TestRunPreparationService Preparation { get; private set; } = null!;
    public string Token { get; private set; } = "";
    public int ModelId { get; private set; }
    public List<int> PointIds { get; } = [];
    public CommitFaultFactory Transactions { get; private set; } = null!;
    public ControlledExecutor Executor { get; } = new();
    public SimulationRunOptions Options { get; private set; } = null!;
    public AuthenticationService Authentication { get; private set; } = null!;

    /// <summary>仅在准备前替换测试资源；正式运行仍冻结当前资源摘要。</summary>
    public void UseReportResources(ReportInputResources resources)
    {
        Options = Options with { Resources = resources };
        Coordinator = NewCoordinator();
    }

    public static async Task<SimulationRunFixture> CreateAsync(int pointCount = 1)
    {
        var fixture = new SimulationRunFixture();
        try { await fixture.InitializeAsync(pointCount); return fixture; }
        catch { await fixture.DisposeAsync(); throw; }
    }

    private async Task InitializeAsync(int pointCount)
    {
        var clock = new SystemClock();
        Database = new(Env.DbPath);
        var hasher = new Pbkdf2PasswordHasher();
        await new SqliteDatabase(Database, hasher).CreateNewAsync(Guid.NewGuid().ToString("D"), "admin123");
        var users = new UserRepository(Database);
        var user = (await users.GetByLoginNameAsync("admin"))!;
        user.MustChangePassword = false;
        await users.UpdateAsync(user);
        var sessions = new InMemorySessionManager();
        Token = (await sessions.CreateAsync(user, TimeSpan.FromHours(1))).Token;
        var audit = new SqliteAuditLog(Database);
        var authentication = new AuthenticationService(users, hasher, sessions, clock, audit);
        Authentication = authentication;
        var products = new ProductRepository(Database);
        var type = new ProductType { Name = "仿真测试类型", CreatedAtUtc = clock.UtcNow };
        await products.AddTypeAsync(type);
        var model = new ProductModel { Name = "仿真测试型号", ProductTypeId = type.Id, CreatedAtUtc = clock.UtcNow };
        await products.AddModelAsync(model);
        ModelId = model.Id;
        var points = new TestPointRepository(Database);
        for (var i = 0; i < pointCount; i++)
        {
            var point = new TestItemPoint { ProductTypeId = type.Id, Name = "仿真压力" + i,
                ExecutorCode = Executor.ExecutorCode, SortOrder = i + 1, CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow };
            await points.AddAsync(point);
            PointIds.Add(point.Id);
        }
        var sequence = new ModelPointConfigRepository(Database);
        await sequence.ReplaceAsync(model.Id, PointIds.Select((id, index) => new ModelPointConfig(model.Id, id, index + 1)).ToArray(), 0);
        Parameters = new(Database);
        await Parameters.SaveProjectAsync(new ProjectTestParameter { TestTimeSeconds = 1, UpdatedAtUtc = clock.UtcNow });
        await Parameters.SaveProductAsync(new ProductTestParameter { ProductTypeId = type.Id, ProductModelId = model.Id,
            TestVoltageV = 24, ProtectCurrentMa = 10, UpdatedAtUtc = clock.UtcNow });
        var config = await Env.LoadSnapshotAsync();
        Configurations = new(Env.ConfigRoot);
        await Configurations.StageAsync(config);
        await Configurations.CommitActiveAsync(config.Revision);
        Runtime = await new DeviceRuntimeFactory(config, clock, requiredSignals: Executor.RequiredSignals).CreateAsync();
        await Runtime.StartAsync();
        var configurations = new DeviceConfigurationService(Configurations, Operations, audit, [], () => false,
            (_, _) => throw new NotSupportedException(), () => Runtime, _ => Task.CompletedTask,
            Executor.RequiredSignals, _ => { });
        Records = new(Database);
        Reports = new(Database);
        Evidence = new(Path.Combine(Env.Root, "state"), TimeProvider.System, TimeSpan.FromSeconds(5));
        var executors = new SingleExecutorFactory(Executor);
        Preparation = new(authentication, users, products, points, sequence,
            new TestParameterService(Parameters, products, clock, audit), configurations, executors,
            Records, Evidence, () => Runtime, "test-bench", "test-node", TimeSpan.FromMinutes(2), clock, TimeProvider.System);
        Transactions = new(new SqliteUnitOfWorkFactory(Database));
        // 资源明确标为测试夹具；只验证 Pending 输入，不冒充可计算的真实 Excel 模板。
        var hash = Convert.ToHexString(SHA256.HashData("simulation-test-fixture"u8.ToArray())).ToLowerInvariant();
        var databaseId = Convert.ToString(await Database.Db.Ado.ExecuteScalarAsync(
            "SELECT value FROM app_metadata WHERE key='database_instance_id'", new { }, default))!;
        Options = new("test-bench", databaseId,
            new ReportInputResources { TemplateKey = "test-only/template.xlsx", MappingKey = "test-only/mapping.json",
                TemplateHash = hash, MappingHash = hash }, "Asia/Shanghai");
        Coordinator = NewCoordinator();
    }

    public TestRunCoordinator NewCoordinator() => new(Operations, Preparation,
        new TestRunStartPersistenceService(Records, Transactions),
        new TestRunFinalizationService(Records, Reports, Transactions), Records, Evidence,
        new SingleExecutorFactory(Executor), () => Runtime, Options, new SystemClock(), TimeProvider.System);

    public async Task<StartRequest> PrepareAsync(Core.Domain.Records.RunScope scope = Core.Domain.Records.RunScope.Auto)
    {
        var ready = await Coordinator.PrepareAsync(Token, ModelId, new("000123", null, null, null, "车01"), scope,
            scope == Core.Domain.Records.RunScope.Single ? PointIds.Last() : null);
        return new(Guid.NewGuid(), ready.OperationId, ready.PreparationSessionId, ready.PreparationHash);
    }

    public async ValueTask DisposeAsync()
    {
        Executor.Continue.TrySetResult();
        if (Coordinator is not null) await Coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        if (Runtime is not null) { await Runtime.StopAsync(); await Runtime.DisposeAsync(); }
        if (Configurations is not null) await Configurations.DisposeAsync();
        await Operations.DisposeAsync();
        Database?.Db.Dispose();
        Env.Dispose();
    }

    internal sealed class ControlledExecutor : ITestItemExecutor
    {
        private readonly SimulationPressureExecutor _inner = new();
        public string ExecutorCode => _inner.ExecutorCode;
        public IReadOnlyList<RequiredSignal> RequiredSignals => _inner.RequiredSignals;
        public IReadOnlyList<ItemOutputDefinition> Outputs => _inner.Outputs;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Block { get; set; }
        public bool OmitMeasurements { get; set; }
        public int Calls;
        public async Task<ItemExecutionOutcome> ExecuteAsync(ItemExecutionContext context, CancellationToken ct = default)
        {
            Interlocked.Increment(ref Calls);
            Entered.TrySetResult();
            if (Block) await Continue.Task.WaitAsync(ct);
            var result = await _inner.ExecuteAsync(context, ct);
            return OmitMeasurements ? result with { Measurements = [] } : result;
        }
    }

    private sealed class SingleExecutorFactory(ITestItemExecutor executor) : ITestItemExecutorFactory
    {
        public IReadOnlyCollection<string> Codes => [executor.ExecutorCode];
        public ITestItemExecutor Get(string code) => code == executor.ExecutorCode ? executor : throw new InvalidOperationException();
    }

    /// <summary>在指定事务真正提交后模拟回执丢失，检验按稳定标识查证而非重发。</summary>
    internal sealed class CommitFaultFactory(IUnitOfWorkFactory inner) : IUnitOfWorkFactory
    {
        public int FailAfterCommitNumber { get; set; }
        public int FailBeforeCommitNumber { get; set; }
        private int _count;
        public IUnitOfWork Create() => new CommitFault(inner.Create(), this, Interlocked.Increment(ref _count));
        private sealed class CommitFault(IUnitOfWork inner, CommitFaultFactory owner, int number) : IUnitOfWork
        {
            public Task BeginTransactionAsync(CancellationToken ct = default) => inner.BeginTransactionAsync(ct);
            public async Task CommitAsync(CancellationToken ct = default)
            {
                if (owner.FailBeforeCommitNumber == number) throw new IOException("测试：提交前故障");
                await inner.CommitAsync(ct);
                if (owner.FailAfterCommitNumber == number) throw new IOException("测试：提交成功但回执丢失");
            }
            public Task RollbackAsync(CancellationToken ct = default) => inner.RollbackAsync(ct);
            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }
}
