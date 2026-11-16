using XXX.TestBench.App.ViewModels;
using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Ports;

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
            source.Entry.RawMin,
            source.Entry.RawMax,
            source.Entry.EngMin,
            source.Entry.EngMax,
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
        var exporter = new BlockingTemplateExporter();
        var services = harness.Services with { DevicePointTemplateExporter = exporter };
        var vm = new DevicePointManagementViewModel(services, actor);
        await vm.LoadAsync();
        vm.SelectedTreeNode = vm.TreeNodes.Single();

        var first = vm.DownloadTemplateAsync(Path.Combine(harness.Root, "busy-template.xlsx"));
        await exporter.Started.Task;

        Assert.True(vm.IsBusy);
        var second = await vm.RefreshAsync();
        Assert.False(second.Succeeded);
        Assert.Contains("正在执行", second.Message, StringComparison.Ordinal);
        Assert.True(vm.IsBusy);

        exporter.Release.TrySetResult(true);
        var firstFeedback = await first;
        Assert.True(firstFeedback.Succeeded, firstFeedback.Message);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task CutPasteToSameGroupDoesNotCreateRevisionOrClearClipboard()
    {
        using var harness = AppTestHarness.Create(grouped: true);
        var actor = await harness.AdminActorAsync();
        var vm = new DevicePointManagementViewModel(harness.Services, actor);
        await vm.LoadAsync();
        var source = vm.Points.Single(row => row.Code == "A_PRESSURE");
        vm.SelectedPoint = source;
        vm.SelectedTreeNode = vm.TreeNodes.Single().Children
            .Single(channel => channel.Code == "CH_SIM")
            .Children.Single(node => node.Code == "DEV_A")
            .Children.Single(node => node.Code == "PRESSURE");
        Assert.True(vm.CutSelectedPoint().Succeeded);
        var revision = harness.Services.DeviceModes.Runtime?.ActiveRevision;

        var draft = vm.CreatePasteDraft();
        Assert.NotNull(draft);
        var feedback = await vm.ApplyCutPasteAsync(draft!);

        Assert.True(feedback.Succeeded, feedback.Message);
        Assert.Contains("已在该分组", feedback.Message, StringComparison.Ordinal);
        Assert.Equal(revision, harness.Services.DeviceModes.Runtime?.ActiveRevision);
        Assert.True(vm.CanPastePoint);
        Assert.Contains(vm.CurrentEntries, point => point.Id == source.PointId);
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
            var row = string.Join(',', "AI_PLAN", "DB1.DBD40", "小数", "只读", "", "", "", "", "计划测试");
            await File.WriteAllTextAsync(path, $"{headers}\n{row}");
            var plan = await vm.CreateImportPlanAsync(path);
            Assert.NotNull(plan);
            Assert.True(plan!.CanApply, string.Join("；", plan.Rows.SelectMany(item => item.Issues)));

            var active = await harness.Services.DeviceConfigurations!.LoadActiveAsync();
            var applied = await harness.Services.DeviceConfigurations.ApplyPointsAsync(
                actor,
                active.Points.Points.Select(ClonePoint).ToList(),
                active.Points.Groups,
                new DeviceConfigurationApplyOptions());
            Assert.True(applied.Ok, applied.Error);

            var feedback = await vm.ApplyImportedPointsAsync(plan, new DeviceConfigurationApplyOptions());

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
        var dir = @"D:\Codex相关\设备点位审计整改\impl-20260915\test-inputs";
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
            S7DecodeOptions = source.S7DecodeOptions,
            WritePolicy = source.WritePolicy,
            IsWritable = source.IsWritable,
            RiskLevel = source.RiskLevel,
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
            source.Entry.RawMin,
            source.Entry.RawMax,
            source.Entry.EngMin,
            source.Entry.EngMax,
            source.Entry.IsWritable,
            source.Entry.RiskLevel,
            source.Description,
            string.Empty,
            source.Entry.DeviceId,
            source.DeviceCode,
            groupId,
            source.Entry.AddressDefinition,
            source.Entry.S7DecodeOptions,
            source.Entry.WritePolicy);
    }

    private sealed class BlockingTemplateExporter : IDevicePointTemplateExporter
    {
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ExportAsync(string filePath, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task ExportAsync(
            string filePath,
            DevicePointTemplateExportRequest request,
            CancellationToken ct = default)
        {
            Started.TrySetResult(true);
            return Release.Task;
        }
    }
}
