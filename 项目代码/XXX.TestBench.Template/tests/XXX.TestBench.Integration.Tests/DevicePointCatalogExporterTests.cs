using ClosedXML.Excel;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Ports;
using XXX.TestBench.Infrastructure.Configuration;

namespace XXX.TestBench.Integration.Tests;

public sealed class DevicePointCatalogExporterTests
{
    private const string DeviceAId = "20000000-0000-0000-0000-000000000001";
    private const string DeviceBId = "20000000-0000-0000-0000-000000000002";
    private const string GroupAId = "30000000-0000-0000-0000-000000000001";
    private const string GroupBId = "30000000-0000-0000-0000-000000000002";
    private const string DefaultGroupAId = "30000000-0000-0000-0000-000000000011";

    [Fact]
    public async Task ExportCurrentRange_UsesV6HeadersInExactOrder()
    {
        var path = NewTestPath();
        try
        {
            await Exporter().ExportAsync(path, SingleDeviceRequest());

            using var workbook = new XLWorkbook(path);
            var sheet = workbook.Worksheet(DevicePointTemplateDefinition.DataSheetName);
            var headers = DevicePointTemplateDefinition.Columns
                .Select((_, index) => sheet.Cell(1, index + 1).GetString())
                .ToList();

            Assert.Equal(
                DevicePointTemplateDefinition.Columns.Select(column => column.Header),
                headers);
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public async Task ExportSingleDevice_UsesGroupPointTagAndOmitsDefaultPrefix()
    {
        var path = NewTestPath();
        try
        {
            await Exporter().ExportAsync(path, SingleDeviceRequest());

            using var workbook = new XLWorkbook(path);
            var sheet = workbook.Worksheet(DevicePointTemplateDefinition.DataSheetName);
            Assert.Equal("AI.入口压力", sheet.Cell(2, 1).GetString());
            Assert.Equal("入口温度", sheet.Cell(3, 1).GetString());
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public async Task ExportMultipleDevices_UsesDevicePrefix()
    {
        var path = NewTestPath();
        try
        {
            var request = MultiDeviceRequest();
            await Exporter().ExportAsync(path, request);

            using var workbook = new XLWorkbook(path);
            var sheet = workbook.Worksheet(DevicePointTemplateDefinition.DataSheetName);
            Assert.Equal("DEV_A/AI.入口压力", sheet.Cell(2, 1).GetString());
            Assert.Equal("DEV_B/启动命令", sheet.Cell(3, 1).GetString());
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public async Task ExportedCatalog_CanBeImported()
    {
        var path = NewTestPath();
        try
        {
            await Exporter().ExportAsync(path, SingleDeviceRequest());

            var imported = await new DevicePointImporter().ImportAsync(path);

            Assert.True(imported.IsValid, string.Join("；", imported.Issues.Select(issue => issue.Message)));
            Assert.Equal(2, imported.Points.Count);
            Assert.Contains(imported.Points, point => point.Name == "入口压力");
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public async Task ExportedCatalog_DoesNotContainInternalIdsOrRuntimeValues()
    {
        var path = NewTestPath();
        try
        {
            await Exporter().ExportAsync(path, SingleDeviceRequest());

            using var workbook = new XLWorkbook(path);
            var sheet = workbook.Worksheet(DevicePointTemplateDefinition.DataSheetName);
            var values = sheet.CellsUsed()
                .Select(cell => cell.GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();

            Assert.DoesNotContain(DeviceAId, values);
            Assert.DoesNotContain(GroupAId, values);
            Assert.DoesNotContain(values, value => value.Contains("质量", StringComparison.Ordinal));
            Assert.DoesNotContain(values, value => value.Contains("时间戳", StringComparison.Ordinal));
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public async Task ExportRejectsNonXlsxPath()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            Exporter().ExportAsync(NewTestPath(".csv"), SingleDeviceRequest()));
    }

    [Fact]
    public async Task ExportEmptyScope_IsRejected()
    {
        var request = SingleDeviceRequest() with { Points = Array.Empty<PointsConfig.PointEntry>() };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Exporter().ExportAsync(NewTestPath(), request));
    }

    private static DevicePointCatalogExporter Exporter() => new();

    private static DevicePointCatalogExportRequest SingleDeviceRequest()
        => new(
            "设备 A",
            new[]
            {
                Point("40000000-0000-0000-0000-000000000001", "A_PRESSURE", "入口压力", DeviceAId, GroupAId),
                Point("40000000-0000-0000-0000-000000000002", "A_TEMP", "入口温度", DeviceAId, DefaultGroupAId)
            },
            new[]
            {
                new PointsConfig.PointGroupEntry { Id = GroupAId, DeviceId = DeviceAId, Code = "AI", Name = "模拟量" },
                new PointsConfig.PointGroupEntry { Id = DefaultGroupAId, DeviceId = DeviceAId, Code = "DEFAULT", Name = "未分组" }
            },
            new[] { Device(DeviceAId, "DEV_A", "设备 A") });

    private static DevicePointCatalogExportRequest MultiDeviceRequest()
        => new(
            "全部设备",
            new[]
            {
                Point("40000000-0000-0000-0000-000000000001", "A_PRESSURE", "入口压力", DeviceAId, GroupAId),
                Point("40000000-0000-0000-0000-000000000003", "B_START", "启动命令", DeviceBId, GroupBId,
                    writable: true)
            },
            new[]
            {
                new PointsConfig.PointGroupEntry { Id = GroupAId, DeviceId = DeviceAId, Code = "AI", Name = "模拟量" },
                new PointsConfig.PointGroupEntry { Id = GroupBId, DeviceId = DeviceBId, Code = "DEFAULT", Name = "未分组" }
            },
            new[] { Device(DeviceAId, "DEV_A", "设备 A"), Device(DeviceBId, "DEV_B", "设备 B") });

    private static PointsConfig.PointEntry Point(
        string id,
        string code,
        string name,
        string deviceId,
        string groupId,
        bool writable = false)
        => new()
        {
            Id = id,
            Code = code,
            Name = name,
            Protocol = "Simulation",
            DeviceId = deviceId,
            GroupId = groupId,
            Address = "sim." + code,
            DataType = "Decimal",
            RawDataType = "Decimal",
            IsWritable = writable,
            RiskLevel = writable ? WriteRiskLevel.HighRisk : WriteRiskLevel.Normal
        };

    private static DeviceConfig.DeviceEntry Device(string id, string code, string name)
        => new()
        {
            Id = id,
            Code = code,
            Name = name,
            DeviceMode = DeviceMode.Simulation,
            Protocol = "Simulation",
            Address = "sim",
            ChannelId = "10000000-0000-0000-0000-000000000001",
            DriverKey = "simulation",
            Model = "sim",
            PollIntervalMs = 500,
            StaleAfterMs = 1500,
            Enabled = true
        };

    private static string NewTestPath(string extension = ".xlsx")
    {
        var dir = @"D:\Codex相关\设备点位功能实现\test-runs";
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "catalog-" + Guid.NewGuid().ToString("N")[..8] + extension);
    }

    private static void Delete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // 测试清理失败不影响结果。
        }
    }
}
