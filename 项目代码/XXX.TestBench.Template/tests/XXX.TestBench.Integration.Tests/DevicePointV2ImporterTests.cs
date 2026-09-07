using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Infrastructure.Configuration;

namespace XXX.TestBench.Integration.Tests;

public sealed class DevicePointV2ImporterTests
{
    [Fact]
    public async Task V2CsvImportKeepsDeviceCodeAndStructuredAddress()
    {
        var path = TempFile();
        try
        {
            await File.WriteAllTextAsync(path, string.Join('\n',
                Headers(),
                Row("", "AI_Pressure", "压力", "PLC1", "PRESSURE", "仿真逻辑地址", "sim.pressure", "小数", "大端", "无", "MPa", "0", "10000", "0", "10", "否", "是", "普通", "输入")));

            var result = await new DevicePointImporter().ImportAsync(path);

            Assert.True(result.IsValid, string.Join("；", result.Issues.Select(issue => issue.Message)));
            var point = Assert.Single(result.Points);
            Assert.Equal("PLC1", point.DeviceCode);
            Assert.Equal("Simulation", point.Protocol);
            Assert.Equal("Decimal", point.RawDataType);
            Assert.Equal("Simulation", point.AddressDefinition?.Area);
            Assert.Equal("sim.pressure", point.AddressDefinition?.LogicalAddress);
            Assert.Equal("MPa", point.Unit);
            Assert.True(string.IsNullOrWhiteSpace(point.Id));
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task V2CsvAllowsSameAddressOnDifferentDevices()
    {
        var path = TempFile();
        try
        {
            await File.WriteAllTextAsync(path, string.Join('\n',
                Headers(),
                Row("", "P1", "设备一压力", "PLC1", "DEFAULT", "仿真逻辑地址", "sim.pressure", "单精度浮点数", "大端", "无", "MPa", "", "", "", "", "否", "是", "普通", ""),
                Row("", "P2", "设备二压力", "PLC2", "DEFAULT", "仿真逻辑地址", "sim.pressure", "单精度浮点数", "大端", "无", "MPa", "", "", "", "", "否", "是", "普通", "")));

            var result = await new DevicePointImporter().ImportAsync(path);

            Assert.True(result.IsValid, string.Join("；", result.Issues.Select(issue => issue.Message)));
            Assert.Equal(new[] { "PLC1", "PLC2" }, result.Points.Select(point => point.DeviceCode));
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task V2CsvRejectsDuplicateAddressOnSameDevice()
    {
        var path = TempFile();
        try
        {
            var row = Row("", "P1", "压力", "PLC1", "DEFAULT", "仿真逻辑地址", "sim.pressure", "小数", "大端", "无", "MPa", "", "", "", "", "否", "是", "普通", "");
            await File.WriteAllTextAsync(path, string.Join('\n', Headers(), row, row.Replace(",P1,", ",P2,")));

            var result = await new DevicePointImporter().ImportAsync(path);

            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, issue => issue.Message.Contains("同一设备的地址参数重复", StringComparison.Ordinal));
        }
        finally { TryDelete(path); }
    }

    private static string Headers()
        => string.Join(',', DevicePointTemplateDefinition.Columns.Select(column => column.Header));

    private static string Row(params string[] values) => string.Join(',', values);

    private static string TempFile()
        => Path.Combine(Path.GetTempPath(), "testbench-point-v2-import-" + Guid.NewGuid().ToString("N") + ".csv");

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
