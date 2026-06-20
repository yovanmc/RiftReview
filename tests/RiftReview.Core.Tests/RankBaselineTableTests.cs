using System.Collections.Generic;
using RiftReview.Core.Data;
using Xunit;

public class RankBaselineTableTests
{
    [Fact]
    public void Table_exposes_meta_and_nested_cells()
    {
        var cells = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>>>
        {
            ["MID"] = new Dictionary<string, IReadOnlyDictionary<string, double>>
            {
                ["GOLD"] = new Dictionary<string, double> { ["cs10"] = 60.0 }
            }
        };
        var table = new RankBaselineTable(new RankBaselineMeta("src", "16.12", true), cells);

        Assert.True(table.Meta.Approximate);
        Assert.Equal(60.0, table.Cells["MID"]["GOLD"]["cs10"]);
    }
}
