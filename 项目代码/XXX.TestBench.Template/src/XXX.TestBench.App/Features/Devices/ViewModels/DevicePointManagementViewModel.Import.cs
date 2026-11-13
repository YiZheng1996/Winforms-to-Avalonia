using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Ports;
using XXX.TestBench.Devices.Modbus;

namespace XXX.TestBench.App.ViewModels;

public sealed partial class DevicePointManagementViewModel
{
    private sealed record DevicePointImportPipelineResult(
        DevicePointImportResult Validated,
        DevicePointImportPlan? Plan);

    /// <summary>
    /// 解析、解析设备/分组、驱动预校验并生成统一导入计划。预览和最终应用共用该计划。
    /// </summary>
    public async Task<DevicePointImportPlan?> CreateImportPlanAsync(
        string filePath,
        CancellationToken ct = default)
    {
        var selectedNode = SelectedTreeNode;
        var pipeline = await RunPageValueOperationAsync(
            token => BuildImportPipelineAsync(filePath, selectedNode, token),
            result => result.Plan is { } plan
                ? plan.CanApply
                    ? $"文件校验通过：新增 {plan.AddedCount}、更新 {plan.UpdatedCount + plan.MovedCount}、无变化 {plan.UnchangedCount}，共 {plan.Rows.Count} 行"
                    : $"导入预览存在 {plan.ConflictCount} 个冲突，不能应用"
                : FormatImportIssues(result.Validated),
            ct);
        return pipeline?.Plan;
    }

    /// <summary>
    /// 解析器只负责文件边界；设备、分组、驱动校验和唯一合并均走同一个计划管线。
    /// </summary>
    private async Task<DevicePointImportPipelineResult> BuildImportPipelineAsync(
        string filePath,
        DevicePointTreeNodeViewModel? selectedNode,
        CancellationToken ct)
    {
        var parsed = await _services.DevicePointImporter.ImportAsync(filePath, ct);
        if (!parsed.IsValid)
            return new(parsed, null);

        if (!IsGroupedConfiguration)
        {
            return new(
                new DevicePointImportResult(
                    Array.Empty<DevicePointImportRow>(),
                    new[]
                    {
                        new DevicePointImportIssue(
                            0,
                            "当前设备点位配置不是完整配置版本，不能导入；请先生成当前生效配置")
                    }),
                null);
        }

        var active = await LoadImportActiveSnapshotAsync(ct);
        var activeScope = BuildScopeSnapshot(
            active.Device,
            active.Points,
            selectedNode,
            CanEdit,
            _services.DeviceModes.Runtime is not null);
        var preparedRows = PrepareImportedRows(parsed.Rows, activeScope.Devices);
        var resolved = ResolveImportedGroups(
            new DevicePointImportResult(
                preparedRows,
                Array.Empty<DevicePointImportIssue>()),
            active);
        if (!resolved.IsValid)
            return new(resolved, null);

        var validated = ValidateImportedDriverAddresses(resolved, active.Device);
        if (!validated.IsValid)
            return new(validated, null);

        var plan = BuildImportPlan(validated, active);
        return new(validated, plan);
    }

    private async Task<DeviceConfigurationSnapshot> LoadImportActiveSnapshotAsync(
        CancellationToken ct)
    {
        if (_services.DeviceConfigurations is not null)
            return await _services.DeviceConfigurations.LoadActiveAsync(ct);
        throw new DomainException("完整设备配置服务未连接，无法创建导入计划");
    }

    private IReadOnlyList<DevicePointImportRow> PrepareImportedRows(
        IReadOnlyList<DevicePointImportRow> importedRows,
        IReadOnlyList<DeviceConfig.DeviceEntry> scopeDevices)
    {
        var rows = importedRows
            .Select(row => new DevicePointImportRow(row.RowNumber, ClonePoint(row.Entry)))
            .ToList();
        if (rows.All(row => !string.IsNullOrWhiteSpace(row.Entry.DeviceCode)))
            return rows;
        if (scopeDevices.Count != 1)
            throw new DomainException(
                "当前模板未包含设备前缀；请选择单个设备范围下载的模板。多设备模板请填写“设备编码/分组.点位”。");

        var device = scopeDevices[0];
        foreach (var row in rows.Where(row => string.IsNullOrWhiteSpace(row.Entry.DeviceCode)))
        {
            row.Entry.DeviceId = GetDeviceIdentity(device);
            row.Entry.DeviceCode = device.Code;
        }
        return rows;
    }

    private DevicePointImportResult ResolveImportedGroups(
        DevicePointImportResult result,
        DeviceConfigurationSnapshot active)
    {
        var devices = (active.Device.Devices ?? new List<DeviceConfig.DeviceEntry>())
            .Where(device => device is not null && !string.IsNullOrWhiteSpace(device.Code))
            .GroupBy(device => device.Code.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var issues = result.Issues.ToList();
        foreach (var parsedRow in result.Rows)
        {
            var point = parsedRow.Entry;
            var deviceCode = point.DeviceCode?.Trim() ?? string.Empty;
            if (!devices.TryGetValue(deviceCode, out var device))
            {
                issues.Add(new DevicePointImportIssue(parsedRow.RowNumber,
                    $"点位 {point.Code} 引用的设备不存在：{point.DeviceCode}"));
                continue;
            }

            var groupCode = string.IsNullOrWhiteSpace(point.GroupCode) ? "DEFAULT" : point.GroupCode.Trim();
            var deviceId = GetDeviceIdentity(device);
            var group = (active.Points.Groups ?? new List<PointsConfig.PointGroupEntry>())
                .FirstOrDefault(item =>
                    string.Equals(item.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(item.Code, groupCode, StringComparison.OrdinalIgnoreCase));
            if (group is null)
            {
                issues.Add(new DevicePointImportIssue(parsedRow.RowNumber,
                    $"点位 {point.Code} 引用的分组不存在：设备 {device.Code} / {groupCode}"));
                continue;
            }

            point.DeviceId = deviceId;
            point.DeviceCode = device.Code;
            point.GroupId = group.Id;
            point.GroupCode = group.Code;
            if (string.IsNullOrWhiteSpace(point.Protocol)
                && DevicePointTypeCatalog.TryParseDriverKey(device.DriverKey, out var protocol))
                point.Protocol = DevicePointTypeCatalog.ToStorage(protocol);
        }
        return new DevicePointImportResult(result.Rows, issues);
    }

    /// <summary>
    /// 在设备和分组已经解析后按设备驱动校验原始地址。
    /// </summary>
    private DevicePointImportResult ValidateImportedDriverAddresses(
        DevicePointImportResult result,
        DeviceConfig deviceConfig)
    {
        var issues = DevicePointImportValidator.Validate(
            deviceConfig,
            result.Points,
            _services.DriverDescriptors);
        if (issues.Count == 0)
            return result;

        return new DevicePointImportResult(
            result.Rows,
            result.Issues.Concat(issues.Select(issue => new DevicePointImportIssue(0, issue.ToString()))).ToList());
    }

    private DevicePointImportPlan BuildImportPlan(
        DevicePointImportResult validated,
        DeviceConfigurationSnapshot active)
    {
        var rawActivePoints = active.Points.Points ?? new List<PointsConfig.PointEntry>();
        var activeGroups = active.Points.Groups ?? new List<PointsConfig.PointGroupEntry>();
        // Revision 和指纹必须基于原始快照；规范化只作用于下方独立候选副本。
        var sourceFingerprint = DevicePointImportPlanner.ComputeCatalogFingerprint(rawActivePoints);
        var devices = (active.Device.Devices ?? new List<DeviceConfig.DeviceEntry>())
            .Where(device => device is not null)
            .ToList();
        var descriptors = (_services.DriverDescriptors ?? Array.Empty<IDeviceDriverDescriptor>())
            .Where(descriptor => descriptor is not null)
            .GroupBy(descriptor => descriptor.DriverKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        DeviceConfig.DeviceEntry ResolveDevice(PointsConfig.PointEntry point)
            => devices.FirstOrDefault(device =>
                   string.Equals(device.Id, point.DeviceId, StringComparison.OrdinalIgnoreCase))
               ?? devices.FirstOrDefault(device =>
                   string.Equals(device.Code, point.DeviceCode, StringComparison.OrdinalIgnoreCase))
               ?? throw new DomainException($"点位“{point.Code}”引用的设备不存在：{point.DeviceCode}");

        IDeviceDriverDescriptor ResolveDescriptor(DeviceConfig.DeviceEntry device)
            => descriptors.TryGetValue(device.DriverKey?.Trim() ?? string.Empty, out var descriptor)
                ? descriptor
                : throw new DomainException($"设备“{device.Code}”的驱动未注册，无法规范化点位地址");

        string NormalizeAddress(PointsConfig.PointEntry point)
        {
            var device = ResolveDevice(point);
            return ResolveDescriptor(device).NormalizeAddress(point, device);
        }

        DevicePointAddressRange? GetAddressRange(PointsConfig.PointEntry point)
        {
            var device = ResolveDevice(point);
            return ResolveDescriptor(device).GetAddressRange(point, device);
        }

        PointsConfig.PointEntry PrepareImportedPoint(PointsConfig.PointEntry source)
        {
            var point = ClonePoint(source);
            var device = ResolveDevice(point);
            if (ModbusPointAddressResolver.IsXinjeFieldV1(device.Model))
            {
                // 保存客户可读的原生地址，同时在候选副本中派生标准 Modbus 区域和偏移。
                var originalAddress = point.Address;
                if (string.IsNullOrWhiteSpace(point.OriginalAddress))
                    point.OriginalAddress = originalAddress;
                // 预校验通常已保证成功。若快照在此期间发现不一致，保留原值交给
                // Planner 的设备上下文委托转换为对应行冲突，不让预览以未处理异常中断。
                if (ModbusPointAddressResolver.TryResolve(device, point, out var resolved, out _)
                    && XinjeFieldV1AddressParser.TryParse(point.Address, out var native, out _))
                {
                    point.Address = native.Canonical;
                    point.AddressDefinition = new PointAddressDefinition
                    {
                        Area = resolved.Area.ToCode(),
                        Offset = resolved.Offset
                    };
                    point.CanonicalAddress = resolved.Canonical;
                }
            }
            return point;
        }

        // Planner 在自己的候选副本上重算已有点地址。坏的旧地址会成为同设备的
        // 预览冲突，不会因 CanonicalAddress 缺失或陈旧而回退到文本比较。
        var candidatePoints = rawActivePoints.Select(ClonePoint).ToList();
        var candidateRows = validated.Rows
            .Select(row => new DevicePointImportRow(row.RowNumber, PrepareImportedPoint(row.Entry)))
            .ToList();
        return new DevicePointImportPlanner()
            .Build(candidateRows, candidatePoints, activeGroups, NormalizeAddress, GetAddressRange) with
        {
            SourceRevision = active.Revision,
            SourceCatalogFingerprint = sourceFingerprint
        };
    }

    /// <summary>
    /// 应用同一个已固化 Plan。预览后 Active Revision 或目录指纹变化时拒绝旧计划。
    /// </summary>
    public Task<OperationFeedback> ApplyImportedPointsAsync(
        DevicePointImportPlan plan,
        DeviceConfigurationApplyOptions options,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return RunMutationAsync(async token =>
        {
            if (!plan.CanApply)
                throw new DomainException("导入计划存在冲突，不能应用");
            if (_services.DeviceConfigurations is null)
                throw new DomainException("完整设备配置服务未连接，不能应用导入");

            var current = await _services.DeviceConfigurations.LoadActiveAsync(token);
            if (!string.IsNullOrWhiteSpace(plan.SourceRevision)
                && !string.Equals(plan.SourceRevision, current.Revision, StringComparison.Ordinal))
                throw new DomainException("配置已变化，请重新导入预览");
            var currentFingerprint = DevicePointImportPlanner.ComputeCatalogFingerprint(current.Points.Points);
            if (!string.Equals(plan.SourceCatalogFingerprint, currentFingerprint, StringComparison.Ordinal))
                throw new DomainException("设备点位目录已变化，请重新导入预览");

            if (plan.Rows.All(row => row.Operation == DevicePointImportOperationKind.Unchanged))
                return "导入内容无变化，未生成新配置";

            return await SaveEntriesCoreAsync(
                plan.MergedPoints,
                $"已合并导入并应用：处理 {plan.Rows.Count} 行点位",
                current.Points.Groups,
                null,
                token,
                options);
        }, ct);
    }

    public void CancelImportPreview()
        => SetFeedback(false, "已取消导入，当前设备点位未改变");
}
