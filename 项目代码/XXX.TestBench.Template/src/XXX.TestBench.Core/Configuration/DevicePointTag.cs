namespace XXX.TestBench.Core.Configuration;

/// <summary>
/// 点位模板中的客户可读标签。
///
/// 单设备模板使用“分组.点位”，跨设备模板在前面增加“设备编码/”：
/// AI.L32 或 S71500/AI.L32。设备、分组和点位编码仍由系统内部维护，
/// 不要求客户在文件中重复填写多列身份字段。
/// </summary>
public static class DevicePointTag
{
    /// <summary>
    /// 解析点位标签。没有“.”时表示未分组点位；“设备编码/”是跨设备模板的可选前缀。
    /// </summary>
    public static bool TryParse(
        string? value,
        out ParsedTag tag,
        out string error)
    {
        tag = null!;
        error = string.Empty;
        var text = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "点位标签不能为空";
            return false;
        }

        var slashIndex = text.IndexOf('/');
        if (slashIndex >= 0)
        {
            if (slashIndex == 0 || slashIndex == text.Length - 1
                || text.IndexOf('/', slashIndex + 1) >= 0)
            {
                error = "点位标签的设备前缀格式不正确，应为“设备编码/分组.点位”";
                return false;
            }
        }

        var deviceCode = slashIndex < 0 ? null : text[..slashIndex].Trim();
        var localTag = slashIndex < 0 ? text : text[(slashIndex + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(localTag))
        {
            error = "点位标签中的点位名称不能为空";
            return false;
        }

        var dotIndex = localTag.IndexOf('.');
        string? groupCode = null;
        string pointName;
        if (dotIndex < 0)
        {
            pointName = localTag;
        }
        else
        {
            groupCode = localTag[..dotIndex].Trim();
            pointName = localTag[(dotIndex + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(groupCode))
            {
                error = "点位标签中的分组名称不能为空";
                return false;
            }
            if (string.IsNullOrWhiteSpace(pointName))
            {
                error = "点位标签中的点位名称不能为空";
                return false;
            }
        }

        if (ContainsControlCharacter(deviceCode) || ContainsControlCharacter(groupCode)
            || ContainsControlCharacter(pointName))
        {
            error = "点位标签不能包含换行或控制字符";
            return false;
        }

        tag = new ParsedTag(deviceCode, groupCode, pointName);
        return true;
    }

    /// <summary>
    /// 生成不带设备前缀的单设备标签。
    /// </summary>
    public static string Format(string? groupCode, string? pointName)
    {
        var name = pointName?.Trim() ?? string.Empty;
        var group = groupCode?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(group)
            || string.Equals(group, "DEFAULT", StringComparison.OrdinalIgnoreCase)
            ? name
            : group + "." + name;
    }

    /// <summary>
    /// 生成跨设备模板标签。设备编码为空时自动退化为单设备格式。
    /// </summary>
    public static string Format(string? deviceCode, string? groupCode, string? pointName)
    {
        var local = Format(groupCode, pointName);
        var device = deviceCode?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(device) ? local : device + "/" + local;
    }

    private static bool ContainsControlCharacter(string? value)
        => !string.IsNullOrEmpty(value) && value.Any(char.IsControl);

    /// <summary>
    /// 标签解析结果。
    /// </summary>
    public sealed record ParsedTag(
        string? DeviceCode,
        string? GroupCode,
        string PointName)
    {
        public string LocalTag => Format(GroupCode, PointName);
        public string FullTag => Format(DeviceCode, GroupCode, PointName);
    }
}
