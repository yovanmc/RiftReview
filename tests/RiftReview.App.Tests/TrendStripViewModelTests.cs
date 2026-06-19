using RiftReview.App.ViewModels;
using RiftReview.Core.Data;

public class TrendStripViewModelTests
{
    [Fact]
    public void Builds_four_series_in_chronological_order()
    {
        var rows = new List<MatchRow>
        {
            Row("NA1_2", start: 200, win: false, deaths: 7, cs10: 60, g15: -100),
            Row("NA1_1", start: 100, win: true,  deaths: 3, cs10: 70, g15: 300),
        };
        var vm = new TrendStripViewModel();
        vm.Load(rows);
        Assert.Equal(new[] { true, false }, vm.Wins.ToArray());   // oldest→newest
        Assert.Equal(new[] { 3, 7 }, vm.Deaths.ToArray());
        Assert.Equal(new[] { 70, 60 }, vm.CsAt10.ToArray());
        Assert.Equal(new[] { 300, -100 }, vm.GoldAt15.ToArray());
    }

    private static MatchRow Row(string id, long start, bool win, int deaths, int cs10, int g15) =>
        new(id, 420, start, 1800, "15.12", 103, "MIDDLE", win, 1, deaths, 1, 100, cs10, g15, 7, 1, start);
}
