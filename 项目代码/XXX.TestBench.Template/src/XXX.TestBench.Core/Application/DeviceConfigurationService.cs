using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Execution;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Core.Application;

/// <summary>
/// 完整设备配置候选的校验、版本存储和应用入口。
/// </summary>
public sealed class DeviceConfigurationService
{
    private readonly IDeviceConfigurationStore _store;
    private readonly DeviceOperationCoordinator _operations;
    private readonly IAuditLog _audit;
    private readonly IEnumerable<IDeviceDriverDescriptor> _drivers;
    private readonly IEnumerable<RequiredSignal> _requiredSignals;
    private readonly Func<bool> _hasActiveRun;
    private readonly Func<DeviceConfigurationSnapshot, CancellationToken, Task<IDeviceRuntime>>? _runtimeFactory;
    private readonly Func<IDeviceRuntime?>? _currentRuntime;
    private readonly Func<IDeviceRuntime, Task>? _publishRuntime;

    public DeviceConfigurationService(
        IDeviceConfigurationStore store,
        DeviceOperationCoordinator operations,
        IAuditLog audit,
        IEnumerable<IDeviceDriverDescriptor>? drivers = null,
        Func<bool>? hasActiveRun = null,
        Func<DeviceConfigurationSnapshot, CancellationToken, Task<IDeviceRuntime>>? runtimeFactory = null,
        Func<IDeviceRuntime?>? currentRuntime = null,
        Func<IDeviceRuntime, Task>? publishRuntime = null,
        IEnumerable<RequiredSignal>? requiredSignals = null)
    {
        _store = store;
        _operations = operations;
        _audit = audit;
        _drivers = drivers ?? Array.Empty<IDeviceDriverDescriptor>();
        _requiredSignals = requiredSignals ?? Array.Empty<RequiredSignal>();
        _hasActiveRun = hasActiveRun ?? (() => false);
        _runtimeFactory = runtimeFactory;
        _currentRuntime = currentRuntime;
        _publishRuntime = publishRuntime;
    }

    public IReadOnlyList<ConfigurationIssue> Validate(DeviceConfigurationSnapshot snapshot)
        => MultiDeviceConfigurationValidator.Validate(snapshot, _drivers, _requiredSignals);

    /// <summary>
    /// 从当前 active 快照构造只修改设备/通道的候选，并通过同一配置应用事务生效。
    /// 点位、仿真规则和业务信号绑定沿用当前生效版本，避免 UI 层分别保存配置文件。
    /// </summary>
    public async Task<DeviceConfigurationApplyResult> ApplyDeviceConfigurationAsync(
        UserContext actor,
        DeviceConfig device,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        var current = await _store.LoadActiveAsync(ct);
        var points = EnsureDeviceGroups(current.Points, device);
        var candidate = new DeviceConfigurationSnapshot
        {
            Revision = "device-" + Guid.NewGuid().ToString("N"),
            Device = device,
            Points = points,
            Simulation = current.Simulation,
            SignalBindings = current.SignalBindings
        };
        return await ApplyAsync(actor, candidate, ct);
    }

    /// <summary>
    /// 从当前 active 快照构造只修改点位的候选，并通过同一配置应用事务生效。
    /// </summary>
    public async Task<DeviceConfigurationApplyResult> ApplyPointsAsync(
        UserContext actor,
        IReadOnlyList<PointsConfig.PointEntry> points,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(points);
        var current = await _store.LoadActiveAsync(ct);
        return await ApplyPointConfigurationAsync(actor, current, points,
            current.Points.Groups ?? new List<PointsConfig.PointGroupEntry>(), ct);
    }

    /// <summary>
    /// 从当前 active 快照构造点位和分组候选，并通过同一配置应用事务生效。
    /// </summary>
    public async Task<DeviceConfigurationApplyResult> ApplyPointsAsync(
        UserContext actor,
        IReadOnlyList<PointsConfig.PointEntry> points,
        IReadOnlyList<PointsConfig.PointGroupEntry> groups,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(groups);
        var current = await _store.LoadActiveAsync(ct);
        return await ApplyPointConfigurationAsync(actor, current, points, groups, ct);
    }

    /// <summary>
    /// 新增点位分组。分组写入仍然沿用完整快照事务，不单独修改 points.json。
    /// </summary>
    public async Task<DeviceConfigurationApplyResult> AddPointGroupAsync(
        UserContext actor,
        PointsConfig.PointGroupEntry group,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(group);
        var current = await _store.LoadActiveAsync(ct);
        var groups = CloneGroups(current.Points.Groups);
        groups.Add(CloneGroup(group));
        return await ApplyPointConfigurationAsync(actor, current, current.Points.Points, groups, ct);
    }

    /// <summary>
    /// 修改点位分组的显示属性，保持分组 Id 稳定。
    /// </summary>
    public async Task<DeviceConfigurationApplyResult> UpdatePointGroupAsync(
        UserContext actor,
        PointsConfig.PointGroupEntry group,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(group);
        var current = await _store.LoadActiveAsync(ct);
        var groups = CloneGroups(current.Points.Groups);
        var index = groups.FindIndex(item => string.Equals(item.Id, group.Id, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return Failed("找不到要修改的点位分组", group.Id);
        groups[index] = CloneGroup(group);
        return await ApplyPointConfigurationAsync(actor, current, current.Points.Points, groups, ct);
    }

    /// <summary>
    /// 删除点位分组。仍有点位引用时拒绝删除，避免产生孤立点位。
    /// </summary>
    public async Task<DeviceConfigurationApplyResult> DeletePointGroupAsync(
        UserContext actor,
        string groupId,
        CancellationToken ct = default)
    {
        var current = await _store.LoadActiveAsync(ct);
        var currentGroups = current.Points.Groups ?? new List<PointsConfig.PointGroupEntry>();
        var group = currentGroups.FirstOrDefault(item =>
            string.Equals(item.Id, groupId, StringComparison.OrdinalIgnoreCase));
        if (group is null)
            return Failed("找不到要删除的点位分组", groupId);
        if (current.Points.Points.Any(point =>
                string.Equals(point.GroupId, group.Id, StringComparison.OrdinalIgnoreCase)))
            return Failed($"分组“{group.Name}”仍被点位引用，请先移动或删除点位", groupId);
        if (currentGroups.Count(item =>
                string.Equals(item.DeviceId, group.DeviceId, StringComparison.OrdinalIgnoreCase)) <= 1)
            return Failed("每个设备至少需要保留一个点位分组", groupId);

        var groups = CloneGroups(current.Points.Groups);
        groups.RemoveAll(item => string.Equals(item.Id, groupId, StringComparison.OrdinalIgnoreCase));
        return await ApplyPointConfigurationAsync(actor, current, current.Points.Points, groups, ct);
    }

    private async Task<DeviceConfigurationApplyResult> ApplyPointConfigurationAsync(
        UserContext actor,
        DeviceConfigurationSnapshot current,
        IReadOnlyList<PointsConfig.PointEntry> points,
        IReadOnlyList<PointsConfig.PointGroupEntry> groups,
        CancellationToken ct)
    {
        var candidate = new DeviceConfigurationSnapshot
        {
            Revision = "points-" + Guid.NewGuid().ToString("N"),
            Device = current.Device,
            Points = new PointsConfig
            {
                SchemaVersion = PointsConfig.CurrentSchemaVersion,
                Groups = groups.Select(CloneGroup).ToList(),
                Points = points.Select(point => NormalizePoint(point, current.Device)).ToList()
            },
            Simulation = current.Simulation,
            SignalBindings = current.SignalBindings
        };
        return await ApplyAsync(actor, candidate, ct);
    }

    /// <summary>
    /// 只修改项目级 SignalKey → PointId 绑定，并通过完整配置事务生效。
    /// </summary>
    public async Task<DeviceConfigurationApplyResult> ApplySignalBindingsAsync(
        UserContext actor,
        SignalBindingsConfig bindings,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        var current = await _store.LoadActiveAsync(ct);
        var candidate = new DeviceConfigurationSnapshot
        {
            Revision = "bindings-" + Guid.NewGuid().ToString("N"),
            Device = current.Device,
            Points = current.Points,
            Simulation = current.Simulation,
            SignalBindings = new SignalBindingsConfig
            {
                SchemaVersion = bindings.SchemaVersion,
                Bindings = new Dictionary<string, string>(
                    bindings.Bindings ?? new Dictionary<string, string>(),
                    StringComparer.OrdinalIgnoreCase)
            }
        };
        return await ApplyAsync(actor, candidate, ct);
    }

    /// <summary>
    /// 将候选快照作为一个版本写入并切换生效指针；没有运行时切换委托时只执行存储应用。
    /// </summary>
    public async Task<DeviceConfigurationApplyResult> ApplyAsync(
        UserContext actor,
        DeviceConfigurationSnapshot snapshot,
        CancellationToken ct = default)
    {
        Ensure(actor, PermissionCode.ManageDevices);
        var issues = Validate(snapshot);
        if (issues.Count > 0)
            return new DeviceConfigurationApplyResult(false, snapshot.Revision, string.Join("；", issues));

        await using var lease = await _operations.EnterConfigurationAsync(ct);
        if (_hasActiveRun())
            return new DeviceConfigurationApplyResult(false, snapshot.Revision, "存在活动试验，禁止应用设备配置");
        if (!(snapshot.Device.Devices ?? new List<DeviceConfig.DeviceEntry>()).Any(device => device is not null && device.Enabled))
            return new DeviceConfigurationApplyResult(false, snapshot.Revision, "当前候选没有启用设备，只能保存草稿，不能应用");
        if (_runtimeFactory is not null && _publishRuntime is null)
            return new DeviceConfigurationApplyResult(false, snapshot.Revision, "设备运行时切换未配置发布回调，不能应用");

        IDeviceRuntime? oldRuntime = _currentRuntime?.Invoke();
        IDeviceRuntime? candidateRuntime = null;
        var oldRevision = oldRuntime?.ActiveRevision;
        var oldRuntimeStopped = false;
        var runtimePublished = false;
        try
        {
            await _store.StageAsync(snapshot, ct);
            if (_runtimeFactory is not null)
            {
                if (oldRuntime is not null)
                {
                    await oldRuntime.StopAsync(ct);
                    oldRuntimeStopped = true;
                }
                candidateRuntime = await _runtimeFactory(snapshot, ct);
                await candidateRuntime.StartAsync(ct);
            }

            await _store.CommitActiveAsync(snapshot.Revision, ct);
            if (candidateRuntime is not null)
            {
                // 构造前已检查发布回调；这里保留防御性判断，避免未来组合根改动后出现半切换。
                if (_publishRuntime is null)
                    throw new InvalidOperationException("已创建新的设备运行时，但没有配置发布回调");
                await _publishRuntime(candidateRuntime);
                runtimePublished = true;
                candidateRuntime = null;
            }

            if (oldRuntimeStopped && oldRuntime is not null)
            {
                // 新运行时已经发布后，旧运行时释放失败不应把已经生效的配置回滚成未知状态。
                try { await oldRuntime.DisposeAsync(); } catch { }
            }
            await WriteAuditSafeAsync(actor.LoginName, "DeviceConfigurationApplied", snapshot.Revision, null, ct);
            return new DeviceConfigurationApplyResult(true, snapshot.Revision, null, snapshot);
        }
        catch (Exception ex)
        {
            // 发布完成后，外部运行时已经可能持有新实例，不能再伪装成完整回滚。
            if (runtimePublished)
            {
                await WriteAuditSafeAsync(actor.LoginName, "DeviceConfigurationApplyWarning", snapshot.Revision,
                    "新运行时已发布，但后续步骤失败：" + ex.Message, CancellationToken.None);
                return new DeviceConfigurationApplyResult(true, snapshot.Revision,
                    "配置已生效，但收尾步骤出现问题：" + ex.Message, snapshot);
            }

            if (candidateRuntime is not null)
            {
                try { await candidateRuntime.StopAsync(CancellationToken.None); } catch { }
                try { await candidateRuntime.DisposeAsync(); } catch { }
            }
            if (oldRuntimeStopped && oldRuntime is not null)
            {
                try { await oldRuntime.StartAsync(CancellationToken.None); } catch { }
                if (!string.IsNullOrWhiteSpace(oldRevision))
                {
                    try { await _store.CommitActiveAsync(oldRevision, CancellationToken.None); } catch { }
                }
            }
            await WriteAuditSafeAsync(actor.LoginName, "DeviceConfigurationApplyFailed", snapshot.Revision, ex.Message, ct);
            return new DeviceConfigurationApplyResult(false, snapshot.Revision, ex.Message);
        }
    }

    private async Task WriteAuditSafeAsync(
        string actor,
        string action,
        string target,
        string? detail,
        CancellationToken ct)
    {
        try { await _audit.WriteAsync(actor, action, target, detail, ct); }
        catch { /* 配置切换结果不能因审计后端异常被误报为未生效。 */ }
    }

    private void Ensure(UserContext actor, PermissionCode permission)
    {
        try { actor.EnsurePermission(permission); }
        catch (AuthorizationException)
        {
            _ = _audit.WriteAsync(actor.LoginName, "AccessDenied", permission.ToString(), null);
            throw;
        }
    }

    private static PointsConfig.PointEntry NormalizePoint(
        PointsConfig.PointEntry source,
        DeviceConfig deviceConfig)
    {
        var devices = (deviceConfig.Devices ?? new List<DeviceConfig.DeviceEntry>())
            .Where(device => !string.IsNullOrWhiteSpace(device.Id))
            .ToList();
        var device = !string.IsNullOrWhiteSpace(source.DeviceId)
            ? devices.FirstOrDefault(item => string.Equals(item.Id, source.DeviceId, StringComparison.OrdinalIgnoreCase))
            : devices.FirstOrDefault(item => string.Equals(item.Code, source.DeviceCode, StringComparison.OrdinalIgnoreCase));
        if (device is null && devices.Count == 1 && string.IsNullOrWhiteSpace(source.DeviceId))
            device = devices[0];

        var protocol = source.Protocol;
        if (string.IsNullOrWhiteSpace(protocol) && device is not null
            && DevicePointTypeCatalog.TryParseDriverKey(device.DriverKey, out var parsedProtocol))
            protocol = DevicePointTypeCatalog.ToStorage(parsedProtocol);
        var rawDataType = string.IsNullOrWhiteSpace(source.RawDataType) ? source.DataType : source.RawDataType;
        return new PointsConfig.PointEntry
        {
            Id = string.IsNullOrWhiteSpace(source.Id)
                ? DeviceConfigurationMigrator.StableId($"point|{source.DeviceCode}|{source.DeviceId}|{source.Code}|{source.Address}")
                : source.Id.Trim(),
            Code = source.Code.Trim(),
            Name = string.IsNullOrWhiteSpace(source.Name) ? source.Code.Trim() : source.Name.Trim(),
            Protocol = protocol?.Trim() ?? string.Empty,
            DeviceId = device?.Id.Trim() ?? source.DeviceId.Trim(),
            DeviceCode = device?.Code.Trim() ?? source.DeviceCode.Trim(),
            GroupId = source.GroupId?.Trim() ?? string.Empty,
            GroupCode = source.GroupCode?.Trim() ?? string.Empty,
            Address = source.Address.Trim(),
            AddressDefinition = source.AddressDefinition,
            DataType = string.IsNullOrWhiteSpace(source.DataType) ? rawDataType.Trim() : source.DataType.Trim(),
            RawDataType = rawDataType.Trim(),
            DecodeOptions = source.DecodeOptions ?? new DecodeOptions(),
            WritePolicy = source.WritePolicy,
            Unit = source.Unit?.Trim() ?? string.Empty,
            IsWritable = source.IsWritable,
            IsEnabled = source.IsEnabled,
            RiskLevel = source.RiskLevel,
            RawMin = source.EffectiveRawMin,
            RawMax = source.EffectiveRawMax,
            EngMin = source.EffectiveEngMin,
            EngMax = source.EffectiveEngMax,
            Description = source.Description?.Trim() ?? string.Empty
        };
    }

    private static List<PointsConfig.PointGroupEntry> CloneGroups(
        IEnumerable<PointsConfig.PointGroupEntry>? groups)
        => (groups ?? Array.Empty<PointsConfig.PointGroupEntry>()).Select(CloneGroup).ToList();

    private static PointsConfig EnsureDeviceGroups(PointsConfig current, DeviceConfig device)
    {
        if (current.SchemaVersion != PointsConfig.CurrentSchemaVersion)
            return current;

        var devices = (device.Devices ?? new List<DeviceConfig.DeviceEntry>())
            .Where(item => item is not null && !string.IsNullOrWhiteSpace(item.Id))
            .ToList();
        var deviceIds = devices.Select(item => item.Id.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var groups = CloneGroups(current.Groups)
            .Where(group => deviceIds.Contains(group.DeviceId.Trim()))
            .ToList();
        foreach (var item in devices)
        {
            if (groups.Any(group => string.Equals(group.DeviceId, item.Id, StringComparison.OrdinalIgnoreCase)))
                continue;
            groups.Add(new PointsConfig.PointGroupEntry
            {
                Id = DeviceConfigurationMigrator.StableId($"group|{item.Id}|DEFAULT"),
                DeviceId = item.Id.Trim(),
                Code = "DEFAULT",
                Name = "未分组",
                Description = "由设备配置变更生成的默认分组",
                SortOrder = 0
            });
        }

        var currentGroups = current.Groups ?? new List<PointsConfig.PointGroupEntry>();
        var unchanged = currentGroups.Count == groups.Count
            && currentGroups.All(source => groups.Any(target =>
                string.Equals(source.Id, target.Id, StringComparison.OrdinalIgnoreCase)
                && string.Equals(source.DeviceId, target.DeviceId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(source.Code, target.Code, StringComparison.OrdinalIgnoreCase)
                && string.Equals(source.Name, target.Name, StringComparison.Ordinal)
                && source.SortOrder == target.SortOrder
                && string.Equals(source.Description, target.Description, StringComparison.Ordinal)));
        if (unchanged) return current;

        return new PointsConfig
        {
            SchemaVersion = current.SchemaVersion,
            Groups = groups,
            Points = current.Points
        };
    }

    private static PointsConfig.PointGroupEntry CloneGroup(PointsConfig.PointGroupEntry source)
        => new()
        {
            Id = string.IsNullOrWhiteSpace(source.Id)
                ? DeviceConfigurationMigrator.StableId($"group|{source.DeviceId}|{source.Code}")
                : source.Id.Trim(),
            DeviceId = source.DeviceId?.Trim() ?? string.Empty,
            Code = source.Code?.Trim() ?? string.Empty,
            Name = string.IsNullOrWhiteSpace(source.Name) ? source.Code?.Trim() ?? string.Empty : source.Name.Trim(),
            Description = source.Description?.Trim() ?? string.Empty,
            SortOrder = source.SortOrder
        };

    private static DeviceConfigurationApplyResult Failed(string message, string revision)
        => new(false, string.IsNullOrWhiteSpace(revision) ? "points-group" : revision, message);
}

public sealed record DeviceConfigurationApplyResult(
    bool Ok,
    string Revision,
    string? Error,
    DeviceConfigurationSnapshot? Snapshot = null);
