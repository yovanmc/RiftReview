using RiftReview.Core.Analysis;
using RiftReview.Core.Data;
using Xunit;

public class ChampTrendCalculatorTests
{
    // helper: build a ranked K'Sante row with a given win + cs10 + deaths, ordered by gameStart
    private static MatchRow Row(long start, bool win, int cs10, int deaths) =>
        new("NA1_" + start, 420, start, 1800, "15.12", 103, "MIDDLE", win,
            5, deaths, 5, 200, cs10, 100, 8, 145, 100, 0.6, 0.25, 1);

    [Fact]
    public void Improving_cs10_when_recent_block_beats_prior_block()
    {
        // 20 games (N=10): prior 10 average cs10 = 60, recent 10 average = 75 -> improving
        var games = new List<MatchRow>();
        for (int i = 0; i < 10; i++) games.Add(Row(i, true, 60, 3));        // oldest (prior block)
        for (int i = 10; i < 20; i++) games.Add(Row(i, true, 75, 3));       // newest (current block)
        games.Reverse();                                                    // calculator expects newest-first
        var t = ChampTrendCalculator.Build(games, n: 10);
        var cs = t.Metrics.Single(m => m.Key == "cs10");
        Assert.Equal(TrendVerdict.Improving, cs.Verdict);
        Assert.Equal(75, cs.Current!.Value, 1);
        Assert.Equal(60, cs.Prior!.Value, 1);
    }

    [Fact]
    public void Deaths_down_reads_as_improving()
    {
        var games = new List<MatchRow>();
        for (int i = 0; i < 10; i++) games.Add(Row(i, true, 70, 6));   // prior: 6 deaths
        for (int i = 10; i < 20; i++) games.Add(Row(i, true, 70, 3));  // recent: 3 deaths (better)
        games.Reverse();
        var t = ChampTrendCalculator.Build(games, n: 10);
        Assert.Equal(TrendVerdict.Improving, t.Metrics.Single(m => m.Key == "deaths").Verdict);
    }

    [Fact]
    public void Building_when_fewer_than_two_windows()
    {
        var games = new List<MatchRow>();
        for (int i = 0; i < 12; i++) games.Add(Row(i, true, 70, 3)); // 12 < 2N(20)
        games.Reverse();
        var t = ChampTrendCalculator.Build(games, n: 10);
        Assert.Equal(TrendVerdict.Building, t.Metrics.Single(m => m.Key == "cs10").Verdict);
    }

    [Fact]
    public void Steady_within_deadband()
    {
        var games = new List<MatchRow>();
        for (int i = 0; i < 10; i++) games.Add(Row(i, true, 70, 3));
        for (int i = 10; i < 20; i++) games.Add(Row(i, true, 71, 3)); // +1 cs < floor(2) -> steady
        games.Reverse();
        var t = ChampTrendCalculator.Build(games, n: 10);
        Assert.Equal(TrendVerdict.Steady, t.Metrics.Single(m => m.Key == "cs10").Verdict);
    }

    [Fact]
    public void Eligible_champions_need_two_windows()
    {
        var rows = new List<MatchRow>();
        for (int i = 0; i < 25; i++) rows.Add(Row(i, true, 70, 3));              // champ 103: 25 games
        for (int i = 25; i < 30; i++) rows.Add(new MatchRow("NA1_" + i, 420, i, 1800, "15.12", 7, "MIDDLE", true, 1,1,1,100, 60,0,8,99,100)); // champ 7: 5 games
        var eligible = ChampTrendCalculator.EligibleChampions(rows, n: 10);
        Assert.Contains(103, eligible);
        Assert.DoesNotContain(7, eligible);
    }
}
