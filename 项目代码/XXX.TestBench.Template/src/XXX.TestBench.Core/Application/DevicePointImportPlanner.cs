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
        IReadOnlyList<PointsConfig.PointGroupEntry> currentGroups)
    {
        ArgumentNullException.ThrowIfNull(importedRows);
        ArgumentNullException.ThrowIfNull(currentPoints);
        ArgumentNullException.ThrowIfNull(currentGroups);

        var merged = currentPoints.Select(ClonePoint).ToList();
        var rows = new List<DevicePointImportPlanRow>();
        foreach (var sourceRow in importedRows)
        {
            var incoming = ClonePoint(sourceRow.Entry);
            var issues = new List<string>();
            var existing = FindMatch(incoming, merged, currentGroups);

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
                        CanonicalAddress(point),
                        CanonicalAddress(candidate),
                        StringComparison.OrdinalIgnoreCase));
                if (addressOwner is not null)
                    issues.Add($"设备内地址已被“{DisplayName(addressOwner)}”占用");
            }

            if (candidate is not null)
            {
                var duplicateIncoming = rows.FirstOrDefault(row =>
                    row.Operation != DevicePointImportOperationKind.Conflict
                    && row.Candidate is not null
                    && IsSameDevice(row.Candidate, candidate)
                    && !IsSameIdentity(row.Candidate, candidate)
                    && string.Equals(
                        CanonicalAddress(row.Candidate),
                        CanonicalAddress(candidate),
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
            RawDataType = string.IsNullOrWhiteSpace(incoming.RawDataType)
                ? incoming.DataType?.Trim() ?? string.Empty
                : incoming.RawDataType.Trim(),
            DecodeOptions = CloneDecodeOptions(incoming.DecodeOptions),
            ByteOrderSource = incoming.ByteOrderSource,
            WordOrderSource = incoming.WordOrderSource,
            WritePolicy = incoming.WritePolicy,
            IsWritable = incoming.IsWritable,
            RiskLevel = WriteRiskLevel.Normal,
            Scale = CloneScale(incoming.Scale),
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
        candidate.RawDataType = string.IsNullOrWhiteSpace(incoming.RawDataType)
            ? candidate.DataType
            : incoming.RawDataType.Trim();
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
        candidate.Scale = CloneScale(incoming.Scale);
        // 模板不承载的字段必须保留现有点位安全边界。
        candidate.WritePolicy = existing.WritePolicy;
        // v6 模板已正式承载 Modbus 字节序/字序；结构化 Modbus 行的值必须
        // 进入候选配置。其他协议的两列为空时，继续保留既有解码方式，避免
        // 用户仅整理地址/量程就意外改变 S7 点位。
        candidate.DecodeOptions = isStructuredModbus
            ? CloneDecodeOptions(incoming.DecodeOptions)
            : CloneDecodeOptions(existing.DecodeOptions);
        candidate.ByteOrderSource = incoming.ByteOrderSource;
        candidate.WordOrderSource = incoming.WordOrderSource;
        candidate.Protocol = string.IsNullOrWhiteSpace(existing.Protocol) ? incoming.Protocol : existing.Protocol;
        return candidate;
    }

    private static bool IsEquivalent(PointsConfig.PointEntry left, PointsConfig.PointEntry right)
        => string.Equals(left.Code?.Trim(), right.Code?.Trim(), StringComparison.OrdinalIgnoreCase)
           && string.Equals(left.Name?.Trim(), right.Name?.Trim(), StringComparison.OrdinalIgnoreCase)
           && string.Equals(left.Address?.Trim(), right.Address?.Trim(), StringComparison.OrdinalIgnoreCase)
           && string.Equals(left.DataType?.Trim(), right.DataType?.Trim(), StringComparison.OrdinalIgnoreCase)
           && string.Equals(left.RawDataType?.Trim(), right.RawDataType?.Trim(), StringComparison.OrdinalIgnoreCase)
           && left.DecodeOptions?.ByteOrder == right.DecodeOptions?.ByteOrder
           && left.DecodeOptions?.WordOrder == right.DecodeOptions?.WordOrder
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

    private static bool IsSameIdentity(PointsConfig.PointEntry left, PointsConfig.PointEntry right)
        => !string.IsNullOrWhiteSpace(left.Id)
           && !string.IsNullOrWhiteSpace(right.Id)
           && string.Equals(left.Id.Trim(), right.Id.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string CanonicalAddress(PointsConfig.PointEntry point)
        => point.AddressDefinition?.ToCanonical(point.Address ?? string.Empty)
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
            point.RawDataType?.Trim() ?? string.Empty,
            point.IsWritable.ToString(),
            point.RiskLevel.ToString(),
            point.WritePolicy.ToString(),
            point.DecodeOptions?.ByteOrder.ToString() ?? string.Empty,
            point.DecodeOptions?.WordOrder.ToString() ?? string.Empty,
            point.EffectiveRawMin?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            point.EffectiveRawMax?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            point.EffectiveEngMin?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            point.EffectiveEngMax?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
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
            RawDataType = source.RawDataType,
            DecodeOptions = CloneDecodeOptions(source.DecodeOptions),
            ByteOrderSource = source.ByteOrderSource,
            WordOrderSource = source.WordOrderSource,
            WritePolicy = source.WritePolicy,
            IsWritable = source.IsWritable,
            RiskLevel = source.RiskLevel,
            Scale = CloneScale(source.Scale),
            RawMin = source.RawMin,
            RawMax = source.RawMax,
            EngMin = source.EngMin,
            EngMax = source.EngMax,
            Description = source.Description
        };

    private static DecodeOptions CloneDecodeOptions(DecodeOptions? source)
        => source is null
            ? new DecodeOptions()
            : new DecodeOptions
            {
                ByteOrder = source.ByteOrder,
                WordOrder = source.WordOrder
            };

    private static PointsConfig.ScaleValues? CloneScale(PointsConfig.ScaleValues? source)
        => source is null
            ? null
            : new PointsConfig.ScaleValues
            {
                RawMin = source.RawMin,
                RawMax = source.RawMax,
                EngMin = source.EngMin,
                EngMax = source.EngMax
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
