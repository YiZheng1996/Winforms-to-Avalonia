using System.Net;
using System.Net.Sockets;
using XXX.TestBench.App.ViewModels;
using XXX.TestBench.Core.Domain.Devices;

namespace XXX.TestBench.App.Tests;

public sealed class DeviceConnectionTestResultFormatterTests
{
    [Fact]
    public void Success_UsesChineseLabelsAndExplainsS7Endpoint()
    {
        var result = new DeviceConnectionTestResult(
            true,
            "192.168.1.10:102 rack=0 slot=1",
            TimeSpan.FromMilliseconds(38),
            960,
            null);

        var text = DeviceConnectionTestResultFormatter.Format(result);

        Assert.Equal(
            "连接成功：目标地址 192.168.1.10:102，机架号 0，插槽号 1，协商报文长度 960，耗时 38 毫秒",
            text);
        Assert.DoesNotContain("rack=", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("slot=", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PDU", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnglishSocketError_UsesChineseActionableGuidance()
    {
        var result = new DeviceConnectionTestResult(
            false,
            "192.168.1.10:102 rack=0 slot=1",
            TimeSpan.Zero,
            null,
            "Connection refused");

        var text = DeviceConnectionTestResultFormatter.Format(result);

        Assert.Equal(
            "连接失败：无法建立网络连接，请检查 PLC 地址、端口和网络（目标地址 192.168.1.10:102，机架号 0，插槽号 1）",
            text);
    }

    [Fact]
    public void TimeoutAndSocketExceptions_UseChineseGuidance()
    {
        Assert.Equal(
            "连接失败：连接超时（3000 毫秒）",
            DeviceConnectionTestResultFormatter.FormatTimeout(3000));
        Assert.Equal(
            "连接失败：无法建立网络连接，请检查 PLC 地址、端口和网络",
            DeviceConnectionTestResultFormatter.FormatException(
                new SocketException((int)SocketError.ConnectionRefused)));
        Assert.Equal(
            "连接失败：无法建立网络连接，请检查 PLC 地址、端口和网络",
            DeviceConnectionTestResultFormatter.FormatException(
                new WebException("connection refused")));
    }
}
