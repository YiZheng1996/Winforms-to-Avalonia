using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Core.Application;

/// <summary>
/// 设备点位目录用例：维护 points.json 的声明式点位，保存前统一校验并保留 JsonConfigStore 的备份能力。
/// 运行时仍由 DeviceRuntimeFactory 创建快照，因此保存后必须由上层重载运行时。
/// </summary>
public sealed class DevicePointCatalogService
{
    /// <summary>
    /// 点位配置文件名称。
    /// </summary>
    private const string ConfigFileName = "points.json";
    /// <summary>
    /// 配置读写器。
    /// </summary>
    private readonly IConfigStore _store;
    /// <summary>
    /// 运行时点位配置对象。
    /// </summary>
    private readonly PointsConfig _runtimeConfig;
    /// <summary>
    /// 审计日志。
    /// </summary>
    private readonly IAuditLog _audit;
    /// <summary>
    /// 完整设备配置；v2 导入时用于把设备编码解析为稳定 DeviceId。
    /// </summary>
    private readonly DeviceConfig? _deviceConfig;

    /// <summary>
    /// 创建设备点位目录服务。
    /// </summary>
    public DevicePointCatalogService(
        IConfigStore store,
        PointsConfig runtimeConfig,
        IAuditLog audit,
        DeviceConfig? deviceConfig = null)
    {
        _store = store;
        _runtimeConfig = runtimeConfig;
        _audit = audit;
        _deviceConfig = deviceConfig;
    }

    /// <summary>
    /// 当前内存目录的点位配置版本。
    /// </summary>
    public int SchemaVersion => _runtimeConfig.SchemaVersion;

    /// <summary>
    /// 当前点位的副本，避免页面直接修改运行时配置。
    /// </summary>
    public IReadOnlyList<PointsConfig.PointEntry> List()
        => (_runtimeConfig.Points ?? new List<PointsConfig.PointEntry>()).Select(Clone).ToList();

    /// <summary>
    /// 当前点位分组的副本，避免页面直接修改运行时配置。
    /// </summary>
    public IReadOnlyList<PointsConfig.PointGroupEntry> ListGroups()
        => (_runtimeConfig.Groups ?? new List<PointsConfig.PointGroupEntry>()).Select(CloneGroup).ToList();

    /// <summary>
    /// 配置应用服务成功切换完整快照后，同步页面目录的内存副本；不独立写文件。
    /// </summary>
    public void AdoptApplied(PointsConfig applied)
    {
        ArgumentNullException.ThrowIfNull(applied);
        _runtimeConfig.SchemaVersion = applied.SchemaVersion;
        _runtimeConfig.Groups ??= new List<PointsConfig.PointGroupEntry>();
        _runtimeConfig.Points ??= new List<PointsConfig.PointEntry>();
        _runtimeConfig.Groups.Clear();
        _runtimeConfig.Groups.AddRange((applied.Groups ?? new List<PointsConfig.PointGroupEntry>()).Select(CloneGroup));
        _runtimeConfig.Points.Clear();
        _runtimeConfig.Points.AddRange((applied.Points ?? new List<PointsConfig.PointEntry>()).Select(Clone));
    }

    /// <summary>
    /// 用一组完整点位替换当前目录。只有保存成功后才更新运行时配置对象。
    /// </summary>
    public async Task SaveAsync(UserContext actor, IReadOnlyList<PointsConfig.PointEntry> entries, CancellationToken ct = default)
    {
        Ensure(actor, PermissionCode.ManageDevices);
        if (entries is null || entries.Count == 0)
            throw new DomainException("点位目录不能为空");

        // 当前点位页面只维护完整设备配置；保存时统一补齐设备和分组身份。
        var candidate = new PointsConfig
        {
            SchemaVersion = _runtimeConfig.SchemaVersion == PointsConfig.LegacySchemaVersion
                ? PointsConfig.LegacySchemaVersion
                : PointsConfig.CurrentSchemaVersion
        };
        var normalized = entries.Select(Normalize).ToList();
        if (candidate.SchemaVersion == PointsConfig.CurrentSchemaVersion)
        {
            candidate.Groups.AddRange((_runtimeConfig.Groups ?? new List<PointsConfig.PointGroupEntry>()).Select(CloneGroup));
            if (candidate.Groups.Count == 0)
                candidate.Groups.AddRange(CreateDefaultGroups());
            ResolveDeviceIds(normalized);
            ResolveGroupIds(normalized, candidate.Groups);
        }
        candidate.Points.AddRange(normalized);
        candidate.Validate();

        await _store.SaveAsync(ConfigFileName, candidate, ct);

        _runtimeConfig.SchemaVersion = candidate.SchemaVersion;
        _runtimeConfig.Groups ??= new List<PointsConfig.PointGroupEntry>();
        _runtimeConfig.Points ??= new List<PointsConfig.PointEntry>();
        _runtimeConfig.Groups.Clear();
        _runtimeConfig.Groups.AddRange(candidate.Groups.Select(CloneGroup));
        _runtimeConfig.Points.Clear();
        _runtimeConfig.Points.AddRange(candidate.Points.Select(Clone));
        await _audit.WriteAsync(actor.LoginName, "DevicePointCatalogSaved", ConfigFileName,
            $"points:{candidate.Points.Count}", ct);
    }

    /// <summary>
    /// 在完整设备配置边界内解析导入行的设备编码，拒绝隐式创建设备或跨设备错配。
    /// </summary>
    private void ResolveDeviceIds(IReadOnlyList<PointsConfig.PointEntry> entries)
    {
        var devices = (_deviceConfig?.Devices ?? new List<DeviceConfig.DeviceEntry>())
            .Where(device => !string.IsNullOrWhiteSpace(device.Id) && !string.IsNullOrWhiteSpace(device.Code))
            .ToDictionary(device => device.Code.Trim(), StringComparer.OrdinalIgnoreCase);
        var onlyDevice = devices.Count == 1 ? devices.Values.Single() : null;
        foreach (var point in entries)
        {
            if (string.IsNullOrWhiteSpace(point.DeviceId) && string.IsNullOrWhiteSpace(point.DeviceCode))
            {
                if (onlyDevice is null)
                    throw new DomainException($"点位 {point.Code} 缺少设备归属；多设备配置不能使用无设备编码的旧模板");
                point.DeviceId = onlyDevice.Id.Trim();
                point.DeviceCode = onlyDevice.Code.Trim();
                if (string.IsNullOrWhiteSpace(point.Protocol)
                    && DevicePointTypeCatalog.TryParseDriverKey(onlyDevice.DriverKey, out var protocol))
                    point.Protocol = DevicePointTypeCatalog.ToStorage(protocol);
            }

            if (string.IsNullOrWhiteSpace(point.DeviceCode))
                continue;
            if (!devices.TryGetValue(point.DeviceCode.Trim(), out var device))
                throw new DomainException($"点位 {point.Code} 引用的设备不存在：{point.DeviceCode}");
            if (!string.IsNullOrWhiteSpace(point.DeviceId)
                && !string.Equals(point.DeviceId.Trim(), device.Id.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new DomainException($"点位 {point.Code} 的设备编码与 DeviceId 不一致：{point.DeviceCode}");
            point.DeviceId = device.Id.Trim();
        }
    }

    private void ResolveGroupIds(
        IReadOnlyList<PointsConfig.PointEntry> entries,
        IReadOnlyList<PointsConfig.PointGroupEntry> groups)
    {
        var groupsByDeviceAndCode = groups
            .Where(group => !string.IsNullOrWhiteSpace(group.DeviceId) && !string.IsNullOrWhiteSpace(group.Code))
            .ToDictionary(group => $"{group.DeviceId.Trim()}\u001f{group.Code.Trim()}",
                StringComparer.OrdinalIgnoreCase);
        foreach (var point in entries)
        {
            var groupCode = string.IsNullOrWhiteSpace(point.GroupCode) ? "DEFAULT" : point.GroupCode.Trim();
            if (string.IsNullOrWhiteSpace(point.DeviceId))
                throw new DomainException($"点位 {point.Code} 缺少设备归属，无法解析点位分组");
            if (!string.IsNullOrWhiteSpace(point.GroupId))
            {
                var group = groups.FirstOrDefault(item =>
                    string.Equals(item.Id, point.GroupId, StringComparison.OrdinalIgnoreCase));
                if (group is null || !string.Equals(group.DeviceId, point.DeviceId, StringComparison.OrdinalIgnoreCase))
                    throw new DomainException($"点位 {point.Code} 的 GroupId 与设备归属不一致");
                continue;
            }
            if (!groupsByDeviceAndCode.TryGetValue($"{point.DeviceId.Trim()}\u001f{groupCode}", out var resolved))
                throw new DomainException($"点位 {point.Code} 引用的分组不存在：{groupCode}");
            point.GroupId = resolved.Id;
        }
    }

    private IReadOnlyList<PointsConfig.PointGroupEntry> CreateDefaultGroups()
        => (_deviceConfig?.Devices ?? new List<DeviceConfig.DeviceEntry>())
            .Where(device => !string.IsNullOrWhiteSpace(device.Id))
            .Select(device => new PointsConfig.PointGroupEntry
            {
                Id = DeviceConfigurationMigrator.StableId($"group|{device.Id}|DEFAULT"),
                DeviceId = device.Id.Trim(),
                Code = "DEFAULT",
                Name = "未分组",
                Description = "由旧版点位配置迁移生成的默认分组",
                SortOrder = 0
            })
            .ToList();

    /// <summary>
    /// 校验设备管理权限，越权时写入审计并抛出异常。
    /// </summary>
    private void Ensure(UserContext actor, PermissionCode permission)
    {
        try { actor.EnsurePermission(permission); }
        catch (AuthorizationException)
        {
            _ = _audit.WriteAsync(actor.LoginName, "AccessDenied", permission.ToString(), null);
            throw;
        }
    }

    /// <summary>
    /// 整理点位字段：去空格并按实际量程落盘。
    /// </summary>
    private static PointsConfig.PointEntry Normalize(PointsConfig.PointEntry source)
    {
        var rawMin = source.EffectiveRawMin;
        var rawMax = source.EffectiveRawMax;
        var engMin = source.EffectiveEngMin;
        var engMax = source.EffectiveEngMax;
        return new PointsConfig.PointEntry
        {
            Id = string.IsNullOrWhiteSpace(source.Id)
                ? DeviceConfigurationMigrator.StableId($"point|{source.DeviceCode.Trim()}|{source.DeviceId.Trim()}|{source.Code.Trim()}|{source.Protocol.Trim()}|{source.Address.Trim()}")
                : source.Id.Trim(),
            Code = source.Code.Trim(),
            Name = string.IsNullOrWhiteSpace(source.Name) ? source.Code.Trim() : source.Name.Trim(),
            Protocol = DevicePointTypeCatalog.TryParseProtocol(source.Protocol, out var protocol)
                ? DevicePointTypeCatalog.ToStorage(protocol)
                : source.Protocol.Trim(),
            Address = source.Address.Trim(),
            DeviceId = source.DeviceId?.Trim() ?? string.Empty,
            DeviceCode = source.DeviceCode?.Trim() ?? string.Empty,
            GroupId = source.GroupId?.Trim() ?? string.Empty,
            GroupCode = source.GroupCode?.Trim() ?? string.Empty,
            DataType = DevicePointTypeCatalog.TryParseDataType(source.DataType, out var dataType)
                ? DevicePointTypeCatalog.ToStorage(dataType)
                : source.DataType.Trim(),
            RawDataType = string.IsNullOrWhiteSpace(source.RawDataType)
                ? source.DataType.Trim()
                : source.RawDataType.Trim(),
            AddressDefinition = source.AddressDefinition is null ? null : new PointAddressDefinition
            {
                Area = source.AddressDefinition.Area,
                Offset = source.AddressDefinition.Offset,
                BitIndex = source.AddressDefinition.BitIndex,
                DbNumber = source.AddressDefinition.DbNumber,
                ByteOffset = source.AddressDefinition.ByteOffset,
                BitOffset = source.AddressDefinition.BitOffset,
                LogicalAddress = source.AddressDefinition.LogicalAddress
            },
            DecodeOptions = source.DecodeOptions is null ? new DecodeOptions() : new DecodeOptions
            {
                ByteOrder = source.DecodeOptions.ByteOrder,
                WordOrder = source.DecodeOptions.WordOrder
            },
            WritePolicy = source.WritePolicy,
            IsWritable = source.IsWritable,
            RiskLevel = source.RiskLevel,
            RawMin = rawMin,
            RawMax = rawMax,
            EngMin = engMin,
            EngMax = engMax,
            Description = source.Description?.Trim() ?? string.Empty
        };
    }

    /// <summary>
    /// 深拷贝点位，避免调用方修改内部对象。
    /// </summary>
    private static PointsConfig.PointEntry Clone(PointsConfig.PointEntry source)
        => new()
        {
            Id = source.Id,
            Code = source.Code,
            Name = source.Name,
            Protocol = source.Protocol,
            Address = source.Address,
            DeviceId = source.DeviceId,
            DeviceCode = source.DeviceCode,
            GroupId = source.GroupId,
            GroupCode = source.GroupCode,
            DataType = source.DataType,
            RawDataType = source.RawDataType,
            AddressDefinition = source.AddressDefinition is null ? null : new PointAddressDefinition
            {
                Area = source.AddressDefinition.Area,
                Offset = source.AddressDefinition.Offset,
                BitIndex = source.AddressDefinition.BitIndex,
                DbNumber = source.AddressDefinition.DbNumber,
                ByteOffset = source.AddressDefinition.ByteOffset,
                BitOffset = source.AddressDefinition.BitOffset,
                LogicalAddress = source.AddressDefinition.LogicalAddress
            },
            DecodeOptions = source.DecodeOptions is null ? new DecodeOptions() : new DecodeOptions
            {
                ByteOrder = source.DecodeOptions.ByteOrder,
                WordOrder = source.DecodeOptions.WordOrder
            },
            WritePolicy = source.WritePolicy,
            IsWritable = source.IsWritable,
            RiskLevel = source.RiskLevel,
            Scale = source.Scale is null ? null : new PointsConfig.ScaleValues
            {
                RawMin = source.Scale.RawMin,
                RawMax = source.Scale.RawMax,
                EngMin = source.Scale.EngMin,
                EngMax = source.Scale.EngMax
            },
            RawMin = source.RawMin,
            RawMax = source.RawMax,
            EngMin = source.EngMin,
            EngMax = source.EngMax,
            Description = source.Description
        };

    private static PointsConfig.PointGroupEntry CloneGroup(PointsConfig.PointGroupEntry source)
        => new()
        {
            Id = source.Id,
            DeviceId = source.DeviceId,
            Code = source.Code,
            Name = source.Name,
            Description = source.Description,
            SortOrder = source.SortOrder
        };
}
