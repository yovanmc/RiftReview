using RiftReview.Core.Analysis;
using RiftReview.Core.Data;
using Xunit;

public class ChampTrendCalculatorTests
{
    // helper: build a ranked K'Sante row with a given win + cs10 + deaths, ordered by gameStart
    private static MatchRow Row(long start, bool win, int cs10, int deaths) =>
        new("NA1_" + start, 420, start, 1800, "15.12", 103, "MIDDLE", win,
            5, deaths, 5, 200, cs10, 100, 8, 145, 100, 0.6, 0.25, 1);

    // 20 games (N=10): a prior block of 10 and a recent block of 10.
    // cs10 60 -> 75 is improving, deaths 6 -> 3 is improving (lower is better),
    // and cs10 70 -> 71 is steady because +1 is inside the deadband floor of 2.
    [Theory]
    [InlineData("cs10", 60, 75, 3, 3, TrendVerdict.Improving, 60.0, 75.0)]
    [InlineData("deaths", 70, 70, 6, 3, TrendVerdict.Improving, 6.0, 3.0)]
    [InlineData("cs10", 70, 71, 3, 3, TrendVerdict.Steady, 70.0, 71.0)]
    public void Verdict_compares_the_recent_block_with_the_prior_block(
        string key, int priorCs10, int recentCs10, int priorDeaths, int recentDeaths,
        TrendVerdict verdict, double prior, double current)
    {
        var games = new List<MatchRow>();
        for (int i = 0; i < 10; i++) games.Add(Row(i, true, priorCs10, priorDeaths));      // oldest (prior block)
        for (int i = 10; i < 20; i++) games.Add(Row(i, true, recentCs10, recentDeaths));   // newest (current block)
        games.Reverse();                                                                    // calculator expects newest-first
        var t = ChampTrendCalculator.Build(games, n: 10);
        var metric = t.Metrics.Single(m => m.Key == key);
        Assert.Equal(verdict, metric.Verdict);
        Assert.Equal(current, metric.Current!.Value, 1);
        Assert.Equal(prior, metric.Prior!.Value, 1);
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
