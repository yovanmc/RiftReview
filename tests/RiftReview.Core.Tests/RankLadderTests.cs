using RiftReview.Core.Analysis;
using Xunit;

public class RankLadderTests
{
    [Fact]
    public void ToPoints_is_monotonic_across_boundaries()
    {
        int goldIV0 = RankLadder.ToPoints("GOLD", "IV", 0);
        int goldI99 = RankLadder.ToPoints("GOLD", "I", 99);
        int platIV0 = RankLadder.ToPoints("PLATINUM", "IV", 0);
        Assert.True(goldIV0 < goldI99);
        Assert.True(goldI99 < platIV0);
    }

    [Fact]
    public void ToPoints_diamond_one_meets_master_zero()
    {
        Assert.Equal(2800, RankLadder.ToPoints("DIAMOND", "I", 100));
        Assert.Equal(2800, RankLadder.ToPoints("MASTER", "I", 0));
        Assert.Equal(3000, RankLadder.ToPoints("MASTER", "I", 200));
    }

    [Fact]
    public void Format_nonapex_and_apex()
    {
        Assert.Equal("Gold II · 47 LP", RankLadder.Format("GOLD", "II", 47));
        Assert.Equal("Master 312 LP", RankLadder.Format("MASTER", "I", 312));
    }
}
