using XXX.TestBench.Core.Application;
using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;
using XXX.TestBench.Core.Ports;
using System.Text.Json;
using Xunit;

namespace XXX.TestBench.Core.Tests;

public sealed class XinjeFieldV1ImportPlannerTests
{
    [Fact]
    public void Build_RecomputesStaleCanonicalKeysAndDetectsNativeAlias()
    {
        var existing = Point("old", "D42088", "existing", DeviceId);
        existing.CanonicalAddress = "HR:4";
        var imported = Point(string.Empty, "HD1000", "new", DeviceId);
        imported.OriginalAddress = "hd01000";

        var plan = Build(imported, existing);

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Rows.Single().Issues, issue => issue.Contains("地址", StringComparison.Ordinal));
        Assert.Equal("HR:4", existing.CanonicalAddress);
        Assert.Equal("HR:42088", plan.Rows.Single().Incoming.CanonicalAddress);
        Assert.Equal("hd01000", plan.Rows.Single().Incoming.OriginalAddress);
        Assert.Equal(DevicePointImportPlanner.ComputeCatalogFingerprint(new[] { existing }),
            plan.SourceCatalogFingerprint);
    }

    [Fact]
    public void Build_AfterSerializationDetectsDuplicateDespiteNonPersistedCanonicalKey()
    {
        var existing = Point("old", "HD1000", "existing", DeviceId);
        existing.CanonicalAddress = "HR:1";
        var restored = JsonSerializer.Deserialize<PointsConfig>(JsonSerializer.Serialize(
            new PointsConfig { Points = new List<PointsConfig.PointEntry> { existing } }))!;
        var imported = Point(string.Empty, "hd01000", "new", DeviceId);

        Assert.Equal(string.Empty, restored.Points.Single().CanonicalAddress);
        var plan = Build(imported, restored.Points.Single());

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Rows.Single().Issues, issue => issue.Contains("地址", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_RejectsOverlappingRangesEvenWhenStartAddressesDiffer()
    {
        var existing = Point("old", "D100", "float", DeviceId, "Float32");
        var imported = Point(string.Empty, "D101", "word", DeviceId, "UInt16");

        var plan = Build(imported, existing);

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Rows.Single().Issues, issue => issue.Contains("重叠", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_DetectsMAndHmNativeAliasInCoilAddressSpace()
    {
        var existing = Point("old", "M49448", "existing", DeviceId);
        var imported = Point(string.Empty, "HM40", "new", DeviceId);

        var plan = Build(imported, existing);

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Rows.Single().Issues, issue => issue.Contains("地址", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_DoesNotReportSameIdentityUpdateAsAnAddressConflict()
    {
        var existing = Point("same-id", "HD1000", "same", DeviceId);
        var imported = Point("same-id", "hd01000", "same", DeviceId);
        imported.Description = "更新后的说明";

        var plan = Build(imported, existing);

        Assert.True(plan.CanApply);
        Assert.Equal(DevicePointImportOperationKind.Update, plan.Rows.Single().Operation);
        Assert.Empty(plan.Rows.Single().Issues);
    }

    [Fact]
    public void Build_SeparatesAddressRangesByDevice()
    {
        var existing = Point("other-id", "D100", "other-device", "device-b");
        var imported = Point(string.Empty, "D100", "current-device", "device-a");

        var plan = Build(imported, existing);

        Assert.True(plan.CanApply);
        Assert.Equal(DevicePointImportOperationKind.Add, plan.Rows.Single().Operation);
    }

    [Fact]
    public void Build_DoesNotFallBackToStaleKeyWhenCurrentPointCannotBeNormalized()
    {
        var existing = Point("old", "D-invalid", "broken", DeviceId);
        existing.CanonicalAddress = "HR:100";
        var imported = Point(string.Empty, "D200", "new", DeviceId);

        var plan = new DevicePointImportPlanner().Build(
            new[] { new DevicePointImportRow(2, imported) },
            new[] { existing },
            Array.Empty<PointsConfig.PointGroupEntry>(),
            Normalize,
            GetRange);

        Assert.False(plan.CanApply);
        Assert.Contains(plan.Rows.Single().Issues,
            issue => issue.Contains("无法规范化", StringComparison.Ordinal));
        Assert.Equal("HR:100", existing.CanonicalAddress);
    }

    private static DevicePointImportPlan Build(
        PointsConfig.PointEntry imported,
        PointsConfig.PointEntry existing)
        => new DevicePointImportPlanner().Build(
            new[] { new DevicePointImportRow(2, imported) },
            new[] { existing },
            Array.Empty<PointsConfig.PointGroupEntry>(),
            Normalize,
            GetRange);

    private static string Normalize(PointsConfig.PointEntry point)
    {
        var (area, offset) = Parse(point.Address);
        return $"{area}:{offset}";
    }

    private static DevicePointAddressRange GetRange(PointsConfig.PointEntry point)
    {
        var (area, offset) = Parse(point.Address);
        var length = point.DataTypeKind == DevicePointDataType.Float32
            || point.DataTypeKind == DevicePointDataType.Int32
            || point.DataTypeKind == DevicePointDataType.UInt32
                ? 2
                : 1;
        return new DevicePointAddressRange(area, offset, length);
    }

    private static (string Area, int Offset) Parse(string text)
    {
        var source = text.Trim().ToUpperInvariant();
        if (source.StartsWith("HD", StringComparison.Ordinal))
            return ("HR", checked(41088 + int.Parse(source[2..])));
        if (source.StartsWith('D'))
            return ("HR", int.Parse(source[1..]));
        if (source.StartsWith("HM", StringComparison.Ordinal))
            return ("C", checked(49408 + int.Parse(source[2..])));
        if (source.StartsWith('M'))
            return ("C", int.Parse(source[1..]));
        throw new FormatException("未知测试地址");
    }

    private static PointsConfig.PointEntry Point(
        string id,
        string address,
        string code,
        string deviceId,
        string dataType = "UInt16")
        => new()
        {
            Id = id,
            DeviceId = deviceId,
            DeviceCode = deviceId,
            Code = code,
            Name = code,
            Address = address,
            Protocol = nameof(DevicePointProtocol.ModbusTcp),
            DataType = dataType,
        };

    private const string DeviceId = "device-a";
}
