using XXX.TestBench.Core.Domain.Devices;

namespace XXX.TestBench.Core.Configuration;

/// <summary>
/// 一条地址范围冲突；重叠时会分别为两个点位返回一条问题。
/// </summary>
public sealed record DevicePointAddressRangeIssue(
    PointsConfig.PointEntry Point,
    PointsConfig.PointEntry? ConflictingPoint,
    string Message);

/// <summary>
/// 校验调用方已经按设备分组的点位范围，不了解任何厂商地址换算规则。
/// </summary>
public static class DevicePointAddressRangeValidator
{
    private const long AddressSpaceLength = (long)ushort.MaxValue + 1;

    public static IReadOnlyList<DevicePointAddressRangeIssue> Validate(
        IEnumerable<(PointsConfig.PointEntry Point, DevicePointAddressRange Range)> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        var entries = points.ToList();
        var issues = new List<DevicePointAddressRangeIssue>();

        foreach (var entry in entries)
        {
            var end = (long)entry.Range.Offset + entry.Range.Length;
            if (string.IsNullOrWhiteSpace(entry.Range.Area)
                || entry.Range.Offset < 0
                || entry.Range.Length <= 0
                || end > AddressSpaceLength)
            {
                issues.Add(new DevicePointAddressRangeIssue(
                    entry.Point,
                    null,
                    $"点位“{DisplayName(entry.Point)}”的地址占用范围无效：{FormatRange(entry.Range)}；范围必须处于 0～65535"));
            }
        }

        for (var leftIndex = 0; leftIndex < entries.Count; leftIndex++)
        {
            var left = entries[leftIndex];
            var leftEnd = (long)left.Range.Offset + left.Range.Length;
            if (!IsValid(left.Range, leftEnd))
                continue;

            for (var rightIndex = leftIndex + 1; rightIndex < entries.Count; rightIndex++)
            {
                var right = entries[rightIndex];
                if (!string.Equals(left.Range.Area, right.Range.Area, StringComparison.OrdinalIgnoreCase))
                    continue;
                var rightEnd = (long)right.Range.Offset + right.Range.Length;
                if (!IsValid(right.Range, rightEnd)
                    || left.Range.Offset >= rightEnd
                    || right.Range.Offset >= leftEnd)
                    continue;

                var message = $"点位“{DisplayName(left.Point)}”占用 {FormatRange(left.Range)}，与点位“{DisplayName(right.Point)}”占用 {FormatRange(right.Range)} 重叠";
                issues.Add(new DevicePointAddressRangeIssue(left.Point, right.Point, message));
                issues.Add(new DevicePointAddressRangeIssue(right.Point, left.Point, message));
            }
        }

        return issues;
    }

    public static bool Overlaps(DevicePointAddressRange left, DevicePointAddressRange right)
    {
        if (!string.Equals(left.Area, right.Area, StringComparison.OrdinalIgnoreCase))
            return false;
        var leftEnd = (long)left.Offset + left.Length;
        var rightEnd = (long)right.Offset + right.Length;
        return IsValid(left, leftEnd)
            && IsValid(right, rightEnd)
            && left.Offset < rightEnd
            && right.Offset < leftEnd;
    }

    public static string FormatRange(DevicePointAddressRange range)
        => $"{range.Area}[{range.Offset}, {(long)range.Offset + range.Length})";

    private static bool IsValid(DevicePointAddressRange range, long end)
        => !string.IsNullOrWhiteSpace(range.Area)
            && range.Offset >= 0
            && range.Length > 0
            && end <= AddressSpaceLength;

    private static string DisplayName(PointsConfig.PointEntry point)
        => string.IsNullOrWhiteSpace(point.Name) ? point.Code : point.Name;
}
