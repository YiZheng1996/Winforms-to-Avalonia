using System.IO.Compression;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Ports;
using XXX.TestBench.Devices.Modbus;
using XXX.TestBench.Infrastructure.Configuration;
using ClosedXML.Excel;

namespace XXX.TestBench.Integration.Tests;

public sealed class XinjeDevicePointTemplateTests
{
    [Fact]
    public async Task Export_XinjeWorkbookUsesNativeExamplesFiveTypeChoicesAndConsistentHints()
    {
        var path = NewPath();
        try
        {
            await new DevicePointTemplateExporter().ExportAsync(
                path,
                NewRequest(CreateXinjeTarget("  xInJe_field_v1  ")));

            using var workbook = new XLWorkbook(path);
            Assert.Equal(3, workbook.Worksheets.Count);
            var dataSheet = workbook.Worksheet(DevicePointTemplateDefinition.DataSheetName);
            Assert.Equal(9, dataSheet.LastColumnUsed()!.ColumnNumber());
            Assert.Equal(DevicePointTemplateDefinition.CurrentTemplateVersion, 7);
            for (var index = 0; index < DevicePointTemplateDefinition.Columns.Count; index++)
                Assert.Equal(DevicePointTemplateDefinition.Columns[index].Header, dataSheet.Cell(1, index + 1).GetString());
            Assert.True(string.IsNullOrWhiteSpace(dataSheet.Cell(2, 1).GetString()));

            var examples = workbook.Worksheet(DevicePointTemplateDefinition.ExamplesSheetName);
            Assert.Equal("HD1000", examples.Cell(2, 2).GetString());
            Assert.Equal("浮点型", examples.Cell(2, 3).GetString());
            Assert.Equal("只读", examples.Cell(2, 4).GetString());
            Assert.Equal("M400", examples.Cell(3, 2).GetString());
            Assert.Equal("布尔量", examples.Cell(3, 3).GetString());
            Assert.Equal("读写", examples.Cell(3, 4).GetString());
            Assert.All(Enumerable.Range(5, 4), column =>
                Assert.True(string.IsNullOrWhiteSpace(examples.Cell(3, column).GetString())));

            var expectedChoices = new[] { "字", "长整型", "双字", "浮点型", "布尔量" };
            Assert.Contains(
                dataSheet.DataValidations.Select(validation => validation.Value),
                value => value.Contains(string.Join(",", expectedChoices), StringComparison.Ordinal));
            var instructions = workbook.Worksheet(DevicePointTemplateDefinition.InstructionsSheetName);
            var instructionText = string.Join(
                "\n",
                instructions.RangeUsed()!.Cells().Select(cell => cell.GetString()));
            foreach (var address in new[] { "D100", "HD1000", "M400", "HM40" })
                Assert.Contains(address, instructionText, StringComparison.Ordinal);
            Assert.Contains("D/HD", instructionText, StringComparison.Ordinal);
            Assert.Contains("M/HM", instructionText, StringComparison.Ordinal);
            Assert.Contains("大端模式、低字在前", instructionText, StringComparison.Ordinal);
            Assert.Contains("不填写 HR:0、C:1", instructionText, StringComparison.Ordinal);

            var comments = ReadCommentXml(path);
            Assert.Contains("D100", comments, StringComparison.Ordinal);
            Assert.Contains("HM40", comments, StringComparison.Ordinal);
            Assert.Contains("仅支持布尔量", comments, StringComparison.Ordinal);
        }
        finally
        {
            DeleteOwnedPath(path);
        }
    }

    [Fact]
    public async Task Export_MixedTargetsKeepPerDeviceExamplesAndUnionChoices()
    {
        var path = NewPath();
        try
        {
            var generic = CreateTarget(
                "DEV_GENERIC",
                "通用 Modbus",
                DevicePointProtocol.ModbusTcp,
                new HashSet<DevicePointDataType>
                {
                    DevicePointDataType.Bool,
                    DevicePointDataType.Byte,
                    DevicePointDataType.Char,
                    DevicePointDataType.Double,
                    DevicePointDataType.Float32,
                    DevicePointDataType.Int16,
                    DevicePointDataType.Int32,
                    DevicePointDataType.UInt16,
                    DevicePointDataType.UInt32
                },
                "GENERIC_MODBUS_TCP");
            var xinje = CreateXinjeTarget(DeviceModelKeyCatalog.XinjeFieldV1);
            await new DevicePointTemplateExporter().ExportAsync(path, NewRequest(xinje, generic));

            using var workbook = new XLWorkbook(path);
            var examples = workbook.Worksheet(DevicePointTemplateDefinition.ExamplesSheetName);
            Assert.Equal("HD1000", examples.Cell(2, 2).GetString());
            Assert.Equal("M400", examples.Cell(3, 2).GetString());
            Assert.Equal("HR:0", examples.Cell(4, 2).GetString());
            Assert.Equal("C:1", examples.Cell(5, 2).GetString());
            Assert.Contains("DEV_XINJE/", examples.Cell(2, 1).GetString(), StringComparison.Ordinal);
            Assert.Contains("DEV_GENERIC/", examples.Cell(4, 1).GetString(), StringComparison.Ordinal);

            var dataSheet = workbook.Worksheet(DevicePointTemplateDefinition.DataSheetName);
            var allChoices = DevicePointTemplateDefinition.DataTypeChoices
                .Select(choice => choice.DisplayValue)
                .ToArray();
            Assert.Contains(
                dataSheet.DataValidations.Select(validation => validation.Value),
                value => value.Contains(string.Join(",", allChoices), StringComparison.Ordinal));
            var instructionText = string.Join(
                "\n",
                workbook.Worksheet(DevicePointTemplateDefinition.InstructionsSheetName)
                    .RangeUsed()!.Cells().Select(cell => cell.GetString()));
            Assert.Contains("有效类型的并集", instructionText, StringComparison.Ordinal);
            Assert.Contains("逐行校验", instructionText, StringComparison.Ordinal);
            Assert.Contains("其他设备", instructionText, StringComparison.Ordinal);
        }
        finally
        {
            DeleteOwnedPath(path);
        }
    }

    [Theory]
    [InlineData("missing-column")]
    [InlineData("extra-column")]
    [InlineData("reordered-columns")]
    [InlineData("wrong-first-sheet")]
    [InlineData("legacy-v6-eleven-columns")]
    public async Task Import_XinjeWorkbookRejectsFixedTemplateContractMutations(string mutation)
    {
        var path = NewPath();
        try
        {
            await new DevicePointTemplateExporter().ExportAsync(
                path,
                NewRequest(CreateXinjeTarget(DeviceModelKeyCatalog.XinjeFieldV1)));

            using (var workbook = new XLWorkbook(path))
            {
                var sheet = workbook.Worksheet(DevicePointTemplateDefinition.DataSheetName);
                switch (mutation)
                {
                    case "missing-column":
                        sheet.Column(9).Delete();
                        break;
                    case "extra-column":
                        sheet.Cell(1, 10).Value = "新增列";
                        break;
                    case "reordered-columns":
                        (sheet.Cell(1, 1).Value, sheet.Cell(1, 2).Value) =
                            (sheet.Cell(1, 2).Value, sheet.Cell(1, 1).Value);
                        break;
                    case "wrong-first-sheet":
                        sheet.Name = "错误工作表";
                        break;
                    case "legacy-v6-eleven-columns":
                        var legacyHeaders = new[]
                        {
                            "点位标签", "地址", "数据类型", "字节序", "字序", "访问权限",
                            "原始下限", "原始上限", "工程下限", "工程上限", "说明"
                        };
                        for (var index = 0; index < legacyHeaders.Length; index++)
                            sheet.Cell(1, index + 1).Value = legacyHeaders[index];
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(mutation), mutation, "未知模板变体");
                }

                workbook.Save();
            }

            var result = await new DevicePointImporter().ImportAsync(path);
            Assert.False(result.IsValid, string.Join("；", result.Issues.Select(issue => issue.Message)));
            Assert.NotEmpty(result.Issues);
        }
        finally
        {
            DeleteOwnedPath(path);
        }
    }

    [Fact]
    public async Task Export_NonXinjeTargetsKeepExistingExamplesAndDefaultWorkbook()
    {
        var path = NewPath();
        try
        {
            await new DevicePointTemplateExporter().ExportAsync(path);
            using (var workbook = new XLWorkbook(path))
            {
                var examples = workbook.Worksheet(DevicePointTemplateDefinition.ExamplesSheetName);
                Assert.Equal("sim.pressure", examples.Cell(2, 2).GetString());
                Assert.Equal("sim.start", examples.Cell(3, 2).GetString());
                Assert.DoesNotContain("XINJE_FIELD_V1",
                    string.Join("\n", workbook.Worksheet(DevicePointTemplateDefinition.InstructionsSheetName)
                        .RangeUsed()!.Cells().Select(cell => cell.GetString())),
                    StringComparison.Ordinal);
            }

            var targets = new[]
            {
                CreateTarget("DEV_TCP", "通用 TCP", DevicePointProtocol.ModbusTcp, GenericModbusTypes(), "GENERIC_MODBUS_TCP"),
                CreateTarget("DEV_RTU", "通用 RTU", DevicePointProtocol.ModbusRtu, GenericModbusTypes(), "GENERIC_MODBUS_RTU"),
                CreateTarget("DEV_S7_200", "S7-200 SMART", DevicePointProtocol.SiemensS7, GenericModbusTypes(), "S7-200 SMART"),
                CreateTarget("DEV_S7_1200", "S7-1200", DevicePointProtocol.SiemensS7, GenericModbusTypes(), "S7-1200"),
                CreateTarget("DEV_SIM", "仿真设备", DevicePointProtocol.Simulation, GenericModbusTypes(), string.Empty)
            };
            await new DevicePointTemplateExporter().ExportAsync(path, NewRequest(targets));

            using var typedWorkbook = new XLWorkbook(path);
            var typedExamples = typedWorkbook.Worksheet(DevicePointTemplateDefinition.ExamplesSheetName);
            Assert.Equal("HR:0", typedExamples.Cell(2, 2).GetString());
            Assert.Equal("C:1", typedExamples.Cell(3, 2).GetString());
            Assert.Equal("VW5022", typedExamples.Cell(6, 2).GetString());
            Assert.Equal("M10.1", typedExamples.Cell(7, 2).GetString());
            Assert.Equal("DB144.DBD88", typedExamples.Cell(8, 2).GetString());
            Assert.Equal("DB143.DBX34.4", typedExamples.Cell(9, 2).GetString());
            Assert.Equal("sim.pressure", typedExamples.Cell(10, 2).GetString());
            Assert.Equal("sim.start", typedExamples.Cell(11, 2).GetString());
        }
        finally
        {
            DeleteOwnedPath(path);
        }
    }

    [Theory]
    [InlineData(DevicePointDataType.Float32)]
    [InlineData(DevicePointDataType.Bool)]
    public async Task Export_XinjeTargetMissingExampleTypeFailsExplicitly(DevicePointDataType missingType)
    {
        var path = NewPath();
        var supported = XinjeTypes();
        supported.Remove(missingType);
        try
        {
            var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
                new DevicePointTemplateExporter().ExportAsync(
                    path,
                    NewRequest(CreateXinjeTarget(DeviceModelKeyCatalog.XinjeFieldV1, supported))));
            Assert.Contains("模板能力不完整", error.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(path));
        }
        finally
        {
            DeleteOwnedPath(path);
        }
    }

    private static DevicePointTemplateExportRequest NewRequest(params DevicePointTemplateTarget[] targets)
        => new(
            "测试设备范围",
            "按当前测试选择的设备生成。",
            targets,
            new[] { "DEFAULT" });

    private static DevicePointTemplateTarget CreateXinjeTarget(
        string model,
        HashSet<DevicePointDataType>? supported = null)
        => CreateTarget(
            "DEV_XINJE",
            "信捷 PLC",
            DevicePointProtocol.ModbusTcp,
            supported ?? XinjeTypes(),
            model);

    private static DevicePointTemplateTarget CreateTarget(
        string code,
        string name,
        DevicePointProtocol protocol,
        IReadOnlySet<DevicePointDataType> supported,
        string model)
        => new(
            code,
            code,
            name,
            protocol,
            DevicePointTypeCatalog.ToDisplayName(protocol),
            supported,
            model);

    private static HashSet<DevicePointDataType> XinjeTypes()
        => new()
        {
            DevicePointDataType.UInt16,
            DevicePointDataType.Int32,
            DevicePointDataType.UInt32,
            DevicePointDataType.Float32,
            DevicePointDataType.Bool
        };

    private static HashSet<DevicePointDataType> GenericModbusTypes()
        => new()
        {
            DevicePointDataType.Bool,
            DevicePointDataType.Byte,
            DevicePointDataType.Char,
            DevicePointDataType.Double,
            DevicePointDataType.Float32,
            DevicePointDataType.Int16,
            DevicePointDataType.Int32,
            DevicePointDataType.UInt16,
            DevicePointDataType.UInt32
        };

    private static string NewPath()
    {
        var directory = Path.Combine(
            @"D:\Codex相关\信捷PLC_XLSX导入模板修正\test-files",
            "integration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "template.xlsx");
    }

    private static string ReadCommentXml(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var documents = archive.Entries
            .Where(entry => entry.FullName.StartsWith("xl/comments", StringComparison.OrdinalIgnoreCase)
                && entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .Select(entry =>
            {
                using var reader = new StreamReader(entry.Open());
                return reader.ReadToEnd();
            });
        return string.Join("\n", documents);
    }

    private static void DeleteOwnedPath(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
        var directory = Path.GetFullPath(Path.GetDirectoryName(path)!);
        var taskRoot = Path.GetFullPath(@"D:\Codex相关\信捷PLC_XLSX导入模板修正\test-files")
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!directory.StartsWith(taskRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("测试工作簿路径超出本用例目录");
        if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
            Directory.Delete(directory);
    }
}
