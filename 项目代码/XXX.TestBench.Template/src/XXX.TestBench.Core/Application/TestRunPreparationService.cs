using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Domain.Products;
using XXX.TestBench.Core.Domain.Records;
using XXX.TestBench.Core.Domain.TestParameters;
using XXX.TestBench.Core.Domain.TestPoints;
using XXX.TestBench.Core.Execution;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Core.Application;

/// <summary>
/// 创建短期准备快照，并在收到稳定开始请求时重新读取关键输入。
/// 本轮只允许纯仿真运行；本服务不执行设备动作，也不签发设备许可。
/// </summary>
public sealed class TestRunPreparationService
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
    {
        WriteIndented = false
    };

    private readonly AuthenticationService _authentication;
    private readonly IUserRepository _users;
    private readonly IProductRepository _products;
    private readonly ITestPointRepository _points;
    private readonly IModelPointConfigRepository _modelPointConfigs;
    private readonly TestParameterService _parameters;
    private readonly DeviceConfigurationService _deviceConfigurations;
    private readonly ITestItemExecutorFactory _executors;
    private readonly IRecordRepository _records;
    private readonly IOperationEvidenceStore _evidence;
    private readonly Func<IDeviceRuntime?> _currentRuntime;
    private readonly string _benchId;
    private readonly string _nodeId;
    private readonly TimeSpan _sessionLifetime;
    private readonly IClock _clock;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<Guid, SessionState> _sessions = new();

    public TestRunPreparationService(
        AuthenticationService authentication,
        IUserRepository users,
        IProductRepository products,
        ITestPointRepository points,
        IModelPointConfigRepository modelPointConfigs,
        TestParameterService parameters,
        DeviceConfigurationService deviceConfigurations,
        ITestItemExecutorFactory executors,
        IRecordRepository records,
        IOperationEvidenceStore evidence,
        Func<IDeviceRuntime?> currentRuntime,
        string benchId,
        string nodeId,
        TimeSpan sessionLifetime,
        IClock clock,
        TimeProvider timeProvider)
    {
        _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _products = products ?? throw new ArgumentNullException(nameof(products));
        _points = points ?? throw new ArgumentNullException(nameof(points));
        _modelPointConfigs = modelPointConfigs ?? throw new ArgumentNullException(nameof(modelPointConfigs));
        _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        _deviceConfigurations = deviceConfigurations ?? throw new ArgumentNullException(nameof(deviceConfigurations));
        _executors = executors ?? throw new ArgumentNullException(nameof(executors));
        _records = records ?? throw new ArgumentNullException(nameof(records));
        _evidence = evidence ?? throw new ArgumentNullException(nameof(evidence));
        _currentRuntime = currentRuntime ?? throw new ArgumentNullException(nameof(currentRuntime));
        _benchId = string.IsNullOrWhiteSpace(benchId) ? throw new ArgumentException("台架身份不能为空。", nameof(benchId)) : benchId.Trim();
        _nodeId = string.IsNullOrWhiteSpace(nodeId) ? throw new ArgumentException("控制节点身份不能为空。", nameof(nodeId)) : nodeId.Trim();
        if (sessionLifetime <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(sessionLifetime), "准备有效期必须大于零。");
        _sessionLifetime = sessionLifetime;
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <summary>
    /// 检查当前登录权限、产品、参数、项点、仿真配置和必需信号，生成短期快照。
    /// </summary>
    public async Task<TestRunPreparationSession> PrepareAsync(
        string sessionToken,
        int productModelId,
        ProductIdentity productIdentity,
        RunScope scope,
        int? selectedPointId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(productIdentity);
        ValidateScope(scope, selectedPointId);
        var actor = await AuthenticateOperatorAsync(sessionToken, cancellationToken);
        await EnsureBenchAvailableAsync(cancellationToken);

        var material = await ReadCurrentMaterialAsync(
            actor.UserId, productModelId, productIdentity, scope, selectedPointId, cancellationToken);
        var preparedAtUtc = _clock.UtcNow;
        if (preparedAtUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("准备时间必须使用 UTC。");
        var expiresAtUtc = preparedAtUtc.Add(_sessionLifetime);
        RemoveExpiredSessions();

        var session = new TestRunPreparationSession(
            Guid.NewGuid(),
            Guid.NewGuid(),
            actor.UserId,
            _benchId,
            _nodeId,
            material.ProductType.Id,
            material.ProductType.Name,
            material.ProductType.RowVersion,
            material.ProductModel.Id,
            material.ProductModel.Name,
            material.ProductModel.RowVersion,
            material.ProductModel.SequenceRevision,
            productIdentity,
            scope,
            selectedPointId,
            Array.AsReadOnly(material.Sequence.ToArray()),
            material.Parameters,
            material.ParameterSnapshotJson,
            material.SequenceSnapshotJson,
            material.VersionVectorJson,
            material.DeviceConfigurationRevision,
            material.SignalBindingsSnapshotJson,
            HashVersionVector(material.VersionVectorJson),
            preparedAtUtc,
            expiresAtUtc);

        _sessions[session.PreparationSessionId] = new SessionState(session, _timeProvider.GetTimestamp());
        return session;
    }

    /// <summary>
    /// 校验准备仍未过期且全部关键输入未变化。调用者须在共享设备操作门内，
    /// 并在释放操作门前提交开始记录和 operation-fence。
    /// </summary>
    public async Task<PreparedTestRun> ValidateStartAsync(
        StartRequest request,
        string sessionToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.StartRequestId == Guid.Empty || request.OperationId == Guid.Empty
            || request.PreparationSessionId == Guid.Empty)
            throw new DomainException("开始请求缺少稳定请求、操作或准备会话标识。");

        if (!_sessions.TryGetValue(request.PreparationSessionId, out var state))
            throw new DomainException("准备会话已失效，请重新进行试验准备。");

        await state.Gate.WaitAsync(cancellationToken);
        try
        {
            var session = state.Session;
            if (IsExpired(state))
            {
                _sessions.TryRemove(session.PreparationSessionId, out _);
                throw new DomainException("准备会话已过期，请重新进行试验准备。");
            }
            if (request.OperationId != session.OperationId
                || !string.Equals(request.ExpectedPreparationHash, session.PreparationHash, StringComparison.Ordinal))
                throw new DomainException("开始请求与已确认的准备内容不一致。");
            if (state.StartRequestId is { } existingRequest && existingRequest != request.StartRequestId)
                throw new DomainException("该准备会话已绑定其他开始请求，请重新进行试验准备。");

            var actor = await AuthenticateOperatorAsync(sessionToken, cancellationToken);
            if (actor.UserId != session.PreparedByUserId)
                throw new AuthorizationException("准备会话与当前登录用户不一致。");
            await EnsureBenchAvailableAsync(cancellationToken);

            var current = await ReadCurrentMaterialAsync(
                actor.UserId,
                session.ProductModelId,
                session.ProductIdentity,
                session.Scope,
                session.SelectedPointId,
                cancellationToken);
            var currentHash = HashVersionVector(current.VersionVectorJson);
            if (!string.Equals(currentHash, session.PreparationHash, StringComparison.Ordinal))
            {
                _sessions.TryRemove(session.PreparationSessionId, out _);
                throw new DomainException("产品、参数、项点或设备配置已变化，请重新进行试验准备。");
            }

            var isReplay = state.StartRequestId is not null;
            state.StartRequestId = request.StartRequestId;
            state.RunId ??= Guid.NewGuid();

            var signalPointIds = new ReadOnlyDictionary<string, string>(
                current.ResolvedSignalPointIds.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.OrdinalIgnoreCase));
            return new PreparedTestRun(session, actor, request.StartRequestId, state.RunId.Value, signalPointIds, isReplay);
        }
        finally
        {
            state.Gate.Release();
        }
    }

    private async Task<PreparationMaterial> ReadCurrentMaterialAsync(
        int actorUserId,
        int productModelId,
        ProductIdentity productIdentity,
        RunScope scope,
        int? selectedPointId,
        CancellationToken cancellationToken)
    {
        if (productModelId <= 0 || !productIdentity.HasAnyValue)
            throw new DomainException("请选择产品型号，并至少填写一个产品标识。");

        var model = await _products.GetModelAsync(productModelId, cancellationToken)
            ?? throw new DomainException("产品型号不存在。");
        var type = await _products.GetTypeAsync(model.ProductTypeId, cancellationToken)
            ?? throw new DomainException("产品类型不存在。");
        if (!model.IsEnabled || !type.IsEnabled)
            throw new DomainException("产品类型或型号已停用，不能进行试验准备。");
        if (model.RowVersion < 0 || model.SequenceRevision < 0 || type.RowVersion < 0)
            throw new DomainException("产品类型或型号版本无效。");

        var modelConfigs = await _modelPointConfigs.ListByModelAsync(model.Id, cancellationToken);
        var allPoints = await _points.ListByTypeAsync(type.Id, includeDisabled: true, cancellationToken);
        if (allPoints.Select(point => point.Id).Distinct().Count() != allPoints.Count)
            throw new DomainException("产品类型存在重复试验项点编号，不能进行试验准备。");
        var pointById = allPoints.ToDictionary(point => point.Id);
        if (modelConfigs.Any(config => config.ProductModelId != model.Id)
            || modelConfigs.Select(config => config.TestItemPointId).Distinct().Count() != modelConfigs.Count
            || modelConfigs.Select(config => config.SortOrder).Distinct().Count() != modelConfigs.Count)
            throw new DomainException("型号项点配置存在重复或身份不一致，不能开始准备。");

        var orderedConfigs = modelConfigs.OrderBy(config => config.SortOrder).ToArray();
        for (var index = 0; index < orderedConfigs.Length; index++)
        {
            if (orderedConfigs[index].SortOrder != index + 1)
                throw new DomainException("型号项点顺序不连续，请先重新保存项点配置。");
        }

        var configuredPoints = new List<(ModelPointConfig Config, TestItemPoint Point)>();
        foreach (var config in orderedConfigs)
        {
            if (!pointById.TryGetValue(config.TestItemPointId, out var point)
                || point.ProductTypeId != type.Id)
                throw new DomainException("型号项点配置引用了不存在或所属类型不匹配的项点。");
            if (point.RowVersion < 0)
                throw new DomainException($"试验项点“{point.Name}”版本无效。");
            configuredPoints.Add((config, point));
        }

        var activePoints = configuredPoints.Where(item => item.Point.IsEnabled).ToArray();
        if (activePoints.Length == 0)
            throw new DomainException("当前型号没有已启用的试验项点。");

        var selectedPoints = scope switch
        {
            RunScope.Auto => activePoints,
            RunScope.Single => activePoints
                .Where(item => item.Point.Id == selectedPointId)
                .Take(1)
                .ToArray(),
            _ => throw new DomainException("不支持的试验范围。")
        };
        if (selectedPoints.Length == 0)
            throw new DomainException("单项执行必须选择当前型号中已启用的项点。");

        var sequence = selectedPoints.Select(item =>
        {
            var executor = _executors.Get(item.Point.ExecutorCode);
            if (executor.OutputContractVersion <= 0)
                throw new DomainException($"项点“{item.Point.Name}”的输出合同版本无效。");
            return new SequenceItem(
                item.Point.Id,
                item.Point.Name,
                item.Point.ExecutorCode,
                item.Point.ResultKind,
                item.Config.SortOrder,
                item.Point.RowVersion,
                executor.OutputContractVersion);
        }).ToArray();

        var parameters = await _parameters.LoadEffectiveAsync(model.Id, cancellationToken);
        if (parameters.ProductTypeId != type.Id || parameters.ProductModelId != model.Id)
            throw new DomainException("当前参数快照与选定产品型号不一致。");

        var deviceConfiguration = await _deviceConfigurations.LoadActiveAsync(cancellationToken);
        var runtime = _currentRuntime() ?? throw new DomainException("设备运行时尚未就绪。");
        if (runtime.Mode != DeviceMode.Simulation || !runtime.IsSimulation)
            throw new DomainException("当前准备流程只允许纯仿真模式；硬件和混合模式须完成项目安全许可后再接入。");
        if (string.IsNullOrWhiteSpace(deviceConfiguration.Revision)
            || !string.Equals(deviceConfiguration.Revision, runtime.ActiveRevision, StringComparison.Ordinal))
            throw new DomainException("设备配置版本与当前仿真运行时不一致。");
        if (deviceConfiguration.SignalBindings is null
            || deviceConfiguration.SignalBindings.SchemaVersion != SignalBindingsConfig.CurrentSchemaVersion)
            throw new DomainException("业务信号绑定配置缺失或版本不受支持。");

        var runtimePoints = await runtime.ListPointsAsync(cancellationToken);
        var pointIds = runtimePoints.Select(point => point.PointId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var bindingIssues = deviceConfiguration.SignalBindings.Validate(pointIds);
        if (bindingIssues.Count > 0)
            throw new DomainException("业务信号绑定无效：" + string.Join("；", bindingIssues.Select(issue => issue.Message)));

        var requirements = sequence
            .SelectMany(item => _executors.Get(item.ExecutorCode).RequiredSignals)
            .ToArray();
        var resolver = new SignalResolver(
            runtime,
            deviceConfiguration.SignalBindings.Bindings,
            () => _clock.UtcNow);
        var resolvedSignals = await resolver.ResolveAsync(requirements, cancellationToken);

        var targetStatuses = resolvedSignals.Values
            .Select(signal => runtime.GetDeviceStatus(signal.Point.DeviceId))
            .GroupBy(status => status.DeviceId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(status => status.DeviceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var status in targetStatuses)
        {
            if (!status.IsSimulation || status.Mode != DeviceMode.Simulation
                || !status.IsConnected || status.Health != DeviceHealth.Healthy
                || status.ConnectionGeneration <= 0
                || !string.Equals(status.Revision, deviceConfiguration.Revision, StringComparison.Ordinal))
                throw new DomainException($"仿真设备“{status.Name}”未处于当前配置版本的可用状态。");
        }

        var parameterSnapshotJson = TestParameterSnapshot.ToJson(parameters);
        var sequenceSnapshotJson = SequenceSnapshot.ToJson(sequence);
        var bindingEntries = NormalizeBindings(deviceConfiguration.SignalBindings);
        var signalBindingsSnapshotJson = JsonSerializer.Serialize(
            new SignalBindingsConfig
            {
                SchemaVersion = SignalBindingsConfig.CurrentSchemaVersion,
                Bindings = bindingEntries.ToDictionary(
                    entry => entry.SignalKey,
                    entry => entry.PointId,
                    StringComparer.OrdinalIgnoreCase)
            },
            SnapshotJsonOptions);
        var versionVectorJson = CreateVersionVectorJson(
            actorUserId,
            _benchId,
            _nodeId,
            type,
            model,
            orderedConfigs,
            allPoints,
            productIdentity,
            scope,
            selectedPointId,
            sequence,
            parameters,
            parameterSnapshotJson,
            sequenceSnapshotJson,
            deviceConfiguration.Revision,
            bindingEntries,
            resolvedSignals,
            targetStatuses);
        var resolvedPointIds = resolvedSignals.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.PointId,
            StringComparer.OrdinalIgnoreCase);

        return new PreparationMaterial(
            type,
            model,
            sequence,
            parameters,
            parameterSnapshotJson,
            sequenceSnapshotJson,
            versionVectorJson,
            deviceConfiguration.Revision,
            signalBindingsSnapshotJson,
            resolvedPointIds);
    }

    private async Task<UserContext> AuthenticateOperatorAsync(string sessionToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sessionToken))
            throw new AuthorizationException("当前登录会话无效。");
        var actor = await _authentication.BuildUserContextAsync(sessionToken, cancellationToken);
        var user = await _users.GetByIdAsync(actor.UserId, cancellationToken);
        if (user is null || !user.IsEnabled)
            throw new AuthorizationException("当前账号已停用或不存在，不能进行试验操作。");
        if (user.MustChangePassword)
            throw new AuthorizationException("请先修改初始密码，再进行试验操作。");
        actor.EnsurePermission(PermissionCode.ExecuteTests);
        return actor;
    }

    private async Task EnsureBenchAvailableAsync(CancellationToken cancellationToken)
    {
        if (await _records.GetActiveRunningRecordAsync(_benchId, cancellationToken) is not null)
            throw new DomainException("当前台架已有未结束的试验记录，不能创建准备会话。");

        var fences = await _evidence.ReadAllAsync(cancellationToken);
        if (fences.Any(fence => string.Equals(fence.BenchId, _benchId, StringComparison.Ordinal)))
            throw new DomainException("当前台架存在未闭合操作标记，必须先完成恢复处理。");
    }

    private static IReadOnlyList<BindingFingerprint> NormalizeBindings(SignalBindingsConfig bindings)
    {
        if (bindings.Bindings is null)
            throw new DomainException("业务信号绑定列表不能为空。");

        var entries = new List<BindingFingerprint>();
        foreach (var pair in bindings.Bindings)
        {
            var key = pair.Key?.Trim() ?? string.Empty;
            var value = pair.Value?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(key) || !Guid.TryParse(value, out var pointId))
                throw new DomainException("业务信号绑定包含空键或无效 PointId。");
            entries.Add(new BindingFingerprint(key.ToUpperInvariant(), pointId.ToString("D").ToLowerInvariant()));
        }
        if (entries.Select(entry => entry.SignalKey).Distinct(StringComparer.Ordinal).Count() != entries.Count)
            throw new DomainException("业务信号绑定包含重复 SignalKey。");
        return entries.OrderBy(entry => entry.SignalKey, StringComparer.Ordinal).ToArray();
    }

    private static string CreateVersionVectorJson(
        int actorUserId,
        string benchId,
        string nodeId,
        ProductType type,
        ProductModel model,
        IReadOnlyList<ModelPointConfig> modelConfigs,
        IReadOnlyList<TestItemPoint> allPoints,
        ProductIdentity productIdentity,
        RunScope scope,
        int? selectedPointId,
        IReadOnlyList<SequenceItem> sequence,
        EffectiveTestParameters parameters,
        string parameterSnapshotJson,
        string sequenceSnapshotJson,
        string deviceConfigurationRevision,
        IReadOnlyList<BindingFingerprint> bindings,
        IReadOnlyDictionary<string, ResolvedSignal> resolvedSignals,
        IReadOnlyList<DeviceRuntimeInfo> targetStatuses)
    {
        var fingerprint = new
        {
            SchemaVersion = 1,
            ActorUserId = actorUserId,
            BenchId = benchId,
            NodeId = nodeId,
            ProductType = new { type.Id, type.Name, type.IsEnabled, type.RowVersion },
            ProductModel = new { model.Id, model.ProductTypeId, model.Name, model.IsEnabled, model.RowVersion, model.SequenceRevision },
            ModelPointConfigs = modelConfigs.OrderBy(config => config.SortOrder)
                .Select(config => new { config.ProductModelId, config.TestItemPointId, config.SortOrder }).ToArray(),
            ProductTypePoints = allPoints.OrderBy(point => point.Id)
                .Select(point => new
                {
                    point.Id, point.ProductTypeId, point.Name, point.ExecutorCode,
                    point.ResultKind, point.IsEnabled, point.SortOrder, point.RowVersion
                }).ToArray(),
            ProductIdentity = productIdentity,
            Scope = (int)scope,
            SelectedPointId = selectedPointId,
            Sequence = sequence,
            Parameters = parameters,
            ParameterSnapshotJson = parameterSnapshotJson,
            SequenceSnapshotJson = sequenceSnapshotJson,
            DeviceConfigurationRevision = deviceConfigurationRevision,
            SignalBindings = bindings,
            ResolvedSignals = resolvedSignals.Values
                .OrderBy(signal => signal.SignalKey, StringComparer.OrdinalIgnoreCase)
                .Select(signal => new
                {
                    signal.SignalKey,
                    signal.PointId,
                    signal.Point.DeviceId,
                    signal.Point.Revision,
                    signal.Point.DataType,
                    signal.Requirement.Access,
                    signal.Requirement.ExpectedDataType,
                    signal.Requirement.Unit,
                    MaxSampleAgeTicks = signal.Requirement.MaxSampleAge.Ticks
                }).ToArray(),
            DeviceSessions = targetStatuses.Select(status => new
            {
                status.DeviceId,
                status.Revision,
                status.ConnectionGeneration,
                status.IsSimulation,
                status.Mode,
                status.Health,
                status.IsConnected,
                status.ConnectionState
            }).ToArray()
        };
        return JsonSerializer.Serialize(fingerprint, SnapshotJsonOptions);
    }

    private bool IsExpired(SessionState state)
        => _timeProvider.GetElapsedTime(state.CreatedTimestamp) >= _sessionLifetime;

    private void RemoveExpiredSessions()
    {
        foreach (var pair in _sessions)
            if (IsExpired(pair.Value))
                _sessions.TryRemove(pair.Key, out _);
    }

    private static void ValidateScope(RunScope scope, int? selectedPointId)
    {
        if (!Enum.IsDefined(scope))
            throw new DomainException("不支持的试验范围。");
        if (scope == RunScope.Single && selectedPointId is not > 0)
            throw new DomainException("单项执行必须选择一个有效项点。");
        if (scope == RunScope.Auto && selectedPointId is not null)
            throw new DomainException("自动执行不能同时指定单个项点。");
    }

    private static string HashVersionVector(string versionVectorJson)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(versionVectorJson))).ToLowerInvariant();

    private sealed class SessionState(TestRunPreparationSession session, long createdTimestamp)
    {
        public TestRunPreparationSession Session { get; } = session;
        public long CreatedTimestamp { get; } = createdTimestamp;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public Guid? StartRequestId { get; set; }
        public Guid? RunId { get; set; }
    }

    private sealed record PreparationMaterial(
        ProductType ProductType,
        ProductModel ProductModel,
        IReadOnlyList<SequenceItem> Sequence,
        EffectiveTestParameters Parameters,
        string ParameterSnapshotJson,
        string SequenceSnapshotJson,
        string VersionVectorJson,
        string DeviceConfigurationRevision,
        string SignalBindingsSnapshotJson,
        IReadOnlyDictionary<string, string> ResolvedSignalPointIds);

    private sealed record BindingFingerprint(string SignalKey, string PointId);
}
