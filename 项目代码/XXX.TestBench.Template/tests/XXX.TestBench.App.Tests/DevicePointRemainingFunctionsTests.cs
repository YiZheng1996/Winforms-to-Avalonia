using System.Reflection;
using XXX.TestBench.App.ViewModels;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;

namespace XXX.TestBench.App.Tests;

public sealed class DevicePointRemainingFunctionsTests
{
    [Fact]
    public async Task CopyPaste_CreatesNewPointIdAndKeepsSource()
    {
        using var harness = AppTestHarness.Create(grouped: true);
        var actor = await harness.AdminActorAsync();
        var vm = new DevicePointManagementViewModel(harness.Services, actor);
        await vm.LoadAsync();
        var source = vm.Points.Single(row => row.Code == "A_TEMP");
        vm.SelectedPoint = source;
        vm.SelectedTreeNode = DeviceNode(vm, "DEV_A");

        Assert.True(vm.CopySelectedPoint().Succeeded);
        var result = CopyResult(vm, source, "A_TEMP_COPY", "入口温度副本", "DB1.DBD20");
        var feedback = await vm.ApplyPasteAsync(result);

        Assert.True(feedback.Succeeded, feedback.Message);
        var copied = Assert.Single(vm.CurrentEntries, point => point.Name == "入口温度副本");
        Assert.NotEqual(source.PointId, copied.Id);
        Assert.Contains(vm.CurrentEntries, point => point.Id == source.PointId);
        Assert.Equal(4, vm.CurrentEntries.Count);
    }

    [Fact]
    public async Task RepeatedCopyPaste_CreatesUniquePointIds()
    {
        using var harness = AppTestHarness.Create(grouped: true);
        var actor = await harness.AdminActorAsync();
        var vm = new DevicePointManagementViewModel(harness.Services, actor);
        await vm.LoadAsync();
        var source = vm.Points.Single(row => row.Code == "A_TEMP");
        vm.SelectedPoint = source;
        vm.SelectedTreeNode = DeviceNode(vm, "DEV_A");
        Assert.True(vm.CopySelectedPoint().Succeeded);

        Assert.True((await vm.ApplyPasteAsync(CopyResult(vm, source, "COPY_1", "复制一", "DB1.DBD20"))).Succeeded);
        Assert.True((await vm.ApplyPasteAsync(CopyResult(vm, source, "COPY_2", "复制二", "DB1.DBD24"))).Succeeded);

        var ids = vm.CurrentEntries
            .Where(point => point.Code is "COPY_1" or "COPY_2")
            .Select(point => point.Id)
            .ToList();
        Assert.Equal(2, ids.Count);
        Assert.Equal(2, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public async Task CutPasteWithinSameDevice_MovesExistingPoint()
    {
        using var harness = AppTestHarness.Create(grouped: true);
        var actor = await harness.AdminActorAsync();
        var vm = new DevicePointManagementViewModel(harness.Services, actor);
        await vm.LoadAsync();
        var source = vm.Points.Single(row => row.Code == "A_PRESSURE");
        vm.SelectedPoint = source;
        Assert.True(vm.CutSelectedPoint().Succeeded);

        var defaultGroup = vm.CurrentGroups.Single(group =>
            group.DeviceId == source.Entry.DeviceId
            && group.Code == "DEFAULT");
        vm.SelectedTreeNode = DeviceNode(vm, "DEV_A");
        var result = new DevicePointDialogResult(
            source.Code,
            source.Name,
            source.Entry.ProtocolKind,
            source.Address,
            source.Entry.DataTypeKind,
            source.Entry.EffectiveRawMin,
            source.Entry.EffectiveRawMax,
            source.Entry.EffectiveEngMin,
            source.Entry.EffectiveEngMax,
            source.Entry.IsWritable,
            source.Entry.RiskLevel,
            source.Description,
            source.PointId,
            source.Entry.DeviceId,
            source.DeviceCode,
            defaultGroup.Id);

        var feedback = await vm.ApplyPasteAsync(result);

        Assert.True(feedback.Succeeded, feedback.Message);
        Assert.Equal(3, vm.CurrentEntries.Count);
        var moved = Assert.Single(vm.CurrentEntries, point => point.Id == source.PointId);
        Assert.Equal(defaultGroup.Id, moved.GroupId);
        Assert.False(vm.CanPastePoint);
    }

    [Fact]
    public async Task CutPasteAcrossDevices_IsRejected()
    {
        using var harness = AppTestHarness.Create(grouped: true);
        var actor = await harness.AdminActorAsync();
        var vm = new DevicePointManagementViewModel(harness.Services, actor);
        await vm.LoadAsync();
        var source = vm.Points.Single(row => row.Code == "A_TEMP");
        vm.SelectedPoint = source;
        Assert.True(vm.CutSelectedPoint().Succeeded);

        vm.SelectedTreeNode = DeviceNode(vm, "DEV_B");

        Assert.Null(vm.CreatePasteDraft());
        Assert.Contains("同一设备", vm.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PasteFailure_KeepsClipboardAndSource()
    {
        using var harness = AppTestHarness.Create(grouped: true);
        var actor = await harness.AdminActorAsync();
        var vm = new DevicePointManagementViewModel(harness.Services, actor);
        await vm.LoadAsync();
        var source = vm.Points.Single(row => row.Code == "A_TEMP");
        vm.SelectedPoint = source;
        vm.SelectedTreeNode = DeviceNode(vm, "DEV_A");
        Assert.True(vm.CopySelectedPoint().Succeeded);

        var duplicateAddress = CopyResult(vm, source, "COPY_DUP", "重复地址", source.Address);
        var feedback = await vm.ApplyPasteAsync(duplicateAddress);

        Assert.False(feedback.Succeeded);
        Assert.True(vm.CanPastePoint);
        Assert.Contains(vm.CurrentEntries, point => point.Id == source.PointId);
        Assert.DoesNotContain(vm.CurrentEntries, point => point.Code == "COPY_DUP");
    }

    [Fact]
    public async Task BusyMutation_RejectsSecondOperation()
    {
        using var harness = AppTestHarness.Create(grouped: true);
        var actor = await harness.AdminActorAsync();
        var vm = new DevicePointManagementViewModel(harness.Services, actor);
        await vm.LoadAsync();
        var source = vm.Points.Single(row => row.Code == "A_TEMP");
        vm.SelectedPoint = source;
        vm.SelectedTreeNode = DeviceNode(vm, "DEV_A");
        var gateField = typeof(DevicePointManagementViewModel).GetField(
            "_pageOperationGate",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var gate = Assert.IsType<SemaphoreSlim>(gateField!.GetValue(vm));
        await gate.WaitAsync();
        try
        {
            var feedback = await vm.CreateFromDialogAsync(CopyResult(vm, source, "COPY_BUSY", "忙碌复制", "DB1.DBD28"));
            Assert.False(feedback.Succeeded);
            Assert.Contains("正在执行", feedback.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(vm.CurrentEntries, point => point.Code == "COPY_BUSY");
        }
        finally
        {
            gate.Release();
        }
    }

    [Fact]
    public async Task ExportRequest_FollowsRootChannelDeviceAndGroupScope()
    {
        using var harness = AppTestHarness.Create(grouped: true);
        var actor = await harness.AdminActorAsync();
        var vm = new DevicePointManagementViewModel(harness.Services, actor);
        await vm.LoadAsync();

        vm.SelectedTreeNode = vm.TreeNodes.Single();
        var root = vm.CreateCatalogExportRequest();
        Assert.NotNull(root);
        Assert.Equal(3, root!.Points.Count);
        Assert.Equal(2, root.Devices.Count);

        var channel = vm.TreeNodes.Single().Children.Single(node => node.Code == "CH_SIM");
        vm.SelectedTreeNode = channel;
        var channelRequest = vm.CreateCatalogExportRequest();
        Assert.NotNull(channelRequest);
        Assert.Equal(3, channelRequest!.Points.Count);

        var device = channel.Children.Single(node => node.Code == "DEV_A");
        vm.SelectedTreeNode = device;
        var deviceRequest = vm.CreateCatalogExportRequest();
        Assert.NotNull(deviceRequest);
        Assert.Equal(2, deviceRequest!.Points.Count);
        Assert.Single(deviceRequest.Devices);

        var group = device.Children.Single(node => node.Code == "PRESSURE");
        vm.SelectedTreeNode = group;
        var groupRequest = vm.CreateCatalogExportRequest();
        Assert.NotNull(groupRequest);
        Assert.Single(groupRequest!.Points);
    }

    [Fact]
    public async Task ExportRequest_RejectsEmptyOrVirtualScope()
    {
        using var harness = AppTestHarness.Create(grouped: true);
        var actor = await harness.AdminActorAsync();
        var vm = new DevicePointManagementViewModel(harness.Services, actor);
        await vm.LoadAsync();

        var emptyChannel = vm.TreeNodes.Single().Children.Single(node => node.Code == "CH_EMPTY");
        vm.SelectedTreeNode = emptyChannel;

        Assert.Null(vm.CreateCatalogExportRequest());
        Assert.False(vm.CanExportCurrentRange);
    }


    [Fact]
    public async Task LoadAsync_UsesActiveSnapshot()
    {
        using var harness = AppTestHarness.Create(grouped: true);
        var actor = await harness.AdminActorAsync();
        var active = await harness.Services.DeviceConfigurations!.LoadActiveAsync();
        var vm = new DevicePointManagementViewModel(harness.Services, actor);

        await vm.LoadAsync();

        Assert.Equal(
            active.Points.Points.Select(point => point.Id).OrderBy(id => id),
            vm.CurrentEntries.Select(point => point.Id).OrderBy(id => id));
        Assert.Contains("已生效", vm.ActiveRevisionText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportPlanRevisionMismatch_IsRejectedBeforeApply()
    {
        using var harness = AppTestHarness.Create(grouped: true);
        var actor = await harness.AdminActorAsync();
        var vm = new DevicePointManagementViewModel(harness.Services, actor);
        await vm.LoadAsync();
        vm.SelectedTreeNode = DeviceNode(vm, "DEV_A");
        var path = NewImportPath();
        try
        {
            var headers = string.Join(',', DevicePointTemplateDefinition.Columns.Select(column => column.Header));
            var row = string.Join(',', "AI_PLAN", "DB1.DBD40", "小数", "", "", "只读", "", "", "", "", "计划测试");
            await File.WriteAllTextAsync(path, $"{headers}\n{row}");
            var plan = await vm.CreateImportPlanAsync(path);
            Assert.NotNull(plan);
            Assert.True(plan!.CanApply, string.Join("；", plan.Rows.SelectMany(item => item.Issues)));

            var active = await harness.Services.DeviceConfigurations!.LoadActiveAsync();
            var applied = await harness.Services.DeviceConfigurations.ApplyPointsAsync(
                actor,
                active.Points.Points.Select(ClonePoint).ToList(),
                active.Points.Groups);
            Assert.True(applied.Ok, applied.Error);

            var feedback = await vm.ApplyImportedPointsAsync(plan);

            Assert.False(feedback.Succeeded);
            Assert.Contains("配置已变化", feedback.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(vm.CurrentEntries, point => point.Code == "AI_PLAN");
        }
        finally
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    private static string NewImportPath()
    {
        var dir = @"D:\Codex相关\设备点位功能实现\test-runs";
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "import-plan-" + Guid.NewGuid().ToString("N")[..8] + ".csv");
    }

    private static PointsConfig.PointEntry ClonePoint(PointsConfig.PointEntry source)
        => new()
        {
            Id = source.Id,
            Code = source.Code,
            Name = source.Name,
            Protocol = source.Protocol,
            DeviceId = source.DeviceId,
            DeviceCode = source.DeviceCode,
            GroupId = source.GroupId,
            GroupCode = source.GroupCode,
            Address = source.Address,
            AddressDefinition = source.AddressDefinition,
            DataType = source.DataType,
            RawDataType = source.RawDataType,
            DecodeOptions = source.DecodeOptions,
            WritePolicy = source.WritePolicy,
            IsWritable = source.IsWritable,
            RiskLevel = source.RiskLevel,
            Scale = source.Scale,
            RawMin = source.RawMin,
            RawMax = source.RawMax,
            EngMin = source.EngMin,
            EngMax = source.EngMax,
            Description = source.Description
        };

    private static DevicePointTreeNodeViewModel DeviceNode(DevicePointManagementViewModel vm, string code)
        => vm.TreeNodes.Single().Children
            .SelectMany(channel => channel.Children)
            .Single(node => node.Code == code);

    private static DevicePointDialogResult CopyResult(
        DevicePointManagementViewModel vm,
        DevicePointRow source,
        string code,
        string name,
        string address)
    {
        var groupId = source.Entry.GroupId;
        if (string.IsNullOrWhiteSpace(groupId))
        {
            groupId = vm.CurrentGroups.Single(group =>
                group.DeviceId == source.Entry.DeviceId
                && group.Code == "DEFAULT").Id;
        }
        return new DevicePointDialogResult(
            code,
            name,
            source.Entry.ProtocolKind,
            address,
            source.Entry.DataTypeKind,
            source.Entry.EffectiveRawMin,
            source.Entry.EffectiveRawMax,
            source.Entry.EffectiveEngMin,
            source.Entry.EffectiveEngMax,
            source.Entry.IsWritable,
            source.Entry.RiskLevel,
            source.Description,
            string.Empty,
            source.Entry.DeviceId,
            source.DeviceCode,
            groupId,
            source.Entry.AddressDefinition,
            source.Entry.RawDataType,
            source.Entry.DecodeOptions,
            source.Entry.WritePolicy);
    }
}
