using XXX.TestBench.Core.Configuration;

namespace XXX.TestBench.Core.Tests;

public sealed class DevicePointTagTests
{
    [Fact]
    public void TryParse_HandlesOptionalDeviceAndGroupPrefix()
    {
        var ok = DevicePointTag.TryParse("S71500/AI.L32", out var tag, out var error);

        Assert.True(ok, error);
        Assert.Equal("S71500", tag.DeviceCode);
        Assert.Equal("AI", tag.GroupCode);
        Assert.Equal("L32", tag.PointName);
        Assert.Equal("AI.L32", tag.LocalTag);
        Assert.Equal("S71500/AI.L32", tag.FullTag);
    }

    [Fact]
    public void TryParse_AllowsUngroupedPoint()
    {
        var ok = DevicePointTag.TryParse("发动机转速", out var tag, out var error);

        Assert.True(ok, error);
        Assert.Null(tag.GroupCode);
        Assert.Equal("发动机转速", tag.PointName);
        Assert.Equal("发动机转速", tag.LocalTag);
    }

    [Theory]
    [InlineData("")]
    [InlineData("AI.")]
    [InlineData("/AI.Pressure")]
    [InlineData("S71500/AI.Pressure/Extra")]
    public void TryParse_RejectsAmbiguousTags(string value)
    {
        Assert.False(DevicePointTag.TryParse(value, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
