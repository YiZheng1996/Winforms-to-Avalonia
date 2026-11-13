using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Infrastructure.Configuration;

namespace XXX.TestBench.Integration.Tests;

public sealed class SimplifiedDevicePointTemplateTests
{
    [Fact]
    public async Task CsvImport_UsesGroupPointTagAndDirectAddress()
    {
        var path = TempFile();
        try
        {
            await File.WriteAllTextAsync(path, string.Join('\n',
                Headers(),
                "AI.L32,DB144.DBD88,单精度浮点数,,,只读,0,10000,0,10,厂房进气压力"));

            var result = await new DevicePointImporter().ImportAsync(path);

            Assert.True(result.IsValid, string.Join("；", result.Issues.Select(issue => issue.Message)));
            var point = Assert.Single(result.Points);
            Assert.Equal("AI.L32", point.Code);
            Assert.Equal("L32", point.Name);
            Assert.Equal("AI", point.GroupCode);
            Assert.Empty(point.DeviceCode);
            Assert.Equal("DB144.DBD88", point.Address);
            Assert.Equal("DB144.DBD88", point.AddressDefinition?.LogicalAddress);
            Assert.Equal("Float32", point.RawDataType);
            Assert.False(point.IsWritable);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task CsvImport_UsesDevicePrefixOnlyWhenTheFileContainsMultipleDevices()
    {
        var path = TempFile();
        try
        {
            await File.WriteAllTextAsync(path, string.Join('\n',
                Headers(),
                "S71500/AI.L32,DB144.DBD88,单精度浮点数,,,只读,0,10000,0,10,压力",
                "S7200/AI.L32,VW5022,16 位无符号整数,,,只读,0,10000,0,10,状态"));

            var result = await new DevicePointImporter().ImportAsync(path);

            Assert.True(result.IsValid, string.Join("；", result.Issues.Select(issue => issue.Message)));
            Assert.Equal(new[] { "S71500", "S7200" }, result.Points.Select(point => point.DeviceCode));
            Assert.Equal(new[] { "S71500/AI.L32", "S7200/AI.L32" }, result.Points.Select(point => point.Code));
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task CsvImport_RejectsEmptyPointTag()
    {
        var path = TempFile();
        try
        {
            await File.WriteAllTextAsync(path, string.Join('\n',
                Headers(),
                ",DB144.DBD88,单精度浮点数,,,只读,0,10000,0,10,压力"));

            var result = await new DevicePointImporter().ImportAsync(path);

            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, issue => issue.Message.Contains("点位标签不能为空", StringComparison.Ordinal));
        }
        finally { TryDelete(path); }
    }

    private static string Headers()
        => string.Join(',', DevicePointTemplateDefinition.Columns.Select(column => column.Header));

    private static string TempFile()
        => Path.Combine(Path.GetTempPath(), "testbench-point-simple-" + Guid.NewGuid().ToString("N") + ".csv");

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

}
