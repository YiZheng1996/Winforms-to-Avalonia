using XXX.TestBench.App.ViewModels;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Domain.Identity;
using ClosedXML.Excel;

namespace XXX.TestBench.App.Tests;

public sealed class DevicePointManagementViewModelTests
{
    [Fact]
    public async Task SimplifiedTreeUsesChannelDeviceGroupLeavesAndSharedChannelOnce()
    {
        using var harness = AppTestHarness.Create(grouped: true);
        var actor = await harness.AdminActorAsync();
        var vm = new DevicePointManagementViewModel(harness.Services, actor);

        await vm.LoadAsync();

        var root = Assert.Single(vm.TreeNodes);
        Assert.Equal("root", root.NodeKey);
        Assert.Equal("全部设备", root.DisplayText);
        var channel = Assert.Single(root.Children, node => node.Code == "CH_SIM");
        Assert.Equal("channel:10000000-0000-0000-0000-000000000001", channel.NodeKey);
        Assert.Equal("仿真设备网络通道", channel.DisplayText);
        Assert.Contains("TCP", channel.TreeSummaryText, StringComparison.Ordinal);
        Assert.False(channel.IsTreeSummaryVisible);
        Assert.Equal(2, channel.Children.Count(node => node.Kind == DevicePointTreeNodeKind.Device));
        Assert.DoesNotContain(channel.Children, node => node.Kind == DevicePointTreeNodeKind.Group);
        var device = Assert.Single(channel.Children, node => node.Code == "DEV_A");
        Assert.Equal("device:20000000-0000-0000-0000-000000000001", device.NodeKey);
        Assert.Equal("设备 A", device.DisplayText);
        Assert.Contains("仿真模式", device.TreeSummaryText, StringComparison.Ordinal);
        Assert.Contains("驱动 西门子 S7", device.TreeSummaryText, StringComparison.Ordinal);
        Assert.False(device.IsTreeSummaryVisible);
        Assert.Single(device.Children);
        Assert.All(device.Children, node =>
        {
            Assert.Equal(DevicePointTreeNodeKind.Group, node.Kind);
            Assert.Empty(node.Children);
            Assert.True(node.IsTreeSummaryVisible);
        });
        Assert.DoesNotContain(device.Children, node => node.Code == "DEFAULT");

        var emptyChannel = Assert.Single(root.Children, node => node.Code == "CH_EMPTY");
        Assert.Empty(emptyChannel.Children);
        Assert.DoesNotContain(root.Children, node => node.IsVirtual);
        Assert.False(vm.CanAddPoint);
    }

    [Fact]
    public async Task SelectingDeviceShowsGroupedAndUngroupedPointsWithoutAnUngroupedNode()
    {
        using var harness = AppTestHarness.Create(grouped: true);
        var actor = await harness.AdminActorAsync();
        var vm = new DevicePointManagementViewModel(harness.Services, actor);
        await vm.LoadAsync();

        var device = vm.TreeNodes[0].Children.Single(node => node.Code == "CH_SIM")
            .Children.Single(node => node.Code == "DEV_A");
        vm.SelectedTreeNode = device;

        Assert.Equal(2, vm.Points.Count);
        Assert.Contains(vm.Points, row => row.Code == "A_TEMP");
        Assert.Equal(string.Empty, vm.Points.Single(row => row.Code == "A_TEMP").GroupText);
        Assert.DoesNotContain(device.Children, node => node.DisplayText.Contains("未分组", StringComparison.Ordinal));

        var group = device.Children.Single(node => node.Code == "PRESSURE");
        vm.SelectedTreeNode = group;
        Assert.Single(vm.Points);
        Assert.Equal("A_PRESSURE", vm.Points[0].Code);
    }

    [Fact]
    public async Task PointSearchOnlyFiltersTableAndReusesRowsAndTreeNodes()
    {
        using var harness = AppTestHarness.Create(grouped: true);
        var actor = await harness.AdminActorAsync();
        var vm = new DevicePointManagementViewModel(harness.Services, actor);
        await vm.LoadAsync();

        var root = Assert.Single(vm.TreeNodes);
        vm.SelectedTreeNode = root;
        var row = Assert.Single(vm.Points, item => item.Code == "A_PRESSURE");
        vm.PointSearchText = "温度";

        Assert.Same(root, vm.TreeNodes[0]);
        Assert.Single(vm.Points);
        Assert.Equal("A_TEMP", vm.Points[0].Code);
        vm.PointSearchText = string.Empty;
        Assert.Same(row, vm.Points.Single(item => item.Code == "A_PRESSURE"));
    }

    [Fact]
    public async Task PointSearchExposesDistinctEmptyStateAndCanBeCleared()
    {
        using var harness = AppTestHarness.Create(grouped: true);
        var actor = await harness.AdminActorAsync();
        var vm = new DevicePointManagementViewModel(harness.Services, actor);
        await vm.LoadAsync();

        vm.SelectedTreeNode = Assert.Single(vm.TreeNodes);
        vm.PointSearchText = "不存在的点位";

        Assert.Empty(vm.Points);
        Assert.True(vm.HasPointSearch);
        Assert.Equal("没有符合条件的点位", vm.EmptyPointsTitle);
        Assert.Contains("清除", vm.EmptyPointsHint, StringComparison.Ordinal);

        vm.ClearPointSearch();

        Assert.False(vm.HasPointSearch);
        Assert.Equal("当前范围暂无点位", vm.EmptyPointsTitle);
        Assert.Equal(3, vm.Points.Count);
    }

    [Fact]
    public async Task AddingFromGroupLocksTheSelectedGroupInSimplifiedDialog()
    {
        using var harness = AppTestHarness.Create(grouped: true);
        var actor = await harness.AdminActorAsync();
        var vm = new DevicePointManagementViewModel(harness.Services, actor);
        await vm.LoadAsync();

        var group = vm.TreeNodes[0].Children.Single(node => node.Code == "CH_SIM")
            .Children.Single(node => node.Code == "DEV_A")
            .Children.Single(node => node.Code == "PRESSURE");
        vm.SelectedTreeNode = group;

        var context = vm.CreatePointDialogContext(isEdit: false);
        var dialog = new DevicePointDialogViewModel(false, null, context);

        Assert.True(context.GroupLocked);
        Assert.False(dialog.UseGroupSelection);
        Assert.Equal("压力", dialog.CurrentGroupText);
    }

    [Fact]
    public async Task TreeSearchRestoresSelectionAndExpandedState()
    {
        using var harness = AppTestHarness.Create(grouped: true);
        var actor = await harness.AdminActorAsync();
        var vm = new DevicePointManagementViewModel(harness.Services, actor);
        await vm.LoadAsync();

        var channel = Assert.Single(vm.TreeNodes[0].Children, node => node.Code == "CH_SIM");
        var device = Assert.Single(channel.Children, node => node.Code == "DEV_A");
        channel.IsExpanded = false;
        device.IsExpanded = true;
        vm.SelectedTreeNode = device;
        vm.TreeSearchText = "压力";
        Assert.Same(device, vm.SelectedTreeNode);
        vm.TreeSearchText = string.Empty;

        Assert.Same(device, vm.SelectedTreeNode);
        Assert.False(channel.IsExpanded);
        Assert.True(device.IsExpanded);
    }

    [Fact]
    public async Task GroupDeleteMovesPointsToDefaultInOneAppliedSnapshot()
    {
        using var harness = AppTestHarness.Create(grouped: true);
        var actor = await harness.AdminActorAsync();
        var vm = new DevicePointManagementViewModel(harness.Services, actor);
        await vm.LoadAsync();

        var group = vm.TreeNodes[0].Children.Single(node => node.Code == "CH_SIM")
            .Children.Single(node => node.Code == "DEV_A")
            .Children.Single(node => node.Code == "PRESSURE");
        vm.SelectedTreeNode = group;
        Assert.True((await vm.DeleteSelectedTreeNodeAsync()).Succeeded);

        Assert.DoesNotContain(vm.CurrentGroups, item => item.Code == "PRESSURE");
        var moved = Assert.Single(vm.CurrentEntries, item => item.Code == "A_PRESSURE");
        Assert.Equal("DEFAULT", vm.CurrentGroups.Single(item => item.Id == moved.GroupId).Code);
        Assert.Equal("DEFAULT", moved.GroupCode);
    }

    [Fact]
    public async Task Import_MergesCatalogAndReloadsSimulationRuntime()
    {
        using var harness = AppTestHarness.Create(grouped: true);
        var actor = await harness.AdminActorAsync();
        var vm = new DevicePointManagementViewModel(harness.Services, actor);
        var path = NewTestPath("testbench-point-vm-", ".csv");
        try
        {
            await vm.LoadAsync();
            vm.SelectedTreeNode = vm.TreeNodes.Single().Children.Single(node => node.Code == "CH_SIM")
                .Children.Single(node => node.Code == "DEV_A");
            Assert.True(
                vm.Points.Count == 2,
                $"rows={vm.Points.Count}; entries={vm.CurrentEntries.Count}; selected={vm.SelectedTreeNode?.Kind}/{vm.SelectedTreeNode?.NodeKey}; tree={string.Join(",", vm.TreeNodes.SelectMany(node => node.Children).Select(node => node.Kind + "/" + node.DisplayText))}; status={vm.StatusMessage}");

            var headers = string.Join(',', DevicePointTemplateDefinition.Columns.Select(column => column.Header));
            var row = string.Join(',', "AI_New", "DB1.DBD8", "小数", "", "", "只读", "", "", "", "", "新压力");
            await File.WriteAllTextAsync(path, $"{headers}\n{row}");
            await vm.ImportAsync(path);

            var point = Assert.Single(vm.Points, item => item.Code == "AI_New");
            Assert.Equal("AI_New", point.Code);
            Assert.Equal(3, vm.Points.Count);
            Assert.Contains("已合并导入", vm.StatusMessage, StringComparison.Ordinal);
            var runtimePoints = await harness.Services.DeviceModes.Runtime!.ListPointsAsync();
            Assert.Contains(runtimePoints, item => item.Code == "AI_New");
        }
        finally
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    [Fact]
    public async Task DownloadTemplate_ProducesFixedChineseHeaderWorkbook()
    {
        using var harness = AppTestHarness.Create(grouped: true);
        var actor = await harness.AdminActorAsync();
        var vm = new DevicePointManagementViewModel(harness.Services, actor);
        var path = NewTestPath("testbench-point-template-", ".xlsx");
        try
        {
            await vm.LoadAsync();
            vm.SelectedTreeNode = vm.TreeNodes.Single().Children.Single(node => node.Code == "CH_SIM")
                .Children.Single(node => node.Code == "DEV_A");
            var request = vm.CreateTemplateExportRequest();
            Assert.NotNull(request);
            var target = Assert.Single(request!.Targets);

            await vm.DownloadTemplateAsync(path);

            Assert.True(File.Exists(path));
            using var workbook = new XLWorkbook(path);
            var sheet = workbook.Worksheet(DevicePointTemplateDefinition.DataSheetName);
            Assert.Equal(DevicePointTemplateDefinition.Columns.Count, sheet.LastColumnUsed()!.ColumnNumber());
            for (var index = 0; index < DevicePointTemplateDefinition.Columns.Count; index++)
                Assert.Equal(DevicePointTemplateDefinition.Columns[index].Header, sheet.Cell(1, index + 1).GetString());
            Assert.Equal(DevicePointTemplateDefinition.InstructionsSheetName, workbook.Worksheet(2).Name);
            Assert.Equal(DevicePointTemplateDefinition.ExamplesSheetName, workbook.Worksheet(3).Name);
            Assert.Equal(
                new[] { "只读", "读写" },
                DevicePointTemplateDefinition.AccessChoices.Select(choice => choice.DisplayValue));
            var instructions = workbook.Worksheet(DevicePointTemplateDefinition.InstructionsSheetName);
            var instructionText = string.Join("\n", instructions.RangeUsed()!.Cells().Select(cell => cell.GetString()));
            Assert.Contains("KEPServer", instructionText, StringComparison.Ordinal);
            Assert.Contains("Scan Rate", instructionText, StringComparison.Ordinal);
            Assert.Contains("暂不支持", instructionText, StringComparison.Ordinal);
            Assert.Equal("PRESSURE.试验压力", workbook.Worksheet(DevicePointTemplateDefinition.ExamplesSheetName).Cell(2, 1).GetString());
            Assert.Equal("DB144.DBD88", workbook.Worksheet(DevicePointTemplateDefinition.ExamplesSheetName).Cell(2, 2).GetString());
            Assert.Equal("只读", workbook.Worksheet(DevicePointTemplateDefinition.ExamplesSheetName).Cell(2, 6).GetString());
            Assert.Contains("生成设备点位模板", vm.StatusMessage, StringComparison.Ordinal);
        }
        finally
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    [Fact]
    public async Task ApplyingChannelAndDeviceChangesRefreshesTreeRowsAndRuntimeImmediately()
    {
        using var harness = AppTestHarness.Create(grouped: true);
        // 该夹具默认注入一个仅用于树展示测试的坏关联草稿；本测试验证可应用配置，先移除测试噪声。
        harness.Services.DeviceConfig.Devices.RemoveAll(device => device.Code == "DEV_ORPHAN");
        var actor = await harness.AdminActorAsync();
        var vm = new DevicePointManagementViewModel(harness.Services, actor);

        await vm.LoadAsync();
        var channel = vm.TreeNodes.Single().Children.Single(node => node.Code == "CH_SIM");
        var device = channel.Children.Single(node => node.Code == "DEV_A");
        vm.SelectedTreeNode = device;
        var previousRuntime = harness.Services.DeviceModes.Runtime;

        var deviceEditor = vm.CreateDeviceConfigurationDialog();
        deviceEditor.SelectDevice(device.Id);
        deviceEditor.EditSelectedDevice();
        var deviceForm = deviceEditor.DeviceEditor;
        Assert.NotNull(deviceForm);
        deviceForm!.Name = "设备 A（已更新）";
        Assert.True(deviceForm.TryBuild(out var updatedDevice), deviceForm.ValidationMessage);
        Assert.True(deviceEditor.ApplyDeviceEntry(updatedDevice), deviceEditor.ValidationMessage);

        var deviceResult = await deviceEditor.SaveAsync();
        Assert.True(deviceResult.Ok, deviceResult.Error);
        await vm.ApplyDeviceConfigurationResultAsync(deviceResult);

        var updatedDeviceNode = vm.TreeNodes.Single().Children.Single(node => node.Code == "CH_SIM")
            .Children.Single(node => node.Code == "DEV_A");
        Assert.Equal("设备 A（已更新）", updatedDeviceNode.DisplayText);
        Assert.Equal("设备 A（已更新）", vm.Points.Single(row => row.Code == "A_PRESSURE").DeviceText);
        Assert.Equal("设备 A（已更新）", harness.Services.DeviceConfig.Devices
            .Single(item => item.Code == "DEV_A").Name);
        Assert.NotSame(previousRuntime, harness.Services.DeviceModes.Runtime);

        var channelEditor = vm.CreateDeviceConfigurationDialog();
        channelEditor.SelectChannel(channel.Id);
        channelEditor.EditSelectedChannel();
        var channelForm = channelEditor.ChannelEditor;
        Assert.NotNull(channelForm);
        channelForm!.Name = "仿真通道（已更新）";
        Assert.True(channelForm.TryBuild(out var updatedChannel), channelForm.ValidationMessage);
        Assert.True(channelEditor.ApplyChannelEntry(updatedChannel), channelEditor.ValidationMessage);

        var channelResult = await channelEditor.SaveAsync();
        Assert.True(channelResult.Ok, channelResult.Error);
        await vm.ApplyDeviceConfigurationResultAsync(channelResult);

        var updatedChannelNode = vm.TreeNodes.Single().Children.Single(node => node.Code == "CH_SIM");
        Assert.Equal("仿真通道（已更新）", updatedChannelNode.DisplayText);
        Assert.Equal("仿真通道（已更新）", harness.Services.DeviceConfig.Channels
            .Single(item => item.Code == "CH_SIM").Name);
    }

    [Fact]
    public async Task TemplateScope_FollowsSelectedDeviceAndCommunicationMethod()
    {
        using var harness = AppTestHarness.Create(grouped: true);
        var actor = await harness.AdminActorAsync();
        var vm = new DevicePointManagementViewModel(harness.Services, actor);

        await vm.LoadAsync();
        var channelNode = vm.TreeNodes.Single().Children.Single(node => node.Code == "CH_SIM");
        var deviceNode = channelNode.Children.Single(node => node.Code == "DEV_A");

        Assert.NotNull(deviceNode);
        vm.SelectedTreeNode = deviceNode;

        var deviceRequest = vm.CreateTemplateExportRequest();

        Assert.NotNull(deviceRequest);
        var deviceTarget = Assert.Single(deviceRequest!.Targets);
        Assert.Equal(deviceNode!.Id, deviceTarget.DeviceId);
        Assert.Equal(deviceNode.Code, deviceTarget.DeviceCode);

        vm.SelectedTreeNode = channelNode;
        var channelRequest = vm.CreateTemplateExportRequest();
        Assert.NotNull(channelRequest);
        Assert.True(string.IsNullOrWhiteSpace(channelRequest!.DefaultGroupCode));

        vm.SelectedTreeNode = deviceNode.Children.Single(node => node.Code == "PRESSURE");
        var groupRequest = vm.CreateTemplateExportRequest();

        Assert.NotNull(groupRequest);
        Assert.Single(groupRequest!.Targets);
        Assert.Equal("PRESSURE", groupRequest.DefaultGroupCode);
        Assert.Single(vm.TemplateScopeOptions);
        Assert.DoesNotContain("migration-", vm.ActiveRevisionText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("migration-", vm.SourceText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Tree_PresentsChannelBeforeDeviceAndGroupsAsLeaves()
    {
        using var harness = AppTestHarness.Create();
        var actor = await harness.AdminActorAsync();
        var vm = new DevicePointManagementViewModel(harness.Services, actor);

        await vm.LoadAsync();

        var root = Assert.Single(vm.TreeNodes);
        Assert.Equal("root", root.Code);
        Assert.Equal("全部设备", root.DisplayText);

        var channel = Assert.Single(root.Children);
        Assert.Equal(DevicePointTreeNodeKind.Channel, channel.Kind);
        Assert.False(channel.IsVirtual);
        var device = Assert.Single(channel.Children);
        Assert.Equal(DevicePointTreeNodeKind.Device, device.Kind);
        Assert.Equal(channel.Id, device.ParentId);
        Assert.Equal(device.Id, device.OwnerDeviceId);

        Assert.Empty(device.Children);
    }

    [Fact]
    public async Task Tree_ShowsOnlyRootAndGroupSummaries()
    {
        using var harness = AppTestHarness.Create();
        var actor = await harness.AdminActorAsync();
        var vm = new DevicePointManagementViewModel(harness.Services, actor);

        await vm.LoadAsync();

        var root = Assert.Single(vm.TreeNodes);
        var channel = Assert.Single(root.Children, child => child.Code == "CH_SIM");
        var device = Assert.Single(channel.Children, child => child.Kind == DevicePointTreeNodeKind.Device);
        Assert.Contains("1 个设备", root.TreeSummaryText, StringComparison.Ordinal);
        Assert.Contains("2 个点位", device.TreeSummaryText, StringComparison.Ordinal);
        Assert.True(root.HasTreeSummaryText);
        Assert.True(device.HasTreeSummaryText);
        Assert.True(root.IsTreeSummaryVisible);
        Assert.False(channel.IsTreeSummaryVisible);
        Assert.False(device.IsTreeSummaryVisible);
        Assert.Empty(device.Children);
    }

    [Fact]
    public async Task PointRows_ExposeReferenceFieldsAndContextActions()
    {
        using var harness = AppTestHarness.Create(grouped: true);
        var actor = await harness.AdminActorAsync();
        var vm = new DevicePointManagementViewModel(harness.Services, actor);

        await vm.LoadAsync();

        var row = Assert.Single(vm.Points, point => point.Code == "A_PRESSURE");
        Assert.Contains("500", row.PollIntervalText, StringComparison.Ordinal);
        Assert.Equal(string.Empty, row.Description);
        Assert.Equal("只读", row.WritePolicyText);
        Assert.Equal("读写", vm.Points.Single(point => point.Code == "B_START").WritePolicyText);

        vm.SelectedPoint = row;
        var actions = vm.GetPointRowActions(row);
        Assert.Equal(
            new[]
            {
                DevicePointTreeActionKind.CopyPoint,
                DevicePointTreeActionKind.CutPoint,
                DevicePointTreeActionKind.EditPoint,
                DevicePointTreeActionKind.DiagnosePoint,
                DevicePointTreeActionKind.MovePoint,
                DevicePointTreeActionKind.DeletePoint
            },
            actions.Select(action => action.Kind));

        var viewer = new UserContext
        {
            LoginName = "viewer",
            DisplayName = "查看者",
            Role = new Role { Name = "Viewer" }
        };
        var readOnlyViewModel = new DevicePointManagementViewModel(harness.Services, viewer);
        await readOnlyViewModel.LoadAsync();

        Assert.Empty(readOnlyViewModel.GetPointRowActions(row));
    }

    [Fact]
    public async Task ValidateImport_DoesNotReplaceUntilApplyIsConfirmed()
    {
        using var harness = AppTestHarness.Create(grouped: true);
        var actor = await harness.AdminActorAsync();
        var vm = new DevicePointManagementViewModel(harness.Services, actor);
        var path = NewTestPath("testbench-point-preview-", ".csv");
        try
        {
            await vm.LoadAsync();
            vm.SelectedTreeNode = vm.TreeNodes.Single().Children.Single(node => node.Code == "CH_SIM")
                .Children.Single(node => node.Code == "DEV_A");
            var headers = string.Join(',', DevicePointTemplateDefinition.Columns.Select(column => column.Header));
            var row = string.Join(',', "AI_Preview", "DB1.DBD8", "小数", "", "", "只读", "", "", "", "", "预览压力");
            await File.WriteAllTextAsync(path, $"{headers}\n{row}");

            var result = await vm.ValidateImportAsync(path);

            Assert.True(result.IsValid, string.Join("；", result.Issues.Select(issue => issue.Message)));
            Assert.Equal(2, vm.Points.Count);
            Assert.Contains("校验通过", vm.StatusMessage, StringComparison.Ordinal);

            await vm.ApplyImportedPointsAsync(result);

            Assert.Equal(3, vm.Points.Count);
            Assert.Contains(vm.Points, point => point.Code == "AI_Preview");
        }
        finally
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    private static DevicePointTreeNodeViewModel? FindNode(
        DevicePointTreeNodeViewModel node,
        DevicePointTreeNodeKind kind)
    {
        if (node.Kind == kind)
            return node;

        foreach (var child in node.Children)
        {
            var found = FindNode(child, kind);
            if (found is not null)
                return found;
        }

        return null;
    }

    private static string NewTestPath(string prefix, string extension)
    {
        var directory = @"D:\Codex相关\设备点位简化改造\test-inputs";
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, prefix + Guid.NewGuid().ToString("N") + extension);
    }
}
