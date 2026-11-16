using System.Text.Json;
using XXX.TestBench.App.ViewModels;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Devices;
using XXX.TestBench.Infrastructure.Configuration;
using Xunit;

namespace XXX.TestBench.App.Tests;

public sealed class XinjeImportWorkflowTests
{
    [Fact]
    public async Task ImportPreviewUsesDeviceContextPreservesOriginalInputAndBlocksReloadedAlias()
    {
        using var harness = AppTestHarness.Create(grouped: true);
        var actor = await harness.AdminActorAsync();
        var active = await harness.Services.DeviceConfigurations!.LoadActiveAsync();
        var channel = active.Device.Channels.First(item => item.TransportKind == ChannelTransportKind.Tcp);
        var xinje = new DeviceConfig.DeviceEntry
        {
            Id = "20000000-0000-0000-0000-000000000099",
            Code = "DEV_XINJE",
            Name = "信捷 PLC",
            ChannelId = channel.Id,
            DriverKey = DriverKeyCatalog.ModbusTcp,
            Model = DeviceModelKeyCatalog.XinjeFieldV1,
            DeviceMode = DeviceMode.Simulation,
            ModbusUnitId = 1,
            ModbusDataOrder = new ModbusDataOrderOptions
            {
                ByteOrder = ByteOrder.BigEndian,
                WordOrder = WordOrder.LowWordFirst
            },
            ModbusTcp = new ModbusTcpConnectionOptions { Host = "127.0.0.1", Port = 502 },
            PollIntervalMs = 500,
            StaleAfterMs = 1500
        };
        var candidateDevice = new DeviceConfig
        {
            SchemaVersion = active.Device.SchemaVersion,
            Channels = active.Device.Channels.ToList(),
            Devices = active.Device.Devices.Append(xinje).ToList()
        };
        var deviceApplied = await harness.Services.DeviceConfigurations.ApplyDeviceConfigurationAsync(
            actor, candidateDevice, new DeviceConfigurationApplyOptions());
        Assert.True(deviceApplied.Ok, deviceApplied.Error);

        var vm = new DevicePointManagementViewModel(harness.Services, actor);
        await vm.LoadAsync();
        vm.SelectedTreeNode = FindNode(vm.TreeNodes, node =>
            node.Kind == DevicePointTreeNodeKind.Device && node.Code == "DEV_XINJE");
        Assert.NotNull(vm.SelectedTreeNode);

        var path = NewImportPath();
        var exportPath = NewExportPath();
        try
        {
            await WriteImportAsync(path, "D_WORD", "D100");
            var dPlan = await vm.CreateImportPlanAsync(path);
            Assert.NotNull(dPlan);
            Assert.True(dPlan!.CanApply, string.Join("；", dPlan.Rows.SelectMany(row => row.Issues)));
            var dPreview = new DevicePointImportPreviewViewModel(
                dPlan,
                active.Points.Points.Count,
                active.Points.Groups,
                active.Device.Devices);
            var dPreviewRow = Assert.Single(dPreview.Rows);
            Assert.Equal("D100", dPreviewRow.OriginalAddress);
            Assert.Equal("HR:100", dPreviewRow.CanonicalAddress);

            await WriteImportAsync(path, "HD_SPEED", "hd01000");
            var rawSnapshot = await harness.Services.DeviceConfigurations.LoadActiveAsync();
            var rawBeforePreview = JsonSerializer.Serialize(rawSnapshot.Points);
            var plan = await vm.CreateImportPlanAsync(path);

            Assert.NotNull(plan);
            Assert.True(plan!.CanApply, string.Join("；", plan.Rows.SelectMany(row => row.Issues)));
            var preview = new DevicePointImportPreviewViewModel(
                plan,
                rawSnapshot.Points.Points.Count,
                rawSnapshot.Points.Groups,
                rawSnapshot.Device.Devices);
            var previewRow = Assert.Single(preview.Rows);
            Assert.Equal("hd01000", previewRow.OriginalAddress);
            Assert.Equal("HR:42088", previewRow.CanonicalAddress);
            Assert.Equal(rawBeforePreview, JsonSerializer.Serialize(rawSnapshot.Points));

            var applied = await vm.ApplyImportedPointsAsync(plan, new DeviceConfigurationApplyOptions());
            Assert.True(applied.Succeeded, applied.Message);
            var persisted = await harness.Services.DeviceConfigurations.LoadActiveAsync();
            var imported = Assert.Single(persisted.Points.Points, point => point.Code == "HD_SPEED");
            Assert.Equal("HD1000", imported.Address);
            Assert.Equal("HR", imported.AddressDefinition?.Area);
            Assert.Equal(42088, imported.AddressDefinition?.Offset);

            var export = await vm.ExportCurrentRangeAsync(exportPath);
            Assert.True(export.Succeeded, export.Message);
            var roundTripPlan = await vm.CreateImportPlanAsync(exportPath);
            Assert.NotNull(roundTripPlan);
            Assert.True(roundTripPlan!.CanApply, string.Join("；", roundTripPlan.Rows.SelectMany(row => row.Issues)));
            var unchangedRow = Assert.Single(roundTripPlan.Rows);
            Assert.Equal(DevicePointImportOperationKind.Unchanged, unchangedRow.Operation);
            Assert.Equal("HD1000", unchangedRow.Incoming.Address);
            Assert.Equal(imported.Id, unchangedRow.Candidate?.Id);
            var beforeNoOpApply = await harness.Services.DeviceConfigurations.LoadActiveAsync();
            var noOpResult = await vm.ApplyImportedPointsAsync(roundTripPlan, new DeviceConfigurationApplyOptions());
            Assert.True(noOpResult.Succeeded, noOpResult.Message);
            var afterNoOpApply = await harness.Services.DeviceConfigurations.LoadActiveAsync();
            Assert.Equal(beforeNoOpApply.Revision, afterNoOpApply.Revision);
            Assert.Equal(beforeNoOpApply.Points.Points.Count, afterNoOpApply.Points.Points.Count);

            // 用同一 Revision 重建一致性有效的快照，但只改目录内容，验证指纹门独立阻止旧预览。
            var catalogStore = new DeviceConfigurationStore(harness.ConfigRoot);
            await using (catalogStore.ConfigureAwait(false))
            {
                var changedCatalog = await catalogStore.LoadActiveAsync();
                var sameRevision = changedCatalog.Revision;
                changedCatalog.Points.Points[0].Description += "|fingerprint-change";

                var revisionsRoot = Path.GetFullPath(Path.Combine(harness.ConfigRoot, "device-config", "revisions"));
                var revisionDirectory = Path.GetFullPath(Path.Combine(revisionsRoot, sameRevision));
                var revisionsPrefix = revisionsRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!revisionDirectory.StartsWith(revisionsPrefix, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("测试 revision 路径超出隔离配置目录");
                Directory.Delete(revisionDirectory, recursive: true);
                await catalogStore.StageAsync(changedCatalog);
                await catalogStore.CommitActiveAsync(sameRevision);
            }

            var changedActive = await harness.Services.DeviceConfigurations.LoadActiveAsync();
            Assert.Equal(roundTripPlan.SourceRevision, changedActive.Revision);
            Assert.NotEqual(
                roundTripPlan.SourceCatalogFingerprint,
                DevicePointImportPlanner.ComputeCatalogFingerprint(changedActive.Points.Points));
            var staleFingerprint = await vm.ApplyImportedPointsAsync(roundTripPlan, new DeviceConfigurationApplyOptions());
            Assert.False(staleFingerprint.Succeeded);
            Assert.Contains("目录已变化", staleFingerprint.Message, StringComparison.Ordinal);

            // CanonicalAddress 不序列化；重新加载后按当前设备上下文重算，
            // 仍能在预览阶段发现不同原生区标签指向的同一个协议地址。
            await WriteImportAsync(path, "D_ALIAS", "D42088");
            var duplicatePlan = await vm.CreateImportPlanAsync(path);
            Assert.NotNull(duplicatePlan);
            Assert.False(duplicatePlan!.CanApply);
            Assert.Contains(duplicatePlan.Rows.Single().Issues,
                issue => issue.Contains("地址", StringComparison.Ordinal));
        }
        finally
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
            try { if (File.Exists(exportPath)) File.Delete(exportPath); } catch { }
        }
    }

    private static DevicePointTreeNodeViewModel? FindNode(
        IEnumerable<DevicePointTreeNodeViewModel> nodes,
        Func<DevicePointTreeNodeViewModel, bool> predicate)
    {
        foreach (var node in nodes)
        {
            if (predicate(node))
                return node;
            var child = FindNode(node.Children, predicate);
            if (child is not null)
                return child;
        }
        return null;
    }

    private static async Task WriteImportAsync(string path, string pointTag, string address)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var headers = string.Join(',', DevicePointTemplateDefinition.Columns.Select(column => column.Header));
        var row = string.Join(',', pointTag, address, "浮点型", "只读", "", "", "", "", "信捷只读验证点");
        await File.WriteAllTextAsync(path, $"{headers}\n{row}");
    }

    private static string NewImportPath()
        => Path.Combine(
            @"D:\Codex相关\信捷PLC_ModbusTCP现场适配\inputs",
            "import-" + Guid.NewGuid().ToString("N") + ".csv");

    private static string NewExportPath()
        => Path.Combine(
            @"D:\Codex相关\信捷PLC_ModbusTCP现场适配\outputs",
            "roundtrip-" + Guid.NewGuid().ToString("N") + ".xlsx");

    private static DeviceConfig.DeviceEntry CreateXinjeDevice(string channelId)
        => new()
        {
            Id = "20000000-0000-0000-0000-000000000099",
            Code = "DEV_XINJE",
            Name = "信捷 PLC",
            ChannelId = channelId,
            DriverKey = DriverKeyCatalog.ModbusTcp,
            Model = DeviceModelKeyCatalog.XinjeFieldV1,
            DeviceMode = DeviceMode.Simulation,
            ModbusUnitId = 1,
            ModbusDataOrder = new ModbusDataOrderOptions
            {
                ByteOrder = ByteOrder.BigEndian,
                WordOrder = WordOrder.LowWordFirst
            },
            ModbusTcp = new ModbusTcpConnectionOptions { Host = "127.0.0.1", Port = 502 },
            PollIntervalMs = 500,
            StaleAfterMs = 1500
        };


}
