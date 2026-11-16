using XXX.TestBench.Core.Common;
using Xunit;

namespace XXX.TestBench.Core.Tests;

public sealed class StableIdentityTests
{
    [Fact]
    public void FromSeed_PreservesEstablishedGuidAlgorithm()
    {
        Assert.Equal(
            "4320dc17-65df-2756-be02-7951c9e87c4b",
            StableIdentity.FromSeed("stable-identity-regression-seed"));
    }
}
