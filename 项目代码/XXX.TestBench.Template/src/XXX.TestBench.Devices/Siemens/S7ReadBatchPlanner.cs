using S7.Net;
using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Devices;

namespace XXX.TestBench.Devices.Siemens;

public sealed record S7ReadPlanItem(DevicePoint Point, S7Address Address, int OffsetInBlock);

public sealed record S7ReadBlock(
    DataType Area,
    int DbNumber,
    int StartByte,
    int ByteCount,
    IReadOnlyList<S7ReadPlanItem> Items);

/// <summary>
/// 按 Area+DB 合并 S7 读请求。相邻间隔不超过 8 字节，单块不超过 222 字节且不超过协商 PDU 安全上限。
/// </summary>
public static class S7ReadBatchPlanner
{
    public static IReadOnlyList<S7ReadBlock> Plan(IEnumerable<DevicePoint> points, int maxPduSize = 960)
    {
        ArgumentNullException.ThrowIfNull(points);
        var maxBlockBytes = Math.Max(1, Math.Min(222, maxPduSize - 18));
        var candidates = points.Select(point =>
        {
            var address = S7AddressParser.Parse(point.Address);
            if (!SiemensS7TypeCapabilities.IsShapeCompatible(address.Shape, point.DataType, out var reason))
                throw new DomainException($"点位“{DisplayPoint(point)}”：{reason}");
            if (address.ByteCount > maxBlockBytes)
                throw new DomainException($"点位“{DisplayPoint(point)}”的地址长度 {address.ByteCount} 超过当前 S7 PDU 可用块长度 {maxBlockBytes}");
            return new Candidate(point, address, address.StartByte, address.ByteCount);
        })
        .GroupBy(item => (item.Address.Area, item.Address.DbNumber))
        .ToList();

        var blocks = new List<S7ReadBlock>();
        foreach (var group in candidates)
        {
            var ordered = group.OrderBy(item => item.StartByte).ThenBy(item => item.Point.PointId, StringComparer.OrdinalIgnoreCase).ToList();
            var current = new List<Candidate>();
            var start = 0;
            var end = 0;
            foreach (var candidate in ordered)
            {
                if (current.Count == 0)
                {
                    current.Add(candidate);
                    start = candidate.StartByte;
                    end = candidate.StartByte + candidate.ByteCount;
                    continue;
                }

                var candidateEnd = Math.Max(end, candidate.StartByte + candidate.ByteCount);
                var gap = candidate.StartByte - end;
                if (gap <= 8 && candidateEnd - start <= maxBlockBytes)
                {
                    current.Add(candidate);
                    end = candidateEnd;
                }
                else
                {
                    blocks.Add(CreateBlock(group.Key.Area, group.Key.DbNumber ?? 0, start, end, current));
                    current.Clear();
                    current.Add(candidate);
                    start = candidate.StartByte;
                    end = candidate.StartByte + candidate.ByteCount;
                }
            }
            if (current.Count > 0)
                blocks.Add(CreateBlock(group.Key.Area, group.Key.DbNumber ?? 0, start, end, current));
        }
        return blocks;
    }

    private static S7ReadBlock CreateBlock(DataType area, int dbNumber, int start, int end, IReadOnlyList<Candidate> candidates)
    {
        var items = candidates
            .Select(candidate => new S7ReadPlanItem(candidate.Point, candidate.Address, candidate.StartByte - start))
            .ToList();
        return new S7ReadBlock(area, dbNumber, start, end - start, items);
    }

    private static string DisplayPoint(DevicePoint point)
        => string.IsNullOrWhiteSpace(point.Name) ? point.Code : point.Name;

    private sealed record Candidate(DevicePoint Point, S7Address Address, int StartByte, int ByteCount);
}
