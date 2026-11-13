using XXX.TestBench.Core.Configuration;
using XXX.TestBench.Core.Domain.Devices;

namespace XXX.TestBench.Core.Tests;

public sealed class DevicePointAddressRangeValidatorTests
{
    [Fact]
    public void AdjacentHalfOpenRanges_DoNotConflict()
    {
        var points = new[]
        {
            (Point("P1", "第一点"), new DevicePointAddressRange("HR", 100, 2)),
            (Point("P2", "第二点"), new DevicePointAddressRange("HR", 102, 1))
        };

        Assert.Empty(DevicePointAddressRangeValidator.Validate(points));
    }

    [Fact]
    public void PartialOverlap_ReportsBothPointsAndRangeNames()
    {
        var first = Point("P1", "浮点值");
        var second = Point("P2", "相邻字");

        var issues = DevicePointAddressRangeValidator.Validate(new[]
        {
            (first, new DevicePointAddressRange("HR", 100, 2)),
            (second, new DevicePointAddressRange("HR", 101, 1))
        });

        Assert.Equal(2, issues.Count);
        Assert.Contains(issues, issue => ReferenceEquals(issue.Point, first)
            && issue.Message.Contains("浮点值", StringComparison.Ordinal)
            && issue.Message.Contains("相邻字", StringComparison.Ordinal));
        Assert.Contains(issues, issue => ReferenceEquals(issue.Point, second));
    }

    [Fact]
    public void AddressSpaceBoundary_UsesWideArithmeticAndAllowsLastSingleRegister()
    {
        var lastRegister = (Point("P1", "末地址"), new DevicePointAddressRange("HR", 65535, 1));
        Assert.Empty(DevicePointAddressRangeValidator.Validate(new[] { lastRegister }));

        var overflow = (Point("P2", "越界双字"), new DevicePointAddressRange("HR", 65535, int.MaxValue));
        var issue = Assert.Single(DevicePointAddressRangeValidator.Validate(new[] { overflow }));
        Assert.Contains("范围无效", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EqualOffsetsInDifferentAreas_AreIndependent()
    {
        var issues = DevicePointAddressRangeValidator.Validate(new[]
        {
            (Point("P1", "寄存器"), new DevicePointAddressRange("HR", 10, 1)),
            (Point("P2", "线圈"), new DevicePointAddressRange("C", 10, 1))
        });

        Assert.Empty(issues);
    }

    [Fact]
    public void DifferentDeviceGroups_AreValidatedIndependentlyByCaller()
    {
        var device1 = new[] { (Point("P1", "甲设备"), new DevicePointAddressRange("HR", 10, 1)) };
        var device2 = new[] { (Point("P2", "乙设备"), new DevicePointAddressRange("HR", 10, 1)) };

        Assert.Empty(DevicePointAddressRangeValidator.Validate(device1));
        Assert.Empty(DevicePointAddressRangeValidator.Validate(device2));
    }

    private static PointsConfig.PointEntry Point(string code, string name)
        => new() { Id = Guid.NewGuid().ToString("D"), Code = code, Name = name };
}
