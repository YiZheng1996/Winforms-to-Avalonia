using XXX.TestBench.App.ViewModels;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.App.Tests;

public sealed class DevicePointImportPreviewViewModelTests
{

    [Fact]
    public void Preview_FromUnifiedPlan_UsesPlanCountsAndHighRiskHint()
    {
        var deviceId = "device-a";
        var groupId = "group-a";
        var existing = new PointsConfig.PointEntry
        {
            Id = Guid.NewGuid().ToString(),
            Code = "START",
            Name = "启动命令",
            DeviceId = deviceId,
            DeviceCode = "DEV_A",
            GroupId = groupId,
            GroupCode = "AI",
            Address = "sim.start",
            DataType = "Boolean",
            RawDataType = "Boolean",
            IsWritable = true,
            RiskLevel = WriteRiskLevel.HighRisk
        };
        var incoming = new PointsConfig.PointEntry
        {
            Id = existing.Id,
            Code = existing.Code,
            Name = existing.Name,
            DeviceId = deviceId,
            DeviceCode = "DEV_A",
            GroupId = groupId,
            GroupCode = "AI",
            Address = existing.Address,
            DataType = existing.DataType,
            RawDataType = existing.RawDataType,
            IsWritable = true,
            RiskLevel = WriteRiskLevel.Normal,
            Description = "维护说明"
        };
        var plan = new XXX.TestBench.Core.Application.DevicePointImportPlanner().Build(
            new[] { new DevicePointImportRow(2, incoming) },
            new[] { existing },
            new[] { new PointsConfig.PointGroupEntry { Id = groupId, DeviceId = deviceId, Code = "AI", Name = "模拟量" } });

        var preview = new DevicePointImportPreviewViewModel(
            plan,
            1,
            new[] { new PointsConfig.PointGroupEntry { Id = groupId, DeviceId = deviceId, Code = "AI", Name = "模拟量" } });

        Assert.True(preview.CanApply);
        Assert.Equal(1, preview.UpdatedCount);
        Assert.Contains("高风险", preview.SafetyText, StringComparison.Ordinal);
    }

    [Fact]
    public void Preview_ResolvesPersistedGroupIdBeforeMatchingSimplifiedTag()
    {
        var deviceId = Guid.NewGuid().ToString();
        var groupId = Guid.NewGuid().ToString();
        var current = new PointsConfig.PointEntry
        {
            Id = Guid.NewGuid().ToString(),
            Code = "AI.L32",
            Name = "L32",
            DeviceId = deviceId,
            GroupId = groupId
        };
        var incoming = new PointsConfig.PointEntry
        {
            Code = "AI.L32",
            Name = "L32",
            DeviceId = deviceId,
            GroupId = groupId,
            GroupCode = "AI"
        };
        var groups = new[]
        {
            new PointsConfig.PointGroupEntry
            {
                Id = groupId,
                DeviceId = deviceId,
                Code = "AI",
                Name = "模拟量输入"
            }
        };

        var row = new DevicePointImportPreviewRow(incoming, new[] { current }, groups);

        Assert.Equal("AI.L32", row.PointTag);
        Assert.Equal("无变化", row.Operation);
        Assert.Empty(row.Conflict);
    }

    [Fact]
    public void Preview_CountsConflictsOutsideTheVisiblePreviewWindow()
    {
        var deviceId = Guid.NewGuid().ToString();
        var otherDeviceId = Guid.NewGuid().ToString();
        var points = Enumerable.Range(1, 101)
            .Select(index => new PointsConfig.PointEntry
            {
                Id = Guid.NewGuid().ToString(),
                Code = $"AI_{index}",
                Name = $"点位 {index}",
                DeviceId = deviceId,
                DeviceCode = "DEV_A",
                GroupCode = "AI",
                Address = $"sim.ai.{index}",
                Protocol = "Simulation",
                DataType = "Decimal",
                RawDataType = "Decimal"
            })
            .ToList();
        var conflict = points[^1];
        var current = new PointsConfig.PointEntry
        {
            Id = Guid.NewGuid().ToString(),
            Code = conflict.Code,
            Name = "另一个设备点位",
            DeviceId = otherDeviceId,
            DeviceCode = "DEV_B",
            Address = conflict.Address,
            Protocol = "Simulation",
            DataType = "Decimal",
            RawDataType = "Decimal"
        };

        var preview = new DevicePointImportPreviewViewModel(
            points,
            currentPointCount: 1,
            new[] { current });

        Assert.Equal(100, preview.Rows.Count);
        Assert.Equal(1, preview.HiddenRowCount);
        Assert.Equal(1, preview.ConflictCount);
        Assert.False(preview.CanApply);
        Assert.Contains("全部 101 条", preview.PreviewLimitText, StringComparison.Ordinal);
    }

    [Fact]
    public void Preview_DistinguishesUpdateFromUnchangedPoint()
    {
        var deviceId = Guid.NewGuid().ToString();
        var groupId = Guid.NewGuid().ToString();
        var current = new PointsConfig.PointEntry
        {
            Id = Guid.NewGuid().ToString(),
            Code = "AI.Pressure",
            Name = "进气压力",
            DeviceId = deviceId,
            DeviceCode = "DEV_A",
            GroupId = groupId,
            GroupCode = "AI",
            Address = "sim.pressure",
            Protocol = "Simulation",
            DataType = "Decimal",
            RawDataType = "Decimal"
        };
        var changed = new PointsConfig.PointEntry
        {
            Id = current.Id,
            Code = current.Code,
            Name = "出口压力",
            DeviceId = deviceId,
            DeviceCode = "DEV_A",
            GroupId = groupId,
            GroupCode = "AI",
            Address = current.Address,
            Protocol = current.Protocol,
            DataType = current.DataType,
            RawDataType = current.RawDataType
        };

        var preview = new DevicePointImportPreviewViewModel(
            new[] { changed },
            currentPointCount: 1,
            new[] { current });

        Assert.Equal("更新", Assert.Single(preview.Rows).Operation);
        Assert.Equal(1, preview.UpdatedCount);
        Assert.Equal(0, preview.UnchangedCount);
    }

    [Fact]
    public void Preview_ShowsOriginalSourceRowForVisibleConflict()
    {
        var incoming = new PointsConfig.PointEntry
        {
            Code = "AI.Pressure",
            Name = "进气压力",
            DeviceId = "device-a",
            DeviceCode = "DEV_A",
            Address = "sim.pressure",
            Protocol = "Simulation",
            DataType = "Decimal",
            RawDataType = "Decimal"
        };
        var current = new PointsConfig.PointEntry
        {
            Id = Guid.NewGuid().ToString(),
            Code = incoming.Code,
            Name = "其他设备压力",
            DeviceId = "device-b",
            DeviceCode = "DEV_B",
            Address = incoming.Address,
            Protocol = incoming.Protocol,
            DataType = incoming.DataType,
            RawDataType = incoming.RawDataType
        };

        var preview = new DevicePointImportPreviewViewModel(
            new[] { incoming },
            currentPointCount: 1,
            new[] { current },
            currentGroups: Array.Empty<PointsConfig.PointGroupEntry>(),
            parsedRows: new[] { new DevicePointImportRow(17, incoming) });

        var row = Assert.Single(preview.Rows);
        Assert.Equal(17, row.RowNumber);
        Assert.Equal("冲突", row.Operation);
        Assert.Contains("第 17 行", row.Conflict, StringComparison.Ordinal);
    }
}
