using RiftReview.Core.Data;
using Xunit;

public class RankBaselineLoaderTests
{
    [Fact]
    public void Loads_embedded_seed_with_gold_mid_cs10()
    {
        var table = RankBaselineLoader.Load();
        Assert.True(table.Meta.Approximate);
        Assert.Equal(60.0, table.Cells["MID"]["GOLD"]["cs10"]);
        Assert.False(table.Cells["SUPPORT"]["GOLD"].ContainsKey("cs10"));
    }
}
