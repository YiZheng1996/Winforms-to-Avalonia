using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Configuration;
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
    /// 创建设备点位目录服务。
    /// </summary>
    public DevicePointCatalogService(IConfigStore store, PointsConfig runtimeConfig, IAuditLog audit)
    {
        _store = store;
        _runtimeConfig = runtimeConfig;
        _audit = audit;
    }

    /// <summary>
    /// 当前点位的副本，避免页面直接修改运行时配置。
    /// </summary>
    public IReadOnlyList<PointsConfig.PointEntry> List()
        => _runtimeConfig.Points.Select(Clone).ToList();

    /// <summary>
    /// 用一组完整点位替换当前目录。只有保存成功后才更新运行时配置对象。
    /// </summary>
    public async Task SaveAsync(UserContext actor, IReadOnlyList<PointsConfig.PointEntry> entries, CancellationToken ct = default)
    {
        Ensure(actor, PermissionCode.ManageDevices);
        if (entries is null || entries.Count == 0)
            throw new DomainException("点位目录不能为空");

        var candidate = new PointsConfig { SchemaVersion = PointsConfig.CurrentSchemaVersion };
        candidate.Points.AddRange(entries.Select(Normalize));
        candidate.Validate();

        await _store.SaveAsync(ConfigFileName, candidate, ct);

        _runtimeConfig.SchemaVersion = candidate.SchemaVersion;
        _runtimeConfig.Points.Clear();
        _runtimeConfig.Points.AddRange(candidate.Points.Select(Clone));
        await _audit.WriteAsync(actor.LoginName, "DevicePointCatalogSaved", ConfigFileName,
            $"points:{candidate.Points.Count}", ct);
    }

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
            Code = source.Code.Trim(),
            Name = string.IsNullOrWhiteSpace(source.Name) ? source.Code.Trim() : source.Name.Trim(),
            Protocol = source.Protocol.Trim(),
            Address = source.Address.Trim(),
            DataType = source.DataType.Trim(),
            Unit = source.Unit?.Trim() ?? string.Empty,
            IsWritable = source.IsWritable,
            IsEnabled = source.IsEnabled,
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
            Code = source.Code,
            Name = source.Name,
            Protocol = source.Protocol,
            Address = source.Address,
            DataType = source.DataType,
            Unit = source.Unit,
            IsWritable = source.IsWritable,
            IsEnabled = source.IsEnabled,
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
}
