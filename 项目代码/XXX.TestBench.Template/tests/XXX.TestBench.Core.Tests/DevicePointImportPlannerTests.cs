using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Ports;

namespace XXX.TestBench.Core.Tests;

public sealed class DevicePointImportPlannerTests
{
    private const string DeviceA = "20000000-0000-0000-0000-000000000001";
    private const string DeviceB = "20000000-0000-0000-0000-000000000002";
    private const string GroupA = "30000000-0000-0000-0000-000000000001";
    private const string GroupB = "30000000-0000-0000-0000-000000000002";

    [Fact]
    public void MatchedHighRiskPoint_PreservesRiskLevel()
    {
        var existing = Point("40000000-0000-0000-0000-000000000001", "START", DeviceA, GroupA,
            writable: true, risk: WriteRiskLevel.HighRisk);
        var incoming = IncomingFrom(existing);
        incoming.Name = "启动命令（批量维护）";

        var row = SingleRow(incoming, existing);

        Assert.Equal(DevicePointImportOperationKind.Update, row.Operation);
        Assert.Equal(WriteRiskLevel.HighRisk, row.Candidate!.RiskLevel);
    }

    [Fact]
    public void MatchedPoint_PreservesWritePolicyAndDecodeOptions()
    {
        var existing = Point("40000000-0000-0000-0000-000000000001", "AI", DeviceA, GroupA,
            writable: true, risk: WriteRiskLevel.Normal);
        existing.WritePolicy = PointWritePolicy.ReadBackEqual;
        existing.DecodeOptions = new DecodeOptions { ByteOrder = ByteOrder.BigEndian, WordOrder = WordOrder.LowWordFirst };
        var incoming = IncomingFrom(existing);
        incoming.Description = "批量维护";

        var row = SingleRow(incoming, existing);

        Assert.NotNull(row.Candidate);
        Assert.Equal(existing.WritePolicy, row.Candidate!.WritePolicy);
        Assert.Equal(existing.DecodeOptions.ByteOrder, row.Candidate.DecodeOptions.ByteOrder);
        Assert.Equal(existing.DecodeOptions.WordOrder, row.Candidate.DecodeOptions.WordOrder);
    }

    [Fact]
    public void ImportChangingPointToReadOnly_ResetsRiskToNormal()
    {
        var existing = Point("40000000-0000-0000-0000-000000000001", "START", DeviceA, GroupA,
            writable: true, risk: WriteRiskLevel.HighRisk);
        var incoming = IncomingFrom(existing);
        incoming.IsWritable = false;

        var row = SingleRow(incoming, existing);

        Assert.Equal(WriteRiskLevel.Normal, row.Candidate!.RiskLevel);
        Assert.False(row.Candidate.IsWritable);
    }

    [Fact]
    public void NewWritablePoint_DefaultsToNormalRisk()
    {
        var incoming = Point(string.Empty, "NEW_START", DeviceA, GroupA,
            writable: true, risk: WriteRiskLevel.HighRisk);

        var row = SingleRow(incoming);

        Assert.Equal(DevicePointImportOperationKind.Add, row.Operation);
        Assert.Equal(WriteRiskLevel.Normal, row.Candidate!.RiskLevel);
        Assert.NotEqual(string.Empty, row.Candidate.Id);
    }

    [Fact]
    public void SameDeviceCodeMatch_ProducesUpdate()
    {
        var existing = Point("40000000-0000-0000-0000-000000000001", "AI", DeviceA, GroupA);
        var incoming = IncomingFrom(existing);
        incoming.Id = string.Empty;
        incoming.Name = "新的显示名称";

        var row = SingleRow(incoming, existing);

        Assert.Equal(DevicePointImportOperationKind.Update, row.Operation);
        Assert.Equal(existing.Id, row.Candidate!.Id);
    }

    [Fact]
    public void SameDeviceTagMatch_ProducesUpdate()
    {
        var existing = Point("40000000-0000-0000-0000-000000000001", "OLD_CODE", DeviceA, GroupA);
        var incoming = IncomingFrom(existing);
        incoming.Id = string.Empty;
        incoming.Code = "NEW_CODE";
        incoming.Name = existing.Name;

        var row = SingleRow(incoming, existing);

        Assert.Equal(DevicePointImportOperationKind.Update, row.Operation);
        Assert.Equal(existing.Id, row.Candidate!.Id);
        Assert.Equal("NEW_CODE", row.Candidate.Code);
    }

    [Fact]
    public void CrossDeviceCodeConflict_IsRejected()
    {
        var existing = Point("40000000-0000-0000-0000-000000000001", "AI", DeviceB, GroupB);
        var incoming = Point(string.Empty, "AI", DeviceA, GroupA);

        var row = SingleRow(incoming, existing);

        Assert.Equal(DevicePointImportOperationKind.Conflict, row.Operation);
        Assert.Contains(row.Issues, issue => issue.Contains("其他设备", StringComparison.Ordinal));
    }

    [Fact]
    public void SameDeviceCanonicalAddressConflict_IsRejected()
    {
        var existing = Point("40000000-0000-0000-0000-000000000001", "AI_1", DeviceA, GroupA);
        existing.Address = "DB1.DBD0";
        var incoming = Point(string.Empty, "AI_2", DeviceA, GroupA);
        incoming.Address = "DB1.DBD0";

        var row = SingleRow(incoming, existing);

        Assert.Equal(DevicePointImportOperationKind.Conflict, row.Operation);
        Assert.Contains(row.Issues, issue => issue.Contains("地址", StringComparison.Ordinal));
    }

    [Fact]
    public void GroupOnlyChange_IsReportedAsMoveGroup()
    {
        var existing = Point("40000000-0000-0000-0000-000000000001", "AI", DeviceA, GroupA);
        var incoming = IncomingFrom(existing);
        incoming.GroupId = GroupB;
        incoming.GroupCode = "OUTPUT";

        var row = SingleRow(incoming, existing,
            new PointsConfig.PointGroupEntry { Id = GroupB, DeviceId = DeviceA, Code = "OUTPUT", Name = "输出" });

        Assert.Equal(DevicePointImportOperationKind.MoveGroup, row.Operation);
    }

    [Fact]
    public void UnchangedRow_IsNotReportedAsUpdate()
    {
        var existing = Point("40000000-0000-0000-0000-000000000001", "AI", DeviceA, GroupA);
        var incoming = IncomingFrom(existing);

        var row = SingleRow(incoming, existing);

        Assert.Equal(DevicePointImportOperationKind.Unchanged, row.Operation);
        Assert.Equal(0, SinglePlan(new[] { new DevicePointImportRow(2, incoming) }, new[] { existing }).UpdatedCount);
    }

    [Fact]
    public void PlanMergedPoints_KeepUnmentionedExistingPoints()
    {
        var existing1 = Point("40000000-0000-0000-0000-000000000001", "AI_1", DeviceA, GroupA);
        var existing2 = Point("40000000-0000-0000-0000-000000000002", "AI_2", DeviceA, GroupA);
        var incoming = IncomingFrom(existing1);
        incoming.Description = "更新说明";

        var plan = new DevicePointImportPlanner().Build(
            new[] { new DevicePointImportRow(2, incoming) },
            new[] { existing1, existing2 },
            Groups());

        Assert.Contains(plan.MergedPoints, point => point.Id == existing2.Id);
        Assert.Equal(2, plan.MergedPoints.Count);
    }

    [Fact]
    public void PlanCandidateUsesNewIdForNewPoint()
    {
        var incoming = Point(string.Empty, "NEW_POINT", DeviceA, GroupA);

        var plan = SinglePlan(new[] { new DevicePointImportRow(2, incoming) }, Array.Empty<PointsConfig.PointEntry>(), Groups());

        Assert.Equal(DevicePointImportOperationKind.Add, plan.Rows[0].Operation);
        Assert.NotEqual(string.Empty, plan.Rows[0].Candidate!.Id);
        Assert.DoesNotContain(Guid.Empty.ToString("D"), new[] { plan.Rows[0].Candidate!.Id });
    }

    private static DevicePointImportPlanRow SingleRow(
        PointsConfig.PointEntry incoming,
        PointsConfig.PointEntry? existing = null,
        params PointsConfig.PointGroupEntry[] extraGroups)
    {
        var current = existing is null ? Array.Empty<PointsConfig.PointEntry>() : new[] { existing };
        return SinglePlan(new[] { new DevicePointImportRow(2, incoming) }, current,
            Groups().Concat(extraGroups).ToList()).Rows.Single();
    }

    private static DevicePointImportPlan SinglePlan(
        IReadOnlyList<DevicePointImportRow> rows,
        IReadOnlyList<PointsConfig.PointEntry>? current = null,
        IReadOnlyList<PointsConfig.PointGroupEntry>? groups = null)
        => new DevicePointImportPlanner().Build(
            rows,
            current ?? Array.Empty<PointsConfig.PointEntry>(),
            groups ?? Groups());

    private static PointsConfig.PointEntry IncomingFrom(PointsConfig.PointEntry existing)
        => new()
        {
            Id = existing.Id,
            Code = existing.Code,
            Name = existing.Name,
            Protocol = existing.Protocol,
            DeviceId = existing.DeviceId,
            DeviceCode = existing.DeviceCode,
            GroupId = existing.GroupId,
            GroupCode = existing.GroupCode,
            Address = existing.Address,
            AddressDefinition = existing.AddressDefinition,
            DataType = existing.DataType,
            RawDataType = existing.RawDataType,
            DecodeOptions = existing.DecodeOptions,
            WritePolicy = existing.WritePolicy,
            IsWritable = existing.IsWritable,
            RiskLevel = existing.RiskLevel,
            RawMin = existing.RawMin,
            RawMax = existing.RawMax,
            EngMin = existing.EngMin,
            EngMax = existing.EngMax,
            Description = existing.Description
        };

    private static PointsConfig.PointEntry Point(
        string id,
        string code,
        string deviceId,
        string groupId,
        bool writable = false,
        WriteRiskLevel risk = WriteRiskLevel.Normal)
        => new()
        {
            Id = id,
            Code = code,
            Name = code,
            Protocol = "Simulation",
            DeviceId = deviceId,
            DeviceCode = deviceId == DeviceA ? "DEV_A" : "DEV_B",
            GroupId = groupId,
            GroupCode = groupId == GroupA ? "AI" : "OUTPUT",
            Address = "sim." + code,
            DataType = "Decimal",
            RawDataType = "Decimal",
            IsWritable = writable,
            RiskLevel = risk,
            WritePolicy = PointWritePolicy.ReadBackEqual,
            DecodeOptions = new DecodeOptions()
        };

    private static IReadOnlyList<PointsConfig.PointGroupEntry> Groups()
        => new[]
        {
            new PointsConfig.PointGroupEntry { Id = GroupA, DeviceId = DeviceA, Code = "AI", Name = "模拟量" },
            new PointsConfig.PointGroupEntry { Id = GroupA, DeviceId = DeviceB, Code = "AI", Name = "模拟量" },
            new PointsConfig.PointGroupEntry { Id = GroupB, DeviceId = DeviceA, Code = "OUTPUT", Name = "输出" }
        };
}
