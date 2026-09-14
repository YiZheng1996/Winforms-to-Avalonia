using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Infrastructure.Configuration;

namespace XXX.TestBench.Integration.Tests;

public sealed class CurrentDevicePointTemplateContractTests
{
    [Theory]
    [InlineData("点位标识,点位编码,点位名称,设备编码,点位分组编码,地址类型,地址参数,原始数据类型,字节序,字序,单位,原始下限,原始上限,工程下限,工程上限,可写,启用,风险等级,说明")]
    [InlineData("点位编码,点位名称,通信方式,设备地址,数据类型,工程单位,原始下限,原始上限,工程下限,工程上限,是否允许写入,写入风险,是否启用,说明")]
    public async Task RemovedTemplateHeadersAreRejected(string removedHeader)
    {
        var path = TempFile();
        try
        {
            await File.WriteAllTextAsync(path, removedHeader + "\n");

            var result = await new DevicePointImporter().ImportAsync(path);

            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, issue => issue.Message.Contains("模板列数量不正确", StringComparison.Ordinal));
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task CurrentTemplateRejectsUnitAndEnabledColumnsEvenWhenOtherColumnsAreValid()
    {
        var path = TempFile();
        try
        {
            var headers = DevicePointTemplateDefinition.Columns
                .Select(column => column.Header)
                .Concat(new[] { "单位", "启用" });
            await File.WriteAllTextAsync(path, string.Join(',', headers) + "\n");

            var result = await new DevicePointImporter().ImportAsync(path);

            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, issue => issue.Message.Contains("模板列数量不正确", StringComparison.Ordinal));
        }
        finally { TryDelete(path); }
    }

    private static string TempFile()
        => Path.Combine(Path.GetTempPath(), "testbench-point-current-contract-" + Guid.NewGuid().ToString("N") + ".csv");

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
