using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;

namespace XXX.TestBench.Core.Application;

/// <summary>
/// 当前生效完整配置中的点位目录只读视图。
/// </summary>
public sealed class DevicePointCatalogService
{
    private readonly PointsConfig _runtimeConfig;

    public DevicePointCatalogService(PointsConfig runtimeConfig)
    {
        _runtimeConfig = runtimeConfig ?? throw new ArgumentNullException(nameof(runtimeConfig));
    }

    public int SchemaVersion => _runtimeConfig.SchemaVersion;

    public IReadOnlyList<PointsConfig.PointEntry> List()
        => (_runtimeConfig.Points ?? new List<PointsConfig.PointEntry>()).Select(Clone).ToList();

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
            OriginalAddress = source.OriginalAddress,
            CanonicalAddress = source.CanonicalAddress,
            DataType = source.DataType,
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
            S7DecodeOptions = source.S7DecodeOptions is null ? null : new DecodeOptions
            {
                ByteOrder = source.S7DecodeOptions.ByteOrder,
                WordOrder = source.S7DecodeOptions.WordOrder
            },
            WritePolicy = source.WritePolicy,
            IsWritable = source.IsWritable,
            RiskLevel = source.RiskLevel,
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
