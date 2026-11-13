using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Domain.Products;

using XXX.TestBench.Core.Domain.Reports;
using XXX.TestBench.Core.Domain.Records;
using XXX.TestBench.Core.Domain.TestParameters;
using XXX.TestBench.Core.Domain.TestPoints;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Core.Tests;

public sealed class FixedClock : IClock
{
    public DateTime UtcNow { get; set; } = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
    public void Advance(TimeSpan span) => UtcNow += span;
}

public sealed class FakeAuditLog : IAuditLog
{
    public List<string> Entries { get; } = new();
    public Task WriteAsync(string actor, string action, string? target, string? detail, CancellationToken ct = default)
    {
        Entries.Add($"{actor}|{action}|{target}|{detail}");
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AuditEntry>> ListRecentAsync(int limit, CancellationToken ct = default)
        => SearchAsync(new AuditLogQuery(Limit: limit), ct);

    public Task<IReadOnlyList<AuditEntry>> SearchAsync(AuditLogQuery query, CancellationToken ct = default)
    {
        var entries = Entries.Select((value, index) =>
        {
            var fields = value.Split('|', 4);
            return new AuditEntry(
                index + 1,
                fields.ElementAtOrDefault(0) ?? string.Empty,
                fields.ElementAtOrDefault(1) ?? string.Empty,
                fields.ElementAtOrDefault(2),
                fields.ElementAtOrDefault(3),
                DateTime.UtcNow);
        });

        if (!string.IsNullOrWhiteSpace(query.Actor))
            entries = entries.Where(entry => entry.Actor.Contains(query.Actor, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(query.Action))
            entries = entries.Where(entry => string.Equals(entry.Action, query.Action, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(query.Text))
            entries = entries.Where(entry => (entry.Target ?? string.Empty).Contains(query.Text, StringComparison.OrdinalIgnoreCase)
                || (entry.Detail ?? string.Empty).Contains(query.Text, StringComparison.OrdinalIgnoreCase));

        var result = entries.Reverse().Take(Math.Clamp(query.Limit, 1, 1000)).ToList();
        return Task.FromResult<IReadOnlyList<AuditEntry>>(result);
    }
}

public sealed class FakeSessionManager : ISessionManager
{
    private readonly Dictionary<string, Session> _sessions = new();

    public Task<Session> CreateAsync(User user, TimeSpan lifetime, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var session = new Session { Token = Guid.NewGuid().ToString("N"), UserId = user.Id, CreatedAtUtc = now, ExpiresAtUtc = now + lifetime };
        _sessions[session.Token] = session;
        return Task.FromResult(session);
    }

    public Task<Session?> GetActiveAsync(string token, CancellationToken ct = default)
    {
        _sessions.TryGetValue(token, out var session);
        return Task.FromResult(session);
    }

    public Task RevokeAsync(string token, CancellationToken ct = default)
    {
        _sessions.Remove(token);
        return Task.CompletedTask;
    }

    public Task RevokeAllForUserAsync(int userId, CancellationToken ct = default)
    {
        foreach (var key in _sessions.Where(kv => kv.Value.UserId == userId).Select(kv => kv.Key).ToList())
            _sessions.Remove(key);
        return Task.CompletedTask;
    }
}

public sealed class FakeUserRepository : IUserRepository
{
    public List<User> Users { get; } = new();
    public List<Role> Roles { get; } = new();
    private int _nextId = 1;
    private int _nextRoleId = 1;

    public Task<User?> GetByLoginNameAsync(string loginName, CancellationToken ct = default)
        => Task.FromResult(Users.FirstOrDefault(u => u.LoginName == loginName));

    public Task<User?> GetByIdAsync(int id, CancellationToken ct = default)
        => Task.FromResult(Users.FirstOrDefault(u => u.Id == id));

    public Task<Role?> GetRoleAsync(int roleId, CancellationToken ct = default)
        => Task.FromResult(Roles.FirstOrDefault(r => r.Id == roleId));

    public Task AddAsync(User user, CancellationToken ct = default)
    {
        if (user.Id == 0) user.Id = _nextId++;
        else _nextId = Math.Max(_nextId, user.Id + 1);
        Users.Add(user);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(User user, CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<User>> ListUsersAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<User>>(Users.ToList());

    public Task<IReadOnlyList<Role>> ListRolesAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Role>>(Roles.ToList());

    public Task<bool> RoleNameExistsAsync(string name, int? excludingRoleId = null, CancellationToken ct = default)
        => Task.FromResult(Roles.Any(r => (!excludingRoleId.HasValue || r.Id != excludingRoleId.Value)
            && string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)));

    public Task AddRoleAsync(Role role, CancellationToken ct = default)
    {
        if (role.Id == 0)
        {
            _nextRoleId = Math.Max(_nextRoleId, Roles.Select(item => item.Id).DefaultIfEmpty(0).Max() + 1);
            role.Id = _nextRoleId++;
        }
        else _nextRoleId = Math.Max(_nextRoleId, role.Id + 1);
        Roles.Add(role);
        return Task.CompletedTask;
    }

    public Task RenameRoleAsync(int roleId, string name, CancellationToken ct = default)
    {
        var role = Roles.FirstOrDefault(r => r.Id == roleId) ?? throw new InvalidOperationException("角色不存在");
        role.Name = name;
        return Task.CompletedTask;
    }

    public Task DeleteRoleAsync(int roleId, CancellationToken ct = default)
    {
        Roles.RemoveAll(r => r.Id == roleId);
        return Task.CompletedTask;
    }

    public Task ClearRolePermissionsAsync(int roleId, CancellationToken ct = default)
    {
        var role = Roles.FirstOrDefault(r => r.Id == roleId) ?? throw new InvalidOperationException("角色不存在");
        role.Permissions.Clear();
        return Task.CompletedTask;
    }

    public Task AddRolePermissionAsync(int roleId, PermissionCode permission, CancellationToken ct = default)
    {
        var role = Roles.FirstOrDefault(r => r.Id == roleId) ?? throw new InvalidOperationException("角色不存在");
        role.Permissions.Add(permission);
        return Task.CompletedTask;
    }

    public Task<int> CountUsersByRoleAsync(int roleId, CancellationToken ct = default)
        => Task.FromResult(Users.Count(u => u.RoleId == roleId));
}

public sealed class FakeUnitOfWorkFactory : IUnitOfWorkFactory
{
    public int CreatedCount { get; private set; }
    public FakeUnitOfWork Last { get; private set; } = new();

    public IUnitOfWork Create()
    {
        CreatedCount++;
        Last = new FakeUnitOfWork();
        return Last;
    }
}

public sealed class FakeUnitOfWork : IUnitOfWork
{
    public bool Begun { get; private set; }
    public bool Committed { get; private set; }
    public bool RolledBack { get; private set; }

    public Task BeginTransactionAsync(CancellationToken ct = default)
    {
        Begun = true;
        return Task.CompletedTask;
    }

    public Task CommitAsync(CancellationToken ct = default)
    {
        Committed = true;
        return Task.CompletedTask;
    }

    public Task RollbackAsync(CancellationToken ct = default)
    {
        RolledBack = true;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class FakeProductRepository : IProductRepository
{
    public List<ProductType> Types { get; } = new();
    public List<ProductModel> Models { get; } = new();
    public HashSet<int> TestPointTypeReferences { get; } = new();
    public HashSet<int> RecordModelReferences { get; } = new();
    private int _nextTypeId = 1;
    private int _nextModelId = 1;
    public Task<ProductType?> GetTypeAsync(int id, CancellationToken ct = default)
        => Task.FromResult(Types.FirstOrDefault(t => t.Id == id));

    public Task<ProductModel?> GetModelAsync(int id, CancellationToken ct = default)
        => Task.FromResult(Models.FirstOrDefault(m => m.Id == id));

    public Task<IReadOnlyList<ProductType>> ListTypesAsync(bool includeDisabled, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ProductType>>(includeDisabled ? Types : Types.Where(t => t.IsEnabled).ToList());

    public Task<IReadOnlyList<ProductModel>> ListModelsAsync(int? productTypeId, bool includeDisabled, CancellationToken ct = default)
    {
        var q = Models.AsEnumerable();
        if (productTypeId is not null) q = q.Where(m => m.ProductTypeId == productTypeId);
        if (!includeDisabled) q = q.Where(m => m.IsEnabled);
        return Task.FromResult<IReadOnlyList<ProductModel>>(q.ToList());
    }

    public Task AddTypeAsync(ProductType type, CancellationToken ct = default)
    {
        if (type.Id == 0) type.Id = _nextTypeId++;
        Types.Add(type);
        return Task.CompletedTask;
    }

    public Task AddModelAsync(ProductModel model, CancellationToken ct = default)
    {
        if (model.Id == 0) model.Id = _nextModelId++;
        Models.Add(model);
        return Task.CompletedTask;
    }
    public Task UpdateTypeAsync(ProductType type, CancellationToken ct = default)
    {
        var index = Types.FindIndex(t => t.Id == type.Id);
        if (index >= 0) Types[index] = type;
        return Task.CompletedTask;
    }

    public Task UpdateModelAsync(ProductModel model, CancellationToken ct = default)
    {
        var index = Models.FindIndex(m => m.Id == model.Id);
        if (index >= 0) Models[index] = model;
        return Task.CompletedTask;
    }
    public Task<int> CountModelsByTypeAsync(int productTypeId, CancellationToken ct = default)
        => Task.FromResult(Models.Count(m => m.ProductTypeId == productTypeId));

    public Task<int> CountTestPointsByTypeAsync(int productTypeId, CancellationToken ct = default)
        => Task.FromResult(TestPointTypeReferences.Contains(productTypeId) ? 1 : 0);

    public Task<int> CountRecordsByModelAsync(int productModelId, CancellationToken ct = default)
        => Task.FromResult(RecordModelReferences.Contains(productModelId) ? 1 : 0);

    public Task DeleteTypeAsync(int productTypeId, CancellationToken ct = default)
    {
        Types.RemoveAll(t => t.Id == productTypeId);
        return Task.CompletedTask;
    }

    public Task DeleteModelAsync(int productModelId, CancellationToken ct = default)
    {
        Models.RemoveAll(m => m.Id == productModelId);
        return Task.CompletedTask;
    }
}

public sealed class FakeTestPointRepository : ITestPointRepository
{
    public List<TestItemPoint> Items { get; } = new();
    private int _nextId = 1;

    public Task<TestItemPoint?> GetAsync(int id, CancellationToken ct = default)
        => Task.FromResult(Items.FirstOrDefault(i => i.Id == id));

    public Task<IReadOnlyList<TestItemPoint>> ListByTypeAsync(int productTypeId, bool includeDisabled, CancellationToken ct = default)
    {
        var q = Items.Where(i => i.ProductTypeId == productTypeId);
        if (!includeDisabled) q = q.Where(i => i.IsEnabled);
        return Task.FromResult<IReadOnlyList<TestItemPoint>>(q.OrderBy(i => i.SortOrder).ThenBy(i => i.Id).ToList());
    }

    public Task AddAsync(TestItemPoint point, CancellationToken ct = default)
    {
        if (point.Id == 0) point.Id = _nextId++;
        Items.Add(point);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(TestItemPoint point, CancellationToken ct = default) => Task.CompletedTask;

    public Task DeleteAsync(int id, CancellationToken ct = default)
    {
        Items.RemoveAll(i => i.Id == id);
        return Task.CompletedTask;
    }

    public Task<int> CountModelReferencesAsync(int pointId, CancellationToken ct = default)
        => Task.FromResult(ModelReferences.Count(r => r.TestItemPointId == pointId));

    public Task<int> CountResultReferencesAsync(int pointId, CancellationToken ct = default)
        => Task.FromResult(ResultReferences.Count(r => r.TestItemPointId == pointId));

    public List<ModelPointConfig> ModelReferences { get; } = new();
    public List<TestItemResult> ResultReferences { get; } = new();
}

public sealed class FakeModelPointConfigRepository : IModelPointConfigRepository
{
    public Dictionary<int, List<ModelPointConfig>> ByModel { get; } = new();
    private readonly Dictionary<int, long> _sequenceRevisions = new();

    public Task<IReadOnlyList<ModelPointConfig>> ListByModelAsync(int productModelId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ModelPointConfig>>(ByModel.TryGetValue(productModelId, out var list) ? list.OrderBy(c => c.SortOrder).ToList() : new List<ModelPointConfig>());

    public Task ReplaceAsync(
        int productModelId,
        IReadOnlyList<ModelPointConfig> configs,
        long expectedSequenceRevision,
        CancellationToken ct = default)
    {
        var currentRevision = _sequenceRevisions.GetValueOrDefault(productModelId);
        if (currentRevision != expectedSequenceRevision)
            throw new InvalidOperationException("型号项点顺序版本冲突");
        ByModel[productModelId] = configs.ToList();
        _sequenceRevisions[productModelId] = checked(expectedSequenceRevision + 1);
        return Task.CompletedTask;
    }
}

public sealed class FakeRecordRepository : IRecordRepository
{
    public List<TestRecord> Records { get; } = new();
    public List<TestItemResult> Results { get; } = new();
    private int _nextId = 1;

    public Task<bool> ExistsRecordNumberAsync(string recordNumber, CancellationToken ct = default)
        => Task.FromResult(Records.Any(r => r.RecordNumber == recordNumber));

    public Task AddRecordAsync(TestRecord record, CancellationToken ct = default)
    {
        if (record.Id == 0) record.Id = _nextId++;
        Records.Add(record);
        return Task.CompletedTask;
    }

    public Task AddItemResultAsync(TestItemResult result, CancellationToken ct = default)
    {
        if (result.Id == 0) result.Id = _nextId++;
        Results.Add(result);
        return Task.CompletedTask;
    }

    public Task<TestRecord?> GetRecordAsync(int recordId, CancellationToken ct = default)
        => Task.FromResult(Records.FirstOrDefault(r => r.Id == recordId));

    public Task<TestRecord?> GetActiveRunningRecordAsync(string benchId, CancellationToken ct = default)
        => Task.FromResult(Records.FirstOrDefault(r => r.State == RecordState.Running && r.BenchId == benchId));

    public Task UpdateRecordAsync(TestRecord record, CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<TestItemResult>> ListItemResultsAsync(int recordId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TestItemResult>>(Results.Where(r => r.RecordId == recordId).ToList());

    public Task UpdateItemResultAsync(TestItemResult result, CancellationToken ct = default) => Task.CompletedTask;

    public Task<IReadOnlyList<TestRecord>> ListRecordsAsync(int? state, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TestRecord>>(state is null ? Records.ToList() : Records.Where(r => (int)r.State == state).ToList());
}

public sealed class FakeRuntime : IDeviceRuntime
{
    private readonly DeviceRuntimeInfo _status;
    private readonly Dictionary<string, object?> _values = new();
    private readonly Func<string, object?>? _readOverride;
    private DevicePoint? _currentPoint;
    public List<string> Writes { get; } = new();

    public FakeRuntime(bool isSimulation, DeviceHealth health = DeviceHealth.Healthy, bool connected = true, Func<string, object?>? readOverride = null)
    {
        IsSimulation = isSimulation;
        _readOverride = readOverride;
        _status = new DeviceRuntimeInfo(Name, "Fake", "fake://1", isSimulation, health, connected, null,
            "fake-device", "fake-channel", "fake-revision", 1,
            DeviceConnectionState.Online, isSimulation ? DeviceMode.Simulation : DeviceMode.Hardware);
    }

    public string Name => "Fake";
    public DeviceMode Mode => IsSimulation ? DeviceMode.Simulation : DeviceMode.Hardware;
    public bool IsSimulation { get; }
    public DeviceRuntimeInfo Status => _status;
    public string ActiveRevision => _status.Revision;
    public SignalBindingsConfig SignalBindings => new();
    public RuntimeActivationReport ActivationReport => RuntimeActivationReport.Empty;
    public int StartCount { get; private set; }
    public int StopCount { get; private set; }
    public Task StartAsync(CancellationToken ct = default)
    {
        StartCount++;
        return Task.CompletedTask;
    }
    public Task StopAsync(CancellationToken ct = default)
    {
        StopCount++;
        return Task.CompletedTask;
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    public Task<IReadOnlyList<DevicePoint>> ListPointsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<DevicePoint>>(Array.Empty<DevicePoint>());
    public bool TryGetCachedValue(string pointId, out PointValue value)
    {
        value = default!;
        return false;
    }
    public IReadOnlyList<PointValue> ListCachedValues() => Array.Empty<PointValue>();
    public DeviceRuntimeInfo GetDeviceStatus(string deviceId)
    {
        if (!string.Equals(deviceId, _status.DeviceId, StringComparison.OrdinalIgnoreCase))
            throw new DomainException($"设备 {deviceId} 不在测试运行时");
        return _status;
    }
    public IReadOnlyList<DeviceRuntimeInfo> ListDeviceStatuses() => new[] { _status };
    public DevicePoint? GetPoint(string pointId) => GetRegisteredPoint(pointId);
    public Task<PointValue> ReadAsync(DevicePoint point, CancellationToken ct = default)
        => Task.FromResult(CreatePointValue(point, _readOverride is not null ? _readOverride(point.Address) : (_values.TryGetValue(point.Address, out var v) ? v : null)));
    public void RegisterPoint(DevicePoint point) => _currentPoint = point;
    public DevicePoint? GetRegisteredPoint(string pointId)
        => _currentPoint is not null && string.Equals(_currentPoint.PointId, pointId, StringComparison.OrdinalIgnoreCase)
            ? _currentPoint
            : null;
    public Task<PointValue> ReadFreshAsync(string pointId, CancellationToken ct = default)
    {
        var point = GetRegisteredPoint(pointId) ?? throw new DomainException($"点位 {pointId} 不在测试运行时");
        var value = _readOverride is not null ? _readOverride(point.Address) : (_values.TryGetValue(point.Address, out var cached) ? cached : null);
        return Task.FromResult(CreatePointValue(point, value));
    }
    public Task<PointValue> WriteAsync(DevicePoint point, object? value, CancellationToken ct = default)
    {
        Writes.Add($"{point.Code}={value}");
        _values[point.Address] = value;
        return Task.FromResult(CreatePointValue(point, value));
    }

    private PointValue CreatePointValue(DevicePoint point, object? value)
        => new(point.Code, point.Address, PointQuality.Good, value, DateTime.UtcNow,
            point.PointId, point.DeviceId, _status.ConnectionGeneration, point.Revision, value);
}

public sealed class FakeRuntimeFactory : IDeviceRuntimeFactory
{
    public Task<IDeviceRuntime> CreateAsync(CancellationToken ct = default)
        => Task.FromResult<IDeviceRuntime>(new FakeRuntime(true));
}

public static class TestContexts
{
    public static UserContext With(params PermissionCode[] permissions)
    {
        var role = new Role { Id = 1, Name = "Test" };
        foreach (var p in permissions) role.Permissions.Add(p);
        return new UserContext { UserId = 1, LoginName = "tester", DisplayName = "测试员", Role = role };
    }

    public static UserContext Admin() => With(Enum.GetValues<PermissionCode>());
}

public sealed class FakeReportRepository : IReportRepository
{
    public List<ReportRecord> Records { get; } = new();
    private int _nextId = 1;

    public Task<ReportRecord> CreatePendingAsync(ReportRecord record, CancellationToken ct = default)
    {
        var existing = Records.FirstOrDefault(r => r.RequestId == record.RequestId);
        if (existing is not null)
        {
            if (existing.TestRecordId != record.TestRecordId || existing.InputHash != record.InputHash
                || existing.RevisionId != record.RevisionId)
                throw new InvalidOperationException("报表请求标识已用于不同输入");
            return Task.FromResult(existing);
        }
        if (record.Id == 0) record.Id = _nextId++;
        record.RevisionNo = Records.Where(r => r.TestRecordId == record.TestRecordId)
            .Select(r => r.RevisionNo ?? 0).DefaultIfEmpty().Max() + 1;
        Records.Add(record);
        return Task.FromResult(record);
    }

    public Task<ReportRecord?> GetAsync(int id, CancellationToken ct = default)
        => Task.FromResult(Records.FirstOrDefault(r => r.Id == id));
    public Task<ReportRecord?> GetByRequestIdAsync(string requestId, CancellationToken ct = default)
        => Task.FromResult(Records.FirstOrDefault(r => r.RequestId == requestId));
    public Task<IReadOnlyList<ReportRecord>> ListByRecordAsync(int testRecordId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ReportRecord>>(Records.Where(r => r.TestRecordId == testRecordId).ToList());

    public Task<IReadOnlyList<ReportRecord>> ListRecoverableAsync(string ownerNodeId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ReportRecord>>(Records.Where(r => r.OwnerNodeId == ownerNodeId
            && r.Status is ReportStatus.Pending or ReportStatus.Generating).ToList());

    public Task<bool> TryClaimAsync(string revisionId, string ownerNodeId, ReportStatus expectedStatus,
        string attemptToken, CancellationToken ct = default)
    {
        var record = Records.FirstOrDefault(r => r.RevisionId == revisionId && r.OwnerNodeId == ownerNodeId
            && r.Status == expectedStatus && expectedStatus == ReportStatus.Pending
            && r.ActiveAttemptKey is not null && r.AttemptToken is null);
        if (record is null) return Task.FromResult(false);
        record.Status = ReportStatus.Generating;
        record.AttemptToken = attemptToken;
        record.RowVersion = checked(record.RowVersion + 1);
        return Task.FromResult(true);
    }

    public Task<bool> TryCompleteAsync(ReportRecord result, string attemptToken, string inputHash,
        EvaluationState evaluationState, string? evaluationText, string conclusionSource,
        CancellationToken ct = default)
    {
        var record = Records.FirstOrDefault(r => r.RevisionId == result.RevisionId
            && r.Status == ReportStatus.Generating && r.AttemptToken == attemptToken
            && r.InputHash == inputHash && r.RowVersion == result.RowVersion);
        if (record is null) return Task.FromResult(false);
        record.Status = ReportStatus.Completed;
        record.OutputPath = result.OutputPath;
        record.StorageKey = result.StorageKey;
        record.ManifestHash = result.ManifestHash;
        record.EngineName = result.EngineName;
        record.EngineVersion = result.EngineVersion;
        record.CalculationResultJson = result.CalculationResultJson;
        record.CompletedAtUtc = result.CompletedAtUtc;
        record.ActiveAttemptKey = null;
        record.RowVersion = checked(record.RowVersion + 1);
        return Task.FromResult(true);
    }

    public Task<bool> TryFailAsync(ReportRecord result, string attemptToken, string inputHash,
        CancellationToken ct = default)
    {
        var record = Records.FirstOrDefault(r => r.RevisionId == result.RevisionId
            && r.Status == ReportStatus.Generating && r.AttemptToken == attemptToken
            && r.InputHash == inputHash && r.RowVersion == result.RowVersion);
        if (record is null) return Task.FromResult(false);
        record.Status = ReportStatus.Failed;
        record.Error = result.Error;
        record.ErrorStage = result.ErrorStage;
        record.CompletedAtUtc = result.CompletedAtUtc;
        record.ActiveAttemptKey = null;
        record.RowVersion = checked(record.RowVersion + 1);
        return Task.FromResult(true);
    }
}

public sealed class FakeTestParameterRepository : ITestParameterRepository
{
    public ProjectTestParameter? Project { get; set; }
    public List<ProductTestParameter> ProductParameters { get; } = new();

    public Task<ProjectTestParameter?> GetProjectAsync(CancellationToken ct = default)
        => Task.FromResult(Project);

    public Task<ProductTestParameter?> GetProductAsync(int productTypeId, int productModelId, CancellationToken ct = default)
        => Task.FromResult(ProductParameters.FirstOrDefault(p => p.ProductTypeId == productTypeId && p.ProductModelId == productModelId));

    public Task SaveProjectAsync(ProjectTestParameter value, CancellationToken ct = default)
    {
        Project = value;
        return Task.CompletedTask;
    }

    public Task SaveProductAsync(ProductTestParameter value, CancellationToken ct = default)
    {
        ProductParameters.RemoveAll(p => p.ProductTypeId == value.ProductTypeId && p.ProductModelId == value.ProductModelId);
        ProductParameters.Add(value);
        return Task.CompletedTask;
    }
}
