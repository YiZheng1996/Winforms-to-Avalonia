using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Domain.Products;
using XXX.TestBench.Core.Domain.Recipes;
using XXX.TestBench.Core.Domain.Tasks;
using XXX.TestBench.Core.Domain.TestDefinitions;
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

    public Task<User?> GetByLoginNameAsync(string loginName, CancellationToken ct = default)
        => Task.FromResult(Users.FirstOrDefault(u => u.LoginName == loginName));

    public Task<User?> GetByIdAsync(int id, CancellationToken ct = default)
        => Task.FromResult(Users.FirstOrDefault(u => u.Id == id));

    public Task<Role?> GetRoleAsync(int roleId, CancellationToken ct = default)
        => Task.FromResult(Roles.FirstOrDefault(r => r.Id == roleId));

    public Task AddAsync(User user, CancellationToken ct = default)
    {
        if (user.Id == 0) user.Id = _nextId++;
        Users.Add(user);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(User user, CancellationToken ct = default) => Task.CompletedTask;
}

public sealed class FakeProductRepository : IProductRepository
{
    public List<ProductType> Types { get; } = new();
    public List<ProductModel> Models { get; } = new();
    private int _nextTypeId = 1;
    private int _nextModelId = 1;
    public Task<ProductType?> GetTypeByCodeAsync(string code, CancellationToken ct = default)
        => Task.FromResult(Types.FirstOrDefault(t => t.Code == code));

    public Task<ProductType?> GetTypeAsync(int id, CancellationToken ct = default)
        => Task.FromResult(Types.FirstOrDefault(t => t.Id == id));

    public Task<ProductModel?> GetModelAsync(int id, CancellationToken ct = default)
        => Task.FromResult(Models.FirstOrDefault(m => m.Id == id));

    public Task<ProductModel?> GetModelByCodeAsync(int productTypeId, string code, CancellationToken ct = default)
        => Task.FromResult(Models.FirstOrDefault(m => m.ProductTypeId == productTypeId && m.Code == code));

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
}

public sealed class FakeTestDefinitionRepository : ITestDefinitionRepository
{
    public List<TestItemDefinition> Items { get; } = new();
    public List<ParameterDefinition> Parameters { get; } = new();
    private int _nextId = 1;

    public Task<TestItemDefinition?> GetItemAsync(int id, CancellationToken ct = default)
        => Task.FromResult(Items.FirstOrDefault(i => i.Id == id));

    public Task<IReadOnlyList<TestItemDefinition>> ListItemsAsync(bool includeDisabled, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TestItemDefinition>>(includeDisabled ? Items : Items.Where(i => i.IsEnabled).ToList());

    public Task<IReadOnlyList<ParameterDefinition>> ListParametersAsync(int itemId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ParameterDefinition>>(Parameters.Where(p => p.TestItemDefinitionId == itemId).OrderBy(p => p.SortOrder).ToList());

    public Task<ParameterDefinition?> GetParameterAsync(int id, CancellationToken ct = default)
        => Task.FromResult(Parameters.FirstOrDefault(p => p.Id == id));

    public Task AddItemAsync(TestItemDefinition item, CancellationToken ct = default)
    {
        if (item.Id == 0) item.Id = NextId();
        Items.Add(item);
        return Task.CompletedTask;
    }

    public Task AddParameterAsync(ParameterDefinition parameter, CancellationToken ct = default)
    {
        if (parameter.Id == 0) parameter.Id = NextId();
        Parameters.Add(parameter);
        return Task.CompletedTask;
    }

    public int NextId() => _nextId++;
}

public sealed class FakeRecipeRepository : IRecipeRepository
{
    public List<RecipeVersion> Versions { get; } = new();
    public List<RecipeItem> Items { get; } = new();
    public List<RecipeParameterValue> Values { get; } = new();
    private int _nextVersionId = 1;
    private int _nextItemId = 1;
    private int _nextValueId = 1;
    public Task<RecipeVersion?> GetAsync(int id, CancellationToken ct = default)
        => Task.FromResult(Versions.FirstOrDefault(v => v.Id == id));

    public Task<RecipeVersion?> GetLatestAsync(int productModelId, CancellationToken ct = default)
        => Task.FromResult(Versions.Where(v => v.ProductModelId == productModelId).OrderByDescending(v => v.Version).FirstOrDefault());

    public Task<IReadOnlyList<RecipeVersion>> ListByModelAsync(int productModelId, bool includeRetired, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<RecipeVersion>>(Versions.Where(v => v.ProductModelId == productModelId && (includeRetired || v.Status != RecipeStatus.Retired)).ToList());

    public Task<RecipeVersion?> GetPublishedByModelAsync(int productModelId, CancellationToken ct = default)
        => Task.FromResult(Versions.Where(v => v.ProductModelId == productModelId && v.Status == RecipeStatus.Published).OrderByDescending(v => v.Version).FirstOrDefault());

    public Task<IReadOnlyList<RecipeItem>> ListItemsAsync(int recipeVersionId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<RecipeItem>>(Items.Where(i => i.RecipeVersionId == recipeVersionId).OrderBy(i => i.SortOrder).ToList());

    public Task<IReadOnlyList<RecipeParameterValue>> ListParameterValuesAsync(int recipeVersionId, CancellationToken ct = default)
    {
        var itemIds = Items.Where(i => i.RecipeVersionId == recipeVersionId).Select(i => i.Id).ToHashSet();
        return Task.FromResult<IReadOnlyList<RecipeParameterValue>>(Values.Where(v => itemIds.Contains(v.RecipeItemId)).ToList());
    }

    public Task AddAsync(RecipeVersion recipe, CancellationToken ct = default)
    {
        if (recipe.Id == 0) recipe.Id = _nextVersionId++;
        Versions.Add(recipe);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(RecipeVersion recipe, CancellationToken ct = default) => Task.CompletedTask;

    public Task AddItemAsync(RecipeItem item, CancellationToken ct = default)
    {
        if (item.Id == 0) item.Id = _nextItemId++;
        Items.Add(item);
        return Task.CompletedTask;
    }

    public Task AddParameterValueAsync(RecipeParameterValue value, CancellationToken ct = default)
    {
        if (value.Id == 0) value.Id = _nextValueId++;
        Values.Add(value);
        return Task.CompletedTask;
    }
}

public sealed class FakeTaskRepository : ITaskRepository
{
    public List<TestTask> Tasks { get; } = new();
    public List<TestRecord> Records { get; } = new();
    public List<TestItemResult> Results { get; } = new();
    private int _nextId = 1;

    public Task<TestTask?> GetAsync(int id, CancellationToken ct = default)
        => Task.FromResult(Tasks.FirstOrDefault(t => t.Id == id));

    public Task<bool> ExistsTaskNumberAsync(string taskNumber, CancellationToken ct = default)
        => Task.FromResult(Tasks.Any(t => t.TaskNumber == taskNumber));

    public Task<TestTask?> GetActiveRunningAsync(CancellationToken ct = default)
        => Task.FromResult(Tasks.FirstOrDefault(t => t.State == TaskState.Running));

    public Task AddAsync(TestTask task, CancellationToken ct = default)
    {
        if (task.Id == 0) task.Id = _nextId++;
        Tasks.Add(task);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(TestTask task, CancellationToken ct = default) => Task.CompletedTask;

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
}

public sealed class FakeRuntime : IDeviceRuntime
{
    private readonly DeviceRuntimeInfo _status;
    public List<string> Writes { get; } = new();

    public FakeRuntime(bool isSimulation, DeviceHealth health = DeviceHealth.Healthy, bool connected = true)
    {
        IsSimulation = isSimulation;
        _status = new DeviceRuntimeInfo(Name, "Fake", "fake://1", isSimulation, health, connected, null);
    }

    public string Name => "Fake";
    public DeviceMode Mode => IsSimulation ? DeviceMode.Simulation : DeviceMode.Hardware;
    public bool IsSimulation { get; }
    public DeviceRuntimeInfo Status => _status;
    public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    public Task<IReadOnlyList<DevicePoint>> ListPointsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<DevicePoint>>(Array.Empty<DevicePoint>());
    public Task<PointValue> ReadAsync(DevicePoint point, CancellationToken ct = default)
        => Task.FromResult(new PointValue(point.Code, point.Address, PointQuality.Good, null, DateTime.UtcNow));
    public Task<PointValue> WriteAsync(DevicePoint point, object? value, CancellationToken ct = default)
    {
        Writes.Add($"{point.Code}={value}");
        return Task.FromResult(new PointValue(point.Code, point.Address, PointQuality.Good, value, DateTime.UtcNow));
    }
}

public sealed class FakeRuntimeFactory : IDeviceRuntimeFactory
{
    public Task<IDeviceRuntime> CreateAsync(DeviceMode mode, CancellationToken ct = default)
    {
        if (mode == DeviceMode.Simulation) return Task.FromResult<IDeviceRuntime>(new FakeRuntime(true));
        throw new DomainException("Hardware 无适配器");
    }
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
