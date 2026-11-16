using System.IO.Ports;

namespace XXX.TestBench.App.ViewModels;

/// <summary>
/// 为串口通道编辑器提供封闭的端口号和波特率选项。
/// </summary>
internal static class SerialPortOptionProvider
{
    private static readonly int[] StandardBaudRates =
    [
        1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200,
        230400, 460800, 921600
    ];

    public static IReadOnlyList<string> GetPortNames(string? currentPortName = null)
    {
        var portNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var portName in SerialPort.GetPortNames())
            {
                if (!string.IsNullOrWhiteSpace(portName))
                    portNames.Add(portName.Trim());
            }
        }
        catch (Exception)
        {
            // 端口枚举失败不应阻止打开配置界面，下面的常用候选仍可用于配置。
        }

        foreach (var portName in GetCommonPortNames())
            portNames.Add(portName);

        if (!string.IsNullOrWhiteSpace(currentPortName))
            portNames.Add(currentPortName.Trim());

        return portNames
            .OrderBy(GetPortSortKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(portName => portName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<int> GetBaudRates(int? currentBaudRate = null)
    {
        var baudRates = new HashSet<int>(StandardBaudRates);
        if (currentBaudRate is > 0)
            baudRates.Add(currentBaudRate.Value);
        return baudRates.OrderBy(rate => rate).ToArray();
    }

    private static IEnumerable<string> GetCommonPortNames()
    {
        if (OperatingSystem.IsWindows())
            return Enumerable.Range(1, 32).Select(index => $"COM{index}");

        return
        [
            "/dev/ttyS0",
            "/dev/ttyS1",
            "/dev/ttyUSB0",
            "/dev/ttyUSB1",
            "/dev/ttyACM0",
            "/dev/ttyACM1"
        ];
    }

    private static string GetPortSortKey(string portName)
    {
        var suffixStart = portName.Length;
        while (suffixStart > 0 && char.IsDigit(portName[suffixStart - 1]))
            suffixStart--;

        if (suffixStart < portName.Length
            && int.TryParse(portName[suffixStart..], out var suffix))
            return $"{portName[..suffixStart]}\0{suffix:D8}";

        return portName;
    }
}
