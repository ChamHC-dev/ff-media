using FFMedia.Tools.VideoMerger.Services;
using Xunit;

namespace FFMedia.Tests.VideoMerger;

public class DiskSpaceGuardTests
{
    [Fact]
    public void SafetyMargin_Is20Percent()
    {
        Assert.Equal(1.2, DiskSpaceGuard.SafetyMargin);
    }

    [Fact]
    public void Evaluate_PassesWithAmpleSpace()
    {
        var result = DiskSpaceGuard.Evaluate(freeBytes: 10_000, requiredBytes: 1_000);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Evaluate_AppliesA20PercentMargin()
    {
        // 1000 required -> 1200 needed. 1199 fails, 1200 passes.
        Assert.False(DiskSpaceGuard.Evaluate(1_199, 1_000).IsSuccess);
        Assert.True(DiskSpaceGuard.Evaluate(1_200, 1_000).IsSuccess);
    }

    [Fact]
    public void Evaluate_RoundsTheMarginUp_SoItIsNeverUnderApplied()
    {
        // 1 required -> 1.2 needed -> 2 bytes, not 1.
        Assert.False(DiskSpaceGuard.Evaluate(1, 1).IsSuccess);
        Assert.True(DiskSpaceGuard.Evaluate(2, 1).IsSuccess);
    }

    [Fact]
    public void Evaluate_ExplainsTheShortfallInHumanUnits()
    {
        var result = DiskSpaceGuard.Evaluate(freeBytes: 0, requiredBytes: 5_000_000_000);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            "Not enough disk space for the merge's temporary files: 5.59 GB needed "
            + "(estimate 4.66 GB + 20% margin), only 0 B free. Free up 5.59 GB and try again.",
            result.Error);
    }

    [Fact]
    public void Evaluate_ReportsTheShortfall_NotTheWholeRequirement()
    {
        // 100 MB estimate -> 114.44 MB needed; 14.44 MB already free -> free up the remaining 100 MB.
        var result = DiskSpaceGuard.Evaluate(freeBytes: 15_141_069, requiredBytes: 100_000_000);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            "Not enough disk space for the merge's temporary files: 114.44 MB needed "
            + "(estimate 95.37 MB + 20% margin), only 14.44 MB free. Free up 100 MB and try again.",
            result.Error);
    }

    [Fact]
    public void Evaluate_ZeroRequirementAlwaysPasses()
    {
        Assert.True(DiskSpaceGuard.Evaluate(0, 0).IsSuccess);
    }

    [Fact]
    public void Evaluate_TreatsNegativeInputsAsZero()
    {
        // A negative "required" is nonsense (nothing to reserve); a negative "free" is no space at all.
        Assert.True(DiskSpaceGuard.Evaluate(freeBytes: 0, requiredBytes: -1).IsSuccess);
        Assert.False(DiskSpaceGuard.Evaluate(freeBytes: -1, requiredBytes: 1_000).IsSuccess);
    }

    [Fact]
    public void Evaluate_FailsInsteadOfOverflowing_OnAnAbsurdRequirement()
    {
        // long.MaxValue * 1.2 in double, cast back to long, wraps to long.MinValue — which would
        // make an impossible requirement silently "pass".
        var result = DiskSpaceGuard.Evaluate(freeBytes: long.MaxValue / 2, requiredBytes: long.MaxValue);

        Assert.False(result.IsSuccess);
    }
}
