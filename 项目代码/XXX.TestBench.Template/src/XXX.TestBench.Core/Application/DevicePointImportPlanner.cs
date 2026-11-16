using System.Security.Cryptography;
using System.Text;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Core.Application;

/// <summary>
/// 导入计划中单行点位的最终操作类型。
/// </summary>
public enum DevicePointImportOperationKind
{
    Add,
    Update,
    MoveGroup,
    Unchanged,
    Conflict
}

/// <summary>
/// 导入计划中的一行。预览和应用都只消费该对象，避免两边重复推断。
/// </summary>
public sealed record DevicePointImportPlanRow(
    int RowNumber,
    PointsConfig.PointEntry Incoming,
    PointsConfig.PointEntry? Existing,
    PointsConfig.PointEntry? Candidate,
    DevicePointImportOperationKind Operation,
    IReadOnlyList<string> Issues);

/// <summary>
/// 一次导入的完整合并计划。SourceRevision 和 SourceCatalogFingerprint 在创建后固定，
/// 应用前必须与当前 Active 配置比较，防止预览期间配置已被其他操作改变。
/// </summary>
public sealed record DevicePointImportPlan(
    string SourceRevision,
    string SourceCatalogFingerprint,
    IReadOnlyList<DevicePointImportPlanRow> Rows,
    IReadOnlyList<PointsConfig.PointEntry> MergedPoints)
{
    public bool CanApply => Rows.Count > 0
        && Rows.All(row => row.Operation != DevicePointImportOperationKind.Conflict);

    public int AddedCount => Rows.Count(row => row.Operation == DevicePointImportOperationKind.Add);
    public int UpdatedCount => Rows.Count(row => row.Operation == DevicePointImportOperationKind.Update);
    public int MovedCount => Rows.Count(row => row.Operation == DevicePointImportOperationKind.MoveGroup);
    public int UnchangedCount => Rows.Count(row => row.Operation == DevicePointImportOperationKind.Unchanged);
    public int ConflictCount => Rows.Count(row => row.Operation == DevicePointImportOperationKind.Conflict);
}

/// <summary>
/// 设备点位导入的唯一匹配与合并规则。
/// 匹配顺序固定为 PointId、同设备点位编码、同设备点位标签，最后新增。
/// </summary>
public sealed class DevicePointImportPlanner
{
    /// <summary>
    /// 使用当前 Active 点位目录生成导入计划。
    /// </summary>
    public DevicePointImportPlan Build(
        IReadOnlyList<DevicePointImportRow> importedRows,
        IReadOnlyList<PointsConfig.PointEntry> currentPoints,
        IReadOnlyList<PointsConfig.PointGroupEntry> currentGroups,
        Func<PointsConfig.PointEntry, string>? normalizeAddress = null,
        Func<PointsConfig.PointEntry, DevicePointAddressRange?>? getAddressRange = null)
    {
        ArgumentNullException.ThrowIfNull(importedRows);
        ArgumentNullException.ThrowIfNull(currentPoints);
        ArgumentNullException.ThrowIfNull(currentGroups);

        var merged = currentPoints.Select(ClonePoint).ToList();
        var invalidCurrentAddresses = new List<(PointsConfig.PointEntry Point, string Error)>();
        if (normalizeAddress is not null)
        {
            foreach (var point in merged)
            {
                try
                {
                    // 导入预览不信任 JsonIgnore 的旧规范键，按当前已加载设备重新计算。
                    point.CanonicalAddress = normalizeAddress(point);
                }
                catch (Exception ex)
                {
                    point.CanonicalAddress = string.Empty;
                    invalidCurrentAddresses.Add((point, ex.Message));
                }
            }
        }
        var rows = new List<DevicePointImportPlanRow>();
        foreach (var sourceRow in importedRows)
        {
            var incoming = ClonePoint(sourceRow.Entry);
            var issues = new List<string>();
            if (normalizeAddress is not null)
            {
                try
                {
                    incoming.CanonicalAddress = normalizeAddress(incoming);
                }
                catch (Exception ex)
                {
                    incoming.CanonicalAddress = string.Empty;
                    issues.Add($"地址无法规范化，不能继续检查重复地址：{ex.Message}");
                }
            }
            var existing = FindMatch(incoming, merged, currentGroups);

            var invalidCurrent = invalidCurrentAddresses.FirstOrDefault(item => IsSameDevice(item.Point, incoming));
            if (invalidCurrent.Point is not null)
                issues.Add($"设备内已有点位“{DisplayName(invalidCurrent.Point)}”的地址无法规范化，不能可靠检查地址冲突：{invalidCurrent.Error}");

            if (existing is not null && !IsSameDevice(existing, incoming))
                issues.Add("点位身份与设备归属不一致，不允许跨设备更新");

            var codeOwner = merged.FirstOrDefault(point =>
                string.Equals(point.Code?.Trim(), incoming.Code?.Trim(), StringComparison.OrdinalIgnoreCase)
                && !IsSameDevice(point, incoming));
            if (codeOwner is not null
                && (existing is null || !IsSameIdentity(existing, codeOwner)))
            {
                issues.Add($"点位编码已存在于其他设备：{incoming.Code}");
            }

            var candidate = existing is null
                ? CreateNewCandidate(incoming)
                : MergeExistingCandidate(existing, incoming, currentGroups);

            if (candidate is not null)
            {
                var addressOwner = merged.FirstOrDefault(point =>
                    IsSameDevice(point, candidate)
                    && (existing is null || !IsSameIdentity(point, existing))
                    && string.Equals(
                        CanonicalAddress(point, normalizeAddress is not null),
                        CanonicalAddress(candidate, normalizeAddress is not null),
                        StringComparison.OrdinalIgnoreCase));
                if (addressOwner is not null)
                    issues.Add($"设备内地址已被“{DisplayName(addressOwner)}”占用");
            }

            if (candidate is not null && getAddressRange is not null)
            {
                try
                {
                    var candidateRange = getAddressRange(candidate);
                    if (candidateRange is not null)
                    {
                        foreach (var other in merged.Where(point =>
                                     IsSameDevice(point, candidate)
                                     && (existing is null || !IsSameIdentity(point, existing))))
                        {
                            var otherRange = getAddressRange(other);
                            if (otherRange is not null
                                && DevicePointAddressRangeValidator.Overlaps(candidateRange, otherRange))
                                issues.Add($"{DevicePointAddressRangeValidator.FormatRange(candidateRange)} 与点位“{DisplayName(other)}”占用的 {DevicePointAddressRangeValidator.FormatRange(otherRange)} 重叠");
                        }
                    }
                }
                catch (Exception ex)
                {
                    issues.Add($"地址占用范围无法校验：{ex.Message}");
                }
            }

            if (candidate is not null)
            {
                var duplicateIncoming = rows.FirstOrDefault(row =>
                    row.Operation != DevicePointImportOperationKind.Conflict
                    && row.Candidate is not null
                    && IsSameDevice(row.Candidate, candidate)
                    && !IsSameIdentity(row.Candidate, candidate)
                    && string.Equals(
                        CanonicalAddress(row.Candidate, normalizeAddress is not null),
                        CanonicalAddress(candidate, normalizeAddress is not null),
                        StringComparison.OrdinalIgnoreCase));
                if (duplicateIncoming is not null)
                    issues.Add($"导入文件中存在重复地址：{incoming.Address}");
            }

            if (issues.Count > 0 || candidate is null)
            {
                rows.Add(new DevicePointImportPlanRow(
                    sourceRow.RowNumber,
                    incoming,
                    existing,
                    candidate,
                    DevicePointImportOperationKind.Conflict,
                    issues.Distinct(StringComparer.OrdinalIgnoreCase).ToList()));
                continue;
            }

            if (existing is null)
            {
                merged.Add(candidate);
                rows.Add(new DevicePointImportPlanRow(
                    sourceRow.RowNumber,
                    incoming,
                    null,
                    candidate,
                    DevicePointImportOperationKind.Add,
                    Array.Empty<string>()));
                continue;
            }

            var index = merged.FindIndex(point => IsSameIdentity(point, existing));
            if (index < 0)
            {
                rows.Add(new DevicePointImportPlanRow(
                    sourceRow.RowNumber,
                    incoming,
                    existing,
                    candidate,
                    DevicePointImportOperationKind.Conflict,
                    new[] { "匹配到的现有点位已不在当前目录中" }));
                continue;
            }

            var operation = IsEquivalent(existing, candidate)
                ? DevicePointImportOperationKind.Unchanged
                : IsGroupOnlyChange(existing, candidate)
                    ? DevicePointImportOperationKind.MoveGroup
                    : DevicePointImportOperationKind.Update;
            merged[index] = candidate;
            rows.Add(new DevicePointImportPlanRow(
                sourceRow.RowNumber,
                incoming,
                existing,
                candidate,
                operation,
                Array.Empty<string>()));
        }

        return new DevicePointImportPlan(
            string.Empty,
            ComputeCatalogFingerprint(currentPoints),
            rows,
            merged);
    }

    /// <summary>
    /// 计算点位目录稳定字段的 SHA-256 指纹，忽略集合顺序和仅显示用文本。
    /// </summary>
    public static string ComputeCatalogFingerprint(IReadOnlyList<PointsConfig.PointEntry> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        var canonical = string.Join(
            "\n",
            points
                .OrderBy(point => point.Id, StringComparer.OrdinalIgnoreCase)
                .ThenBy(point => point.Code, StringComparer.OrdinalIgnoreCase)
                .Select(PointFingerprintLine));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash);
    }

    private static PointsConfig.PointEntry? FindMatch(
        PointsConfig.PointEntry incoming,
        IReadOnlyList<PointsConfig.PointEntry> merged,
        IReadOnlyList<PointsConfig.PointGroupEntry> groups)
    {
        if (Guid.TryParse(incoming.Id, out _))
        {
            var byId = merged.FirstOrDefault(point =>
                string.Equals(point.Id, incoming.Id, StringComparison.OrdinalIgnoreCase));
            if (byId is not null)
                return byId;
        }

        var byCode = merged.FirstOrDefault(point =>
            IsSameDevice(point, incoming)
            && string.Equals(point.Code?.Trim(), incoming.Code?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (byCode is not null)
            return byCode;

        var incomingTag = ResolveTag(incoming, groups);
        if (!string.IsNullOrWhiteSpace(incomingTag))
        {
            return merged.FirstOrDefault(point =>
                IsSameDevice(point, incoming)
                && string.Equals(ResolveTag(point, groups), incomingTag, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private static PointsConfig.PointEntry CreateNewCandidate(PointsConfig.PointEntry incoming)
    {
        var id = Guid.NewGuid().ToString("D");
        var code = string.IsNullOrWhiteSpace(incoming.Code)
            ? "PT_" + id.Replace("-", string.Empty, StringComparison.Ordinal)
            : incoming.Code.Trim();
        return new PointsConfig.PointEntry
        {
            Id = id,
            Code = code,
            Name = string.IsNullOrWhiteSpace(incoming.Name) ? code : incoming.Name.Trim(),
            Protocol = incoming.Protocol?.Trim() ?? string.Empty,
            DeviceId = incoming.DeviceId?.Trim() ?? string.Empty,
            DeviceCode = incoming.DeviceCode?.Trim() ?? string.Empty,
            GroupId = incoming.GroupId?.Trim() ?? string.Empty,
            GroupCode = incoming.GroupCode?.Trim() ?? string.Empty,
            Address = incoming.Address?.Trim() ?? string.Empty,
            AddressDefinition = CloneAddressDefinition(incoming.AddressDefinition),
            OriginalAddress = incoming.OriginalAddress,
            CanonicalAddress = incoming.CanonicalAddress,
            DataType = incoming.DataType?.Trim() ?? string.Empty,
            S7DecodeOptions = CloneDecodeOptions(incoming.S7DecodeOptions),
            WritePolicy = incoming.WritePolicy,
            IsWritable = incoming.IsWritable,
            RiskLevel = WriteRiskLevel.Normal,
            RawMin = incoming.RawMin,
            RawMax = incoming.RawMax,
            EngMin = incoming.EngMin,
            EngMax = incoming.EngMax,
            Description = incoming.Description?.Trim() ?? string.Empty
        };
    }

    private static PointsConfig.PointEntry MergeExistingCandidate(
        PointsConfig.PointEntry existing,
        PointsConfig.PointEntry incoming,
        IReadOnlyList<PointsConfig.PointGroupEntry> groups)
    {
        var candidate = ClonePoint(existing);
        candidate.Code = string.IsNullOrWhiteSpace(incoming.Code) ? existing.Code : incoming.Code.Trim();
        candidate.Name = string.IsNullOrWhiteSpace(incoming.Name) ? candidate.Code : incoming.Name.Trim();
        candidate.Address = incoming.Address?.Trim() ?? string.Empty;
        candidate.OriginalAddress = incoming.OriginalAddress;
        candidate.CanonicalAddress = incoming.CanonicalAddress;
        // 导入器已经把 Modbus 地址解析为 Area/Offset。即使旧点位只有
        // 同样的文本地址、尚未持久化 AddressDefinition，也必须用新结构
        // 覆盖进候选，否则 HR:0/40001 的规范化结果会在合并时丢失。
        var isStructuredModbus = incoming.AddressDefinition is { Area: { Length: > 0 }, Offset: not null };
        candidate.AddressDefinition = isStructuredModbus
            ? CloneAddressDefinition(incoming.AddressDefinition)
            : string.Equals(
                existing.Address?.Trim(),
                candidate.Address,
                StringComparison.OrdinalIgnoreCase)
                ? CloneAddressDefinition(existing.AddressDefinition)
                : CloneAddressDefinition(incoming.AddressDefinition);
        candidate.DataType = incoming.DataType?.Trim() ?? string.Empty;
        candidate.DeviceId = string.IsNullOrWhiteSpace(incoming.DeviceId) ? existing.DeviceId : incoming.DeviceId.Trim();
        candidate.DeviceCode = string.IsNullOrWhiteSpace(incoming.DeviceCode) ? existing.DeviceCode : incoming.DeviceCode.Trim();
        candidate.GroupId = string.IsNullOrWhiteSpace(incoming.GroupId) ? existing.GroupId : incoming.GroupId.Trim();
        candidate.GroupCode = string.IsNullOrWhiteSpace(incoming.GroupCode)
            ? ResolveGroupCode(candidate.GroupId, groups)
            : incoming.GroupCode.Trim();
        candidate.IsWritable = incoming.IsWritable;
        candidate.RiskLevel = incoming.IsWritable ? existing.RiskLevel : WriteRiskLevel.Normal;
        candidate.RawMin = incoming.RawMin;
        candidate.RawMax = incoming.RawMax;
        candidate.EngMin = incoming.EngMin;
        candidate.EngMax = incoming.EngMax;
        candidate.Description = incoming.Description?.Trim() ?? string.Empty;
        // 模板不承载的字段必须保留现有点位安全边界。
        candidate.WritePolicy = existing.WritePolicy;
        // 当前模板不承载设备级 Modbus 数据排列。Modbus 点位不能继续携带
        // 旧的点位级解码对象；S7 点位的解码元数据则沿用现有配置。
        candidate.S7DecodeOptions = IsModbusPoint(existing) || IsModbusPoint(incoming)
            ? null
            : CloneDecodeOptions(existing.S7DecodeOptions);
        candidate.Protocol = string.IsNullOrWhiteSpace(existing.Protocol) ? incoming.Protocol : existing.Protocol;
        return candidate;
    }

    private static bool IsEquivalent(PointsConfig.PointEntry left, PointsConfig.PointEntry right)
        => string.Equals(left.Code?.Trim(), right.Code?.Trim(), StringComparison.OrdinalIgnoreCase)
           && string.Equals(left.Name?.Trim(), right.Name?.Trim(), StringComparison.OrdinalIgnoreCase)
           && string.Equals(left.Address?.Trim(), right.Address?.Trim(), StringComparison.OrdinalIgnoreCase)
           && string.Equals(left.DataType?.Trim(), right.DataType?.Trim(), StringComparison.OrdinalIgnoreCase)
           && left.IsWritable == right.IsWritable
           && left.RawMin == right.RawMin
           && left.RawMax == right.RawMax
           && left.EngMin == right.EngMin
           && left.EngMax == right.EngMax
           && string.Equals(left.Description?.Trim(), right.Description?.Trim(), StringComparison.OrdinalIgnoreCase)
           && string.Equals(left.DeviceId?.Trim(), right.DeviceId?.Trim(), StringComparison.OrdinalIgnoreCase)
           && string.Equals(left.GroupId?.Trim(), right.GroupId?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool IsGroupOnlyChange(PointsConfig.PointEntry existing, PointsConfig.PointEntry candidate)
        => string.Equals(existing.DeviceId?.Trim(), candidate.DeviceId?.Trim(), StringComparison.OrdinalIgnoreCase)
           && !string.Equals(existing.GroupId?.Trim(), candidate.GroupId?.Trim(), StringComparison.OrdinalIgnoreCase)
           && string.Equals(existing.Code?.Trim(), candidate.Code?.Trim(), StringComparison.OrdinalIgnoreCase)
           && string.Equals(existing.Name?.Trim(), candidate.Name?.Trim(), StringComparison.OrdinalIgnoreCase)
           && string.Equals(existing.Address?.Trim(), candidate.Address?.Trim(), StringComparison.OrdinalIgnoreCase)
           && string.Equals(existing.DataType?.Trim(), candidate.DataType?.Trim(), StringComparison.OrdinalIgnoreCase)
           && existing.IsWritable == candidate.IsWritable;

    private static bool IsSameDevice(PointsConfig.PointEntry left, PointsConfig.PointEntry right)
    {
        var leftId = left.DeviceId?.Trim() ?? string.Empty;
        var rightId = right.DeviceId?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(leftId) || !string.IsNullOrWhiteSpace(rightId))
            return string.Equals(leftId, rightId, StringComparison.OrdinalIgnoreCase);
        return string.Equals(left.DeviceCode?.Trim(), right.DeviceCode?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsModbusPoint(PointsConfig.PointEntry point)
    {
        if (point.ProtocolKind is DevicePointProtocol.ModbusRtu or DevicePointProtocol.ModbusTcp)
            return true;

        var area = point.AddressDefinition?.Area?.Trim();
        return string.Equals(area, "C", StringComparison.OrdinalIgnoreCase)
            || string.Equals(area, "DI", StringComparison.OrdinalIgnoreCase)
            || string.Equals(area, "HR", StringComparison.OrdinalIgnoreCase)
            || string.Equals(area, "IR", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSameIdentity(PointsConfig.PointEntry left, PointsConfig.PointEntry right)
        => !string.IsNullOrWhiteSpace(left.Id)
           && !string.IsNullOrWhiteSpace(right.Id)
           && string.Equals(left.Id.Trim(), right.Id.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string CanonicalAddress(PointsConfig.PointEntry point, bool useResolvedAddress)
        => useResolvedAddress
            ? point.CanonicalAddress?.Trim() ?? string.Empty
            : point.AddressDefinition?.ToCanonical(point.Address ?? string.Empty)
              ?? point.Address?.Trim()
              ?? string.Empty;

    private static string ResolveTag(
        PointsConfig.PointEntry point,
        IReadOnlyList<PointsConfig.PointGroupEntry> groups)
    {
        var groupCode = point.GroupCode?.Trim();
        if (string.IsNullOrWhiteSpace(groupCode))
            groupCode = groups.FirstOrDefault(group =>
                string.Equals(group.Id, point.GroupId, StringComparison.OrdinalIgnoreCase))?.Code;
        return Configuration.DevicePointTag.Format(
            groupCode,
            string.IsNullOrWhiteSpace(point.Name) ? point.Code : point.Name);
    }

    private static string ResolveGroupCode(
        string? groupId,
        IReadOnlyList<PointsConfig.PointGroupEntry> groups)
        => groups.FirstOrDefault(group =>
            string.Equals(group.Id, groupId, StringComparison.OrdinalIgnoreCase))?.Code?.Trim() ?? string.Empty;

    private static string DisplayName(PointsConfig.PointEntry point)
        => string.IsNullOrWhiteSpace(point.Name) ? point.Code : point.Name;

    private static string PointFingerprintLine(PointsConfig.PointEntry point)
        => string.Join(
            "\u001f",
            point.Id?.Trim() ?? string.Empty,
            point.DeviceId?.Trim() ?? string.Empty,
            point.GroupId?.Trim() ?? string.Empty,
            point.Code?.Trim() ?? string.Empty,
            point.Name?.Trim() ?? string.Empty,
            point.Address?.Trim() ?? string.Empty,
            point.DataType?.Trim() ?? string.Empty,
            point.IsWritable.ToString(),
            point.RiskLevel.ToString(),
            point.WritePolicy.ToString(),
            point.RawMin?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            point.RawMax?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            point.EngMin?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            point.EngMax?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            point.Description?.Trim() ?? string.Empty);

    private static PointsConfig.PointEntry ClonePoint(PointsConfig.PointEntry source)
        => new()
        {
            Id = source.Id,
            Code = source.Code,
            Name = source.Name,
            Protocol = source.Protocol,
            DeviceId = source.DeviceId,
            DeviceCode = source.DeviceCode,
            GroupId = source.GroupId,
            GroupCode = source.GroupCode,
            Address = source.Address,
            AddressDefinition = CloneAddressDefinition(source.AddressDefinition),
            OriginalAddress = source.OriginalAddress,
            CanonicalAddress = source.CanonicalAddress,
            DataType = source.DataType,
            S7DecodeOptions = CloneDecodeOptions(source.S7DecodeOptions),
            WritePolicy = source.WritePolicy,
            IsWritable = source.IsWritable,
            RiskLevel = source.RiskLevel,
            RawMin = source.RawMin,
            RawMax = source.RawMax,
            EngMin = source.EngMin,
            EngMax = source.EngMax,
            Description = source.Description
        };

    private static DecodeOptions? CloneDecodeOptions(DecodeOptions? source)
        => source is null
            ? null
            : new DecodeOptions
            {
                ByteOrder = source.ByteOrder,
                WordOrder = source.WordOrder
            };

    private static PointAddressDefinition? CloneAddressDefinition(PointAddressDefinition? source)
        => source is null
            ? null
            : new PointAddressDefinition
            {
                Area = source.Area,
                Offset = source.Offset,
                BitIndex = source.BitIndex,
                DbNumber = source.DbNumber,
                ByteOffset = source.ByteOffset,
                BitOffset = source.BitOffset,
                LogicalAddress = source.LogicalAddress
            };
}
