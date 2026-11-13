using XXX.TestBench.Core.Common;
using XXX.TestBench.Core.Domain.Devices;

namespace XXX.TestBench.Devices.Modbus;

public sealed record ModbusReadBatchItem(
    DevicePoint Point,
    ModbusAddress Address,
    int OffsetInBatch,
    int ValueLength);

public sealed record ModbusReadBatch(
    ModbusArea Area,
    ushort Start,
    ushort Count,
    IReadOnlyList<ModbusReadBatchItem> Items);

/// <summary>
/// 把同一数据区的点位合并成符合 Modbus 功能码上限的读请求。
///
/// 批次不会跨数据区，也不会跨 65535 的协议地址边界；批次内部允许存在
/// 地址间隙，间隙只会增加一次请求的覆盖范围，不会改变任何点位的偏移语义。
/// </summary>
public static class ModbusReadBatchPlanner
{
    public const int MaximumBitCount = 2000;
    public const int MaximumRegisterCount = 125;

    public static IReadOnlyList<ModbusReadBatch> Plan(IEnumerable<DevicePoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        var parsed = points.Select(point =>
        {
            var address = ModbusAddressParser.Parse(point.Address);
            var type = ModbusTypeCapabilities.NormalizeCompatibilityType(address.Area, point.DataType);
            var length = address.Area.IsBitArea() ? 1 : ModbusTypeCapabilities.GetRegisterCount(type);
            if (!ModbusTypeCapabilities.TryValidate(address.Area, type, point.IsWritable, point.DecodeOptions, out var reason))
                throw new DomainException($"点位“{DisplayPoint(point)}”配置无效：{reason}");
            return (Point: point, Address: address, Length: length);
        }).ToList();

        var batches = new List<ModbusReadBatch>();
        foreach (var areaGroup in parsed.GroupBy(item => item.Address.Area))
        {
            var ordered = areaGroup.OrderBy(item => item.Address.Offset).ToList();
            var index = 0;
            while (index < ordered.Count)
            {
                var first = ordered[index];
                var batchStart = first.Address.Offset;
                var batchEnd = batchStart;
                var items = new List<ModbusReadBatchItem>();
                while (index < ordered.Count)
                {
                    var current = ordered[index];
                    var currentEnd = checked(current.Address.Offset + current.Length);
                    if (current.Address.Offset < batchEnd)
                        throw new DomainException($"同一 Modbus 数据区存在重叠地址：{current.Address.Canonical}");

                    var proposedEnd = Math.Max(batchEnd, currentEnd);
                    var max = areaGroup.Key.IsBitArea() ? MaximumBitCount : MaximumRegisterCount;
                    if (proposedEnd - batchStart > max && items.Count > 0)
                        break;
                    if (proposedEnd > ushort.MaxValue + 1)
                        throw new DomainException($"Modbus 地址范围超出 0-65535：{current.Address.Canonical}");

                    items.Add(new ModbusReadBatchItem(
                        current.Point,
                        current.Address,
                        current.Address.Offset - batchStart,
                        current.Length));
                    batchEnd = proposedEnd;
                    index++;
                }

                if (items.Count == 0)
                    throw new DomainException("Modbus 读批次为空");
                batches.Add(new ModbusReadBatch(
                    areaGroup.Key,
                    checked((ushort)batchStart),
                    checked((ushort)(batchEnd - batchStart)),
                    items));
            }
        }
        return batches;
    }

    private static string DisplayPoint(DevicePoint point)
        => string.IsNullOrWhiteSpace(point.Name) ? point.Code : point.Name;
}
