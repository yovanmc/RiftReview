using System.Linq;
using RiftReview.App.ViewModels;
using RiftReview.Core.Analysis;
using RiftReview.Core.Data;
using Xunit;

public class MatchupRowViewModelTests
{
    private static MatchRow Game(long start, bool win, int? gold15) =>
        new("NA1_" + start, 420, start, 1800, "15.12", 103, "TOP", win,
            5, 3, 7, 200, 60, gold15, 8, 114, 100, 0.6, 0.25, 1);

    [Fact]
    public void Favorable_when_sampled_and_winrate_high()
    {
        var games = new List<MatchRow> { Game(0, true, 200), Game(1, true, 100), Game(2, true, 300), Game(3, false, -50) };
        var mr = MatchupCalculator.Build(games)[0];   // 4 games, 75%
        var vm = new MatchupRowViewModel(mr, "Camille");
        Assert.Equal("Camille", vm.OpponentName);
        Assert.Equal("75%", vm.WinPercent);
        Assert.True(vm.IsFavorable);
        Assert.False(vm.IsThin);
        Assert.Equal(4, vm.GameRows.Count);
        Assert.Equal("+138", vm.GoldDiff15);          // (200+100+300-50)/4 = 137.5 -> +138
    }

    [Fact]
    public void Thin_when_under_three_games_no_color()
    {
        var games = new List<MatchRow> { Game(0, true, 100), Game(1, false, -100) };
        var mr = MatchupCalculator.Build(games)[0];   // 2 games
        var vm = new MatchupRowViewModel(mr, "Garen");
        Assert.True(vm.IsThin);
        Assert.False(vm.IsFavorable);
        Assert.False(vm.IsUnfavorable);
    }

    [Fact]
    public void Null_gold_renders_dash()
    {
        var games = new List<MatchRow> { Game(0, true, null), Game(1, true, null), Game(2, false, null) };
        var mr = MatchupCalculator.Build(games)[0];
        var vm = new MatchupRowViewModel(mr, "Sett");
        Assert.Equal("—", vm.GoldDiff15);
    }

    [Fact]
    public void Game_row_formats_result_and_gold()
    {
        var vm = new MatchupGameViewModel(Game(1_700_000_000, false, -620));
        Assert.Equal("Loss", vm.Result);
        Assert.False(vm.Win);
        Assert.Equal("5/3/7", vm.Kda);
        Assert.Equal("-620", vm.GoldDiff15);
        Assert.Equal("NA1_1700000000", vm.MatchId);
    }
}
