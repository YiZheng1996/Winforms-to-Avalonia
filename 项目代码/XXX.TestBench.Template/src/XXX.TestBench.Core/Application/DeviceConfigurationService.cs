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
    private readonly IDeviceEventSink? _events;
    private readonly Action<string>? _markRuntimeFaulted;

    public DeviceConfigurationService(
        IDeviceConfigurationStore store,
        DeviceOperationCoordinator operations,
        IAuditLog audit,
        IEnumerable<IDeviceDriverDescriptor>? drivers = null,
        Func<bool>? hasActiveRun = null,
        Func<DeviceConfigurationSnapshot, CancellationToken, Task<IDeviceRuntime>>? runtimeFactory = null,
        Func<IDeviceRuntime?>? currentRuntime = null,
        Func<IDeviceRuntime, Task>? publishRuntime = null,
        IEnumerable<RequiredSignal>? requiredSignals = null,
        IDeviceEventSink? events = null,
        Action<string>? markRuntimeFaulted = null)
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
        _events = events;
        _markRuntimeFaulted = markRuntimeFaulted;
    }

    public IReadOnlyList<ConfigurationIssue> Validate(DeviceConfigurationSnapshot snapshot)
        => MultiDeviceConfigurationValidator.Validate(snapshot, _drivers, _requiredSignals);

    /// <summary>
    /// 读取当前已经提交的完整设备配置快照，供应用成功后的页面同步使用。
    /// </summary>
    public Task<DeviceConfigurationSnapshot> LoadActiveAsync(CancellationToken ct = default)
        => _store.LoadActiveAsync(ct);

    /// <summary>
    /// 从当前 active 快照构造只修改设备/通道的候选，并通过同一配置应用事务生效。
    /// 点位、仿真规则和业务信号绑定沿用当前生效版本，避免 UI 层分别保存配置文件。
    /// </summary>
    public async Task<DeviceConfigurationApplyResult> ApplyDeviceConfigurationAsync(
        UserContext actor,
        DeviceConfig device,
        CancellationToken ct = default,
        bool s7OptimizedBlockAccessConfirmed = false)
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
        return await ApplyCoreAsync(actor, candidate, current, s7OptimizedBlockAccessConfirmed, ct);
    }

    /// <summary>
    /// 从当前 active 快照构造只修改点位的候选，并通过同一配置应用事务生效。
    /// </summary>
    public async Task<DeviceConfigurationApplyResult> ApplyPointsAsync(
        UserContext actor,
        IReadOnlyList<PointsConfig.PointEntry> points,
        CancellationToken ct = default,
        bool s7OptimizedBlockAccessConfirmed = false)
    {
        ArgumentNullException.ThrowIfNull(points);
        var current = await _store.LoadActiveAsync(ct);
        return await ApplyPointConfigurationAsync(actor, current, points,
            current.Points.Groups ?? new List<PointsConfig.PointGroupEntry>(), ct,
            s7OptimizedBlockAccessConfirmed);
    }

    /// <summary>
    /// 从当前 active 快照构造点位和分组候选，并通过同一配置应用事务生效。
    /// </summary>
    public async Task<DeviceConfigurationApplyResult> ApplyPointsAsync(
        UserContext actor,
        IReadOnlyList<PointsConfig.PointEntry> points,
        IReadOnlyList<PointsConfig.PointGroupEntry> groups,
        CancellationToken ct = default,
        bool s7OptimizedBlockAccessConfirmed = false)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(groups);
        var current = await _store.LoadActiveAsync(ct);
        return await ApplyPointConfigurationAsync(actor, current, points, groups, ct,
            s7OptimizedBlockAccessConfirmed);
    }

    /// <summary>
    /// 新增点位分组。分组写入仍然沿用完整快照事务，不单独修改 points.json。
    /// </summary>
    public async Task<DeviceConfigurationApplyResult> AddPointGroupAsync(
        UserContext actor,
        PointsConfig.PointGroupEntry group,
        CancellationToken ct = default,
        bool s7OptimizedBlockAccessConfirmed = false)
    {
        ArgumentNullException.ThrowIfNull(group);
        var current = await _store.LoadActiveAsync(ct);
        var groups = CloneGroups(current.Points.Groups);
        groups.Add(CloneGroup(group));
        return await ApplyPointConfigurationAsync(actor, current, current.Points.Points, groups, ct,
            s7OptimizedBlockAccessConfirmed);
    }

    /// <summary>
    /// 修改点位分组的显示属性，保持分组 Id 稳定。
    /// </summary>
    public async Task<DeviceConfigurationApplyResult> UpdatePointGroupAsync(
        UserContext actor,
        PointsConfig.PointGroupEntry group,
        CancellationToken ct = default,
        bool s7OptimizedBlockAccessConfirmed = false)
    {
        ArgumentNullException.ThrowIfNull(group);
        var current = await _store.LoadActiveAsync(ct);
        var groups = CloneGroups(current.Points.Groups);
        var index = groups.FindIndex(item => string.Equals(item.Id, group.Id, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return Failed("找不到要修改的点位分组", group.Id);
        groups[index] = CloneGroup(group);
        return await ApplyPointConfigurationAsync(actor, current, current.Points.Points, groups, ct,
            s7OptimizedBlockAccessConfirmed);
    }

    /// <summary>
    /// 删除点位分组。删除前把该设备下引用点位一次性迁移到 DEFAULT，
    /// 候选快照校验或运行时切换失败时不会改变当前生效配置。
    /// </summary>
    public async Task<DeviceConfigurationApplyResult> DeletePointGroupAsync(
        UserContext actor,
        string groupId,
        CancellationToken ct = default,
        bool s7OptimizedBlockAccessConfirmed = false)
    {
        var current = await _store.LoadActiveAsync(ct);
        var currentGroups = current.Points.Groups ?? new List<PointsConfig.PointGroupEntry>();
        var group = currentGroups.FirstOrDefault(item =>
            string.Equals(item.Id, groupId, StringComparison.OrdinalIgnoreCase));
        if (group is null)
            return Failed("找不到要删除的点位分组", groupId);
        if (string.Equals(group.Code, "DEFAULT", StringComparison.OrdinalIgnoreCase))
            return Failed("系统默认分组不能删除", groupId);

        var defaultGroup = currentGroups.FirstOrDefault(item =>
            string.Equals(item.DeviceId, group.DeviceId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.Code, "DEFAULT", StringComparison.OrdinalIgnoreCase));
        if (defaultGroup is null)
            return Failed("当前设备缺少系统默认分组，不能安全删除", groupId);

        var groups = CloneGroups(current.Points.Groups);
        groups.RemoveAll(item => string.Equals(item.Id, groupId, StringComparison.OrdinalIgnoreCase));
        var points = current.Points.Points
            .Select(point => NormalizePoint(point, current.Device))
            .ToList();
        foreach (var point in points.Where(point =>
                     string.Equals(point.GroupId, group.Id, StringComparison.OrdinalIgnoreCase)))
        {
            point.GroupId = defaultGroup.Id;
            point.GroupCode = defaultGroup.Code;
        }
        return await ApplyPointConfigurationAsync(actor, current, points, groups, ct,
            s7OptimizedBlockAccessConfirmed);
    }

    private async Task<DeviceConfigurationApplyResult> ApplyPointConfigurationAsync(
        UserContext actor,
        DeviceConfigurationSnapshot current,
        IReadOnlyList<PointsConfig.PointEntry> points,
        IReadOnlyList<PointsConfig.PointGroupEntry> groups,
        CancellationToken ct,
        bool s7OptimizedBlockAccessConfirmed = false)
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
        return await ApplyCoreAsync(actor, candidate, current, s7OptimizedBlockAccessConfirmed, ct);
    }

    /// <summary>
    /// 只修改项目级 SignalKey → PointId 绑定，并通过完整配置事务生效。
    /// </summary>
    public async Task<DeviceConfigurationApplyResult> ApplySignalBindingsAsync(
        UserContext actor,
        SignalBindingsConfig bindings,
        CancellationToken ct = default,
        bool s7OptimizedBlockAccessConfirmed = false)
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
        return await ApplyCoreAsync(actor, candidate, current, s7OptimizedBlockAccessConfirmed, ct);
    }

    /// <summary>
    /// 将候选快照作为一个版本写入并切换生效指针；没有运行时切换委托时只执行存储应用。
    /// </summary>
    public async Task<DeviceConfigurationApplyResult> ApplyAsync(
        UserContext actor,
        DeviceConfigurationSnapshot snapshot,
        CancellationToken ct = default,
        bool s7OptimizedBlockAccessConfirmed = false)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return await ApplyCoreAsync(
            actor,
            snapshot,
            previousSnapshot: null,
            s7OptimizedBlockAccessConfirmed: s7OptimizedBlockAccessConfirmed,
            ct: ct);
    }

    /// <summary>
    /// 使用完整应用选项应用候选快照。旧的 bool 参数入口仍保留，避免已有调用方改变行为。
    /// </summary>
    public Task<DeviceConfigurationApplyResult> ApplyAsync(
        UserContext actor,
        DeviceConfigurationSnapshot snapshot,
        DeviceConfigurationApplyOptions options,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return ApplyCoreAsync(actor, snapshot, previousSnapshot: null,
            options.ConfirmS7OptimizedBlockAccess, ct, options);
    }

    private async Task<DeviceConfigurationApplyResult> ApplyCoreAsync(
        UserContext actor,
        DeviceConfigurationSnapshot snapshot,
        DeviceConfigurationSnapshot? previousSnapshot,
        bool s7OptimizedBlockAccessConfirmed,
        CancellationToken ct = default,
        DeviceConfigurationApplyOptions? applyOptions = null)
    {
        Ensure(actor, PermissionCode.ManageDevices);
        var options = applyOptions ?? new DeviceConfigurationApplyOptions(
            ConfirmS7OptimizedBlockAccess: s7OptimizedBlockAccessConfirmed);
        if (_hasActiveRun())
            return new DeviceConfigurationApplyResult(false, snapshot.Revision, "存在活动试验，禁止应用设备配置");
        if (!(snapshot.Device?.Devices ?? new List<DeviceConfig.DeviceEntry>()).Any(device => device is not null))
            return new DeviceConfigurationApplyResult(false, snapshot.Revision, "当前候选没有设备，不能应用");
        var issues = Validate(snapshot);
        if (issues.Count > 0)
            return new DeviceConfigurationApplyResult(
                false,
                snapshot.Revision,
                string.Join("；", issues.Select(issue => issue.Message)));

        // 完整快照入口没有调用方传入旧版本时，仅在配置切换已接入运行时的组合根中
        // 读取当前 active，供风险提示和失败回滚使用。没有运行时的首个配置仍可直接落盘。
        if (previousSnapshot is null && _currentRuntime is not null)
            previousSnapshot = await TryLoadPreviousSnapshotAsync(ct);

        var optimizedBlockAccessNotice = SiemensS7OptimizedBlockAccessAdvisor.Inspect(
            snapshot.Points?.Points,
            snapshot.Device?.Devices,
            requireHardwareMode: true,
            previousPoints: previousSnapshot?.Points?.Points,
            previousDevices: previousSnapshot?.Device?.Devices);
        if (optimizedBlockAccessNotice is not null && !options.ConfirmS7OptimizedBlockAccess)
        {
            return new DeviceConfigurationApplyResult(
                false,
                snapshot.Revision,
                optimizedBlockAccessNotice.ConfirmationMessage,
                RequiresS7OptimizedBlockAccessConfirmation: true,
                S7OptimizedBlockAccessNotice: optimizedBlockAccessNotice);
        }

        await using var lease = await _operations.EnterConfigurationAsync(ct);
        if (_runtimeFactory is not null && _publishRuntime is null)
            return new DeviceConfigurationApplyResult(false, snapshot.Revision, "设备运行时切换未配置发布回调，不能应用");

        IDeviceRuntime? oldRuntime = _currentRuntime?.Invoke();
        IDeviceRuntime? candidateRuntime = null;
        var oldRevision = string.IsNullOrWhiteSpace(previousSnapshot?.Revision)
            ? oldRuntime?.ActiveRevision
            : previousSnapshot.Revision;
        var oldRuntimeWasTouched = false;
        var activeCommitted = false;
        var runtimePublished = false;
        RuntimeActivationReport? activationReport = null;
        try
        {
            // Revision 先完整落盘；运行时切换失败时 active 仍指向旧版本。
            await _store.StageAsync(snapshot, ct);

            if (_runtimeFactory is not null && oldRuntime is not null)
            {
                oldRuntimeWasTouched = true;
                await oldRuntime.StopAsync(ct);
            }

            if (_runtimeFactory is not null)
            {
                candidateRuntime = await _runtimeFactory(snapshot, ct);
                await candidateRuntime.StartAsync(ct);
                activationReport = candidateRuntime.ActivationReport;
                if (activationReport.TotalDevices > 0 && !activationReport.AllOnline
                    && !options.AllowOfflineHardwareDevices)
                {
                    var activationError = FormatActivationIssues(activationReport);
                    try { await candidateRuntime.StopAsync(CancellationToken.None); } catch { }
                    await candidateRuntime.DisposeAsync();
                    candidateRuntime = null;
                    var rollbackError = oldRuntimeWasTouched && oldRuntime is not null
                        ? await TryRestartRuntimeAsync(oldRuntime)
                        : null;
                    PublishRollbackFailureIfNeeded(rollbackError, snapshot.Revision);
                    var error = rollbackError is null
                        ? $"候选配置存在未激活设备，未写入生效版本：{activationError}"
                        : $"候选配置存在未激活设备，且旧运行时恢复失败：{activationError}；{rollbackError}";
                    return new DeviceConfigurationApplyResult(
                        false,
                        snapshot.Revision,
                        error,
                        RequiresOfflineApplyConfirmation: true,
                        ActivationReport: activationReport,
                        RollbackError: rollbackError);
                }
            }

            await _store.CommitActiveAsync(snapshot.Revision, ct);
            activeCommitted = true;
            if (candidateRuntime is not null)
            {
                // 构造前已检查发布回调；这里保留防御性判断，避免未来组合根改动后出现半切换。
                if (_publishRuntime is null)
                    throw new InvalidOperationException("已创建新的设备运行时，但没有配置发布回调");
                await _publishRuntime(candidateRuntime);
                runtimePublished = true;
                candidateRuntime = null;
            }

            if (_runtimeFactory is not null && oldRuntime is not null)
            {
                // 新运行时已经发布后，旧运行时释放失败不应把已经生效的配置回滚成未知状态。
                try { await oldRuntime.DisposeAsync(); } catch { }
            }
            await WriteAuditSafeAsync(actor.LoginName, "DeviceConfigurationApplied", snapshot.Revision, null, ct);
            var warning = activationReport is not null && !activationReport.AllOnline
                ? $"配置已生效，但部分设备未连接：{FormatActivationIssues(activationReport)}"
                : null;
            if (warning is not null)
                PublishEvent(DeviceEventSeverity.Warning, "CONFIG_APPLIED_WITH_OFFLINE_DEVICE", warning);
            return new DeviceConfigurationApplyResult(true, snapshot.Revision, warning, snapshot,
                ActivationReport: activationReport);
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
            string? rollbackError = null;
            if (activeCommitted && string.IsNullOrWhiteSpace(oldRevision))
                rollbackError = "没有可恢复的旧配置版本";
            else if (activeCommitted && !string.IsNullOrWhiteSpace(oldRevision))
            {
                try
                {
                    await _store.CommitActiveAsync(oldRevision, CancellationToken.None);
                }
                catch (Exception rollbackException) { rollbackError = rollbackException.Message; }
            }
            if (rollbackError is null && oldRuntimeWasTouched && oldRuntime is not null)
                rollbackError = await TryRestartRuntimeAsync(oldRuntime);
            PublishRollbackFailureIfNeeded(rollbackError, snapshot.Revision);
            await WriteAuditSafeAsync(actor.LoginName, "DeviceConfigurationApplyFailed", snapshot.Revision,
                rollbackError is null ? ex.Message : $"{ex.Message}；回滚失败：{rollbackError}", ct);
            var error = rollbackError is null ? ex.Message : $"{ex.Message}；回滚失败：{rollbackError}";
            return new DeviceConfigurationApplyResult(false, snapshot.Revision, error,
                ActivationReport: activationReport, RollbackError: rollbackError);
        }
    }

    private async Task<DeviceConfigurationSnapshot?> TryLoadPreviousSnapshotAsync(CancellationToken ct)
    {
        try
        {
            return await _store.LoadActiveAsync(ct);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or InvalidOperationException)
        {
            // 首次配置还没有 active 指针时，没有旧版本是合法状态。
            return null;
        }
    }

    private static async Task<string?> TryRestartRuntimeAsync(IDeviceRuntime runtime)
    {
        try
        {
            await runtime.StartAsync(CancellationToken.None);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private void PublishRollbackFailureIfNeeded(string? error, string revision)
    {
        if (string.IsNullOrWhiteSpace(error)) return;
        try { _markRuntimeFaulted?.Invoke(error); } catch { /* 不覆盖原始回滚错误 */ }
        PublishEvent(DeviceEventSeverity.Error, "CONFIG_ROLLBACK_FAILED",
            $"配置 {revision} 切换失败，旧运行时或旧版本恢复失败：{error}");
    }

    private void PublishEvent(DeviceEventSeverity severity, string code, string message)
        => _events?.Publish(new DeviceCommunicationEvent(
            DateTime.UtcNow,
            severity,
            string.Empty,
            string.Empty,
            nameof(DeviceConfigurationService),
            code,
            message));

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
            IsWritable = source.IsWritable,
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

    private static string FormatActivationIssues(RuntimeActivationReport report)
        => report.Issues.Count == 0
            ? $"已激活 {report.ActivatedDevices}/{report.TotalDevices} 台设备"
            : string.Join("；", report.Issues.Select(issue =>
                $"{issue.DeviceName}({issue.DeviceId})：{issue.Message}"));
}

public sealed record DeviceConfigurationApplyOptions(
    bool ConfirmS7OptimizedBlockAccess = false,
    bool AllowOfflineHardwareDevices = false)
{
    // 兼容早期实现的读取属性；新调用方应使用与方案一致的 AllowOfflineHardwareDevices。
    public bool ConfirmOfflineDevices => AllowOfflineHardwareDevices;
}

public sealed record DeviceConfigurationApplyResult(
    bool Ok,
    string Revision,
    string? Error,
    DeviceConfigurationSnapshot? Snapshot = null,
    bool RequiresS7OptimizedBlockAccessConfirmation = false,
    SiemensS7OptimizedBlockAccessNotice? S7OptimizedBlockAccessNotice = null,
    bool RequiresOfflineApplyConfirmation = false,
    RuntimeActivationReport? ActivationReport = null,
    string? RollbackError = null)
{
    public IReadOnlyList<DeviceActivationIssue>? ActivationIssues => ActivationReport?.Issues;
}
