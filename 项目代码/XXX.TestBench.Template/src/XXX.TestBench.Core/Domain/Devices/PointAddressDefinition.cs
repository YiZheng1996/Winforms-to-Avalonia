namespace XXX.TestBench.Core.Domain.Devices;

/// <summary>
/// 驱动无关的结构化地址字段。不同驱动只读取自己支持的字段并生成规范地址。
/// </summary>
public sealed class PointAddressDefinition
{
    public string Area { get; set; } = string.Empty;
    public int? Offset { get; set; }
    public int? BitIndex { get; set; }
    public int? DbNumber { get; set; }
    public int? ByteOffset { get; set; }
    public int? BitOffset { get; set; }
    public string LogicalAddress { get; set; } = string.Empty;

    /// <summary>
    /// 生成用于同设备地址唯一性校验的稳定文本。
    /// </summary>
    public string ToCanonical(string fallback)
    {
        if (!string.IsNullOrWhiteSpace(LogicalAddress))
            return "logical:" + LogicalAddress.Trim();

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(Area)) parts.Add("area=" + Area.Trim().ToLowerInvariant());
        if (Offset.HasValue) parts.Add("offset=" + Offset.Value);
        if (BitIndex.HasValue) parts.Add("bit=" + BitIndex.Value);
        if (DbNumber.HasValue) parts.Add("db=" + DbNumber.Value);
        if (ByteOffset.HasValue) parts.Add("byte=" + ByteOffset.Value);
        if (BitOffset.HasValue) parts.Add("bitOffset=" + BitOffset.Value);
        return parts.Count == 0 ? fallback.Trim() : string.Join(';', parts);
    }
}
/// <summary>
/// 原始数据的字节序。
/// </summary>
public enum ByteOrder
{
    Unknown = 0,
    BigEndian = 1,
    LittleEndian = 2
}

/// <summary>
/// 32 位值的寄存器/字序。
/// </summary>
public enum WordOrder
{
    None = 0,
    HighWordFirst = 1,
    LowWordFirst = 2
}

public sealed class DecodeOptions
{
    public ByteOrder ByteOrder { get; set; } = ByteOrder.BigEndian;
    public WordOrder WordOrder { get; set; } = WordOrder.None;
}

/// <summary>
/// 第一版允许的写入确认策略。
/// </summary>
public enum PointWritePolicy
{
    ReadBackEqual = 1
}
