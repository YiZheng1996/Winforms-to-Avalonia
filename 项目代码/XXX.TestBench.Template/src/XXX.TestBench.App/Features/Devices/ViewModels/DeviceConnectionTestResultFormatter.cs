using System.Globalization;
using System.Net.Sockets;
using XXX.TestBench.Core.Domain.Devices;

namespace XXX.TestBench.App.ViewModels;

/// <summary>
/// 将通信测试结果转换为客户可直接理解的中文提示。
/// 底层异常只保留在日志边界，不直接展示给客户。
/// </summary>
public static class DeviceConnectionTestResultFormatter
{
    public static string Format(DeviceConnectionTestResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var target = FormatEndpoint(result.Endpoint);
        if (!result.Executed)
            return result.Summary;

        if (result.DriverKey is DriverKeyCatalog.ModbusTcp or DriverKeyCatalog.ModbusRtu)
        {
            if (result.Ok)
                return $"连接成功：{result.Summary}（目标地址 {target}，耗时 {result.Elapsed.TotalMilliseconds:0} 毫秒）";
            return $"连接失败：{FormatModbusDetail(result.Error)}（目标地址 {target}）";
        }

        if (result.Ok)
        {
            var pdu = result.NegotiatedPduSize?.ToString(CultureInfo.InvariantCulture) ?? "未返回";
            return $"连接成功：目标地址 {target}，协商报文长度 {pdu}，耗时 {result.Elapsed.TotalMilliseconds:0} 毫秒";
        }

        return $"连接失败：{FormatDetail(result.Error)}（目标地址 {target}）";
    }

    public static string FormatTimeout(int? timeoutMs = null)
        => timeoutMs is > 0
            ? $"连接失败：连接超时（{timeoutMs.Value.ToString(CultureInfo.InvariantCulture)} 毫秒）"
            : "连接失败：请求超时";

    public static string FormatException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is OperationCanceledException or TimeoutException)
            return FormatTimeout();
        if (exception is SocketException)
            return "连接失败：无法建立网络连接，请检查 PLC 地址、端口和网络";

        return $"连接失败：{FormatDetail(exception.Message)}";
    }

    private static string FormatEndpoint(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return "未配置";

        return endpoint.Trim()
            .Replace(" rack=", "，机架号 ", StringComparison.OrdinalIgnoreCase)
            .Replace(" slot=", "，插槽号 ", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatDetail(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
            return "未返回具体错误";

        var text = error.Trim()
            .Replace("连接测试失败：", string.Empty, StringComparison.Ordinal)
            .Replace("连接测试失败:", string.Empty, StringComparison.Ordinal)
            .Replace("连接测试超时", "连接超时", StringComparison.Ordinal)
            .Replace("当前设备不是 Siemens S7 驱动", "当前设备不是西门子 S7 驱动", StringComparison.Ordinal)
            .Replace(" ms", " 毫秒", StringComparison.OrdinalIgnoreCase);

        if (ContainsAny(text, "timeout", "timed out", "超时"))
            return text.Contains("毫", StringComparison.Ordinal) ? text : "连接超时";
        if (ContainsAny(text, "connection refused", "actively refused", "unreachable", "no such host", "name or service not known", "network is unreachable", "dns", "socket"))
            return "无法建立网络连接，请检查 PLC 地址、端口和网络";
        if (text.Contains("rack", StringComparison.OrdinalIgnoreCase)
            && text.Contains("slot", StringComparison.OrdinalIgnoreCase))
            return "PLC 机架号或插槽号不正确，请按现场配置核对";
        if (ContainsAny(text, "access denied", "forbidden", "拒绝访问"))
            return "PLC 拒绝访问，请检查通信权限和 PLC 设置";

        return ContainsChinese(text)
            ? text
            : "设备连接失败，请检查 PLC 地址、端口、机架号和插槽号";
    }

    private static string FormatModbusDetail(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
            return "未返回具体错误";
        var text = error.Trim()
            .Replace("连接测试失败：", string.Empty, StringComparison.Ordinal)
            .Replace("连接测试失败:", string.Empty, StringComparison.Ordinal)
            .Replace(" ms", " 毫秒", StringComparison.OrdinalIgnoreCase);
        if (ContainsAny(text, "timeout", "timed out", "超时"))
            return text.Contains("毫", StringComparison.Ordinal) ? text : "连接超时";
        if (ContainsAny(text, "refused", "unreachable", "no such host", "network is unreachable", "dns", "socket"))
            return "无法建立 Modbus 网络连接，请检查设备地址、端口和网络";
        if (ContainsAny(text, "access denied", "port", "串口", "serial", "COM"))
            return ContainsChinese(text) ? text : "无法打开 Modbus 串口，请检查串口名称、占用情况和权限";
        return ContainsChinese(text) ? text : "Modbus 设备连接失败，请检查地址、端口、串口参数和站号";
    }

    private static bool ContainsAny(string value, params string[] candidates)
        => candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsChinese(string value)
    {
        foreach (var character in value)
        {
            if (character is >= '\u4e00' and <= '\u9fff')
                return true;
        }

        return false;
    }
}
