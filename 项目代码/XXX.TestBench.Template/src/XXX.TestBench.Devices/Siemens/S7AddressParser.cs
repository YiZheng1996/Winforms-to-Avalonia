using System.Text.RegularExpressions;
using S7.Net;
using XXX.TestBench.Core.Common;

namespace XXX.TestBench.Devices.Siemens;

public enum S7AddressShape
{
    Bit = 1,
    Byte = 2,
    Word = 3,
    DoubleWord = 4
}

/// <summary>
/// 已规范化的 S7 地址。V 区是 S7-200 SMART 的逻辑写法，底层按 DB2 访问。
/// </summary>
public sealed record S7Address(
    string Original,
    string Canonical,
    DataType Area,
    int? DbNumber,
    int StartByte,
    int? BitIndex,
    S7AddressShape Shape)
{
    public int ByteCount => Shape switch
    {
        S7AddressShape.Bit or S7AddressShape.Byte => 1,
        S7AddressShape.Word => 2,
        S7AddressShape.DoubleWord => 4,
        _ => 0
    };
}

/// <summary>
/// 统一解析 S7-1200/1500 与 S7-200 SMART 的位/字节/字/双字地址。
/// </summary>
public static class S7AddressParser
{
    private static readonly Regex DbPattern = new(
        @"^DB(?<db>\d+)\.(?<kind>DBX|DBB|DBW|DBD)(?<offset>\d+)(?:\.(?<bit>\d+))?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex AreaPattern = new(
        @"^(?<area>[MIQ])(?<kind>B|W|D)?(?<offset>\d+)(?:\.(?<bit>\d+))?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex VPattern = new(
        @"^V(?<kind>B|W|D)?(?<offset>\d+)(?:\.(?<bit>\d+))?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static S7Address Parse(string address)
    {
        if (TryParse(address, out var parsed, out var error)) return parsed!;
        throw new DomainException(error ?? $"S7 地址无效：{address}");
    }

    public static bool TryParse(string? address, out S7Address? parsed, out string? error)
    {
        parsed = null;
        error = null;
        var original = address ?? string.Empty;
        var normalized = Normalize(original);
        if (normalized.Length == 0)
        {
            error = "S7 地址不能为空";
            return false;
        }

        var dbMatch = DbPattern.Match(normalized);
        if (dbMatch.Success)
        {
            if (!TryParseNonNegative(dbMatch.Groups["db"].Value, "DB 号", out var db, out error))
                return false;
            if (!TryParseNonNegative(dbMatch.Groups["offset"].Value, "地址偏移", out var offset, out error))
                return false;
            var kind = dbMatch.Groups["kind"].Value.ToUpperInvariant();
            var hasBit = dbMatch.Groups["bit"].Success;
            var shape = kind switch
            {
                "DBX" => S7AddressShape.Bit,
                "DBB" => S7AddressShape.Byte,
                "DBW" => S7AddressShape.Word,
                "DBD" => S7AddressShape.DoubleWord,
                _ => (S7AddressShape)0
            };
            if (shape == 0) return Fail($"S7 DB 地址类型不受支持：{kind}", out parsed, out error);
            if (shape == S7AddressShape.Bit && !hasBit)
                return Fail("DBX 地址必须包含 .bit", out parsed, out error);
            if (shape != S7AddressShape.Bit && hasBit)
                return Fail($"{kind} 地址不能包含 bit 位", out parsed, out error);
            if (!TryParseBit(dbMatch.Groups["bit"].Value, hasBit, out var bit, out error))
                return false;
            parsed = new S7Address(original, normalized, DataType.DataBlock, db, offset, bit, shape);
            return true;
        }

        var vMatch = VPattern.Match(normalized);
        if (vMatch.Success)
            return ParseAreaMatch(original, normalized, vMatch, DataType.DataBlock, 2, "V", out parsed, out error);

        var areaMatch = AreaPattern.Match(normalized);
        if (areaMatch.Success)
        {
            var area = areaMatch.Groups["area"].Value.ToUpperInvariant() switch
            {
                "M" => DataType.Memory,
                "I" => DataType.Input,
                "Q" => DataType.Output,
                _ => (DataType)0
            };
            return ParseAreaMatch(original, normalized, areaMatch, area, null,
                areaMatch.Groups["area"].Value.ToUpperInvariant(), out parsed, out error);
        }

        return Fail($"S7 地址格式不受支持：{original}", out parsed, out error);
    }

    private static bool ParseAreaMatch(
        string original,
        string normalized,
        Match match,
        DataType area,
        int? dbNumber,
        string areaName,
        out S7Address? parsed,
        out string? error)
    {
        parsed = null;
        error = null;
        var kind = match.Groups["kind"].Value.ToUpperInvariant();
        if (!TryParseNonNegative(match.Groups["offset"].Value, "地址偏移", out var offset, out error))
            return false;
        var hasBit = match.Groups["bit"].Success;
        if (string.IsNullOrEmpty(kind) && !hasBit)
            return Fail($"{areaName} 地址必须明确 B/W/D 或 bit 形式", out parsed, out error);

        var shape = hasBit
            ? S7AddressShape.Bit
            : kind switch
            {
                "B" => S7AddressShape.Byte,
                "W" => S7AddressShape.Word,
                "D" => S7AddressShape.DoubleWord,
                _ => (S7AddressShape)0
            };
        if (shape == 0)
            return Fail($"S7 地址类型不受支持：{kind}", out parsed, out error);
        if (!TryParseBit(match.Groups["bit"].Value, hasBit, out var bit, out error))
            return false;
        parsed = new S7Address(original, normalized, area, dbNumber, offset, bit, shape);
        return true;
    }

    private static bool TryParseBit(string text, bool present, out int? bit, out string? error)
    {
        bit = null;
        error = null;
        if (!present) return true;
        if (!int.TryParse(text, out var value) || value is < 0 or > 7)
        {
            error = $"S7 bit 位必须在 0-7 范围内：{text}";
            return false;
        }
        bit = value;
        return true;
    }

    private static bool TryParseNonNegative(
        string text,
        string label,
        out int value,
        out string? error)
    {
        if (int.TryParse(text, out value) && value >= 0)
        {
            error = null;
            return true;
        }

        value = 0;
        error = $"S7 {label}无效：{text}";
        return false;
    }

    private static string Normalize(string value)
        => value.Trim()
            .Replace("％", "%", StringComparison.Ordinal)
            .Replace("　", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .TrimStart('%')
            .ToUpperInvariant();

    private static bool Fail(string message, out S7Address? parsed, out string? error)
    {
        parsed = null;
        error = message;
        return false;
    }
}
