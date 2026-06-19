using RiftReview.Core.Analysis;
using RiftReview.Core.Data;
using Xunit;

public class ChampPoolCalculatorTests
{
    private static MatchRow M(int champ, bool win, int k, int d, int a, int? cs10, long start) =>
        new($"NA1_{start}", 420, start, 1800, "15.12", champ, "MIDDLE", win, k, d, a, 200, cs10, 0, 7, 1, start);

    [Fact]
    public void Aggregates_per_champion()
    {
        var rows = new List<MatchRow>
        {
            M(103, true,  5, 2, 10, 80, 300),
            M(103, false, 3, 4, 6,  60, 200),
            M(157, true,  9, 1, 4,  90, 100),
        };
        var pool = ChampPoolCalculator.Build(rows);
        var ahri = pool.All.Single(c => c.ChampionId == 103);
        Assert.Equal(2, ahri.Games);
        Assert.Equal(1, ahri.Wins);
        Assert.Equal(0.5, ahri.WinRate, 3);
        Assert.Equal((5 + 3 + 10 + 6) / 6.0, ahri.Kda, 3);   // (K+A)/D = 24/6 = 4.0
        Assert.Equal(70.0, ahri.AvgCs10!.Value, 3);          // (80+60)/2
        Assert.Equal(3.0, ahri.AvgDeaths, 3);                // (2+4)/2
    }

    [Fact]
    public void Kda_with_zero_deaths_uses_one_as_divisor()
    {
        var pool = ChampPoolCalculator.Build(new List<MatchRow> { M(99, true, 4, 0, 6, 70, 100) });
        Assert.Equal(10.0, pool.All.Single().Kda, 3);        // (4+6)/max(1,0)
    }

    [Fact]
    public void Trend_is_chronological_oldest_to_newest()
    {
        var rows = new List<MatchRow> { M(103, true, 1, 1, 1, 90, 300), M(103, true, 1, 1, 1, 70, 100) };
        var trend = ChampPoolCalculator.Build(rows).All.Single().TrendCs10;
        Assert.Equal(new int?[] { 70, 90 }, trend.ToArray());  // start=100 first, start=300 last
    }

    [Fact]
    public void Practicing_is_top_champs_in_recent_window_with_min_games()
    {
        var rows = new List<MatchRow>();
        long s = 0;
        for (int i = 0; i < 6; i++) rows.Add(M(103, true, 1, 1, 1, 80, ++s));  // K'Sante x6
        for (int i = 0; i < 4; i++) rows.Add(M(157, true, 1, 1, 1, 80, ++s));  // Galio x4
        rows.Add(M(7, true, 1, 1, 1, 80, ++s));                                // LeBlanc x1 (below min)
        var pool = ChampPoolCalculator.Build(rows, recentWindow: 15, maxPracticing: 3, minPracticeGames: 2);
        Assert.Equal(new[] { 103, 157 }, pool.PracticingChampionIds.ToArray());
        Assert.DoesNotContain(7, pool.PracticingChampionIds);
    }

    [Fact]
    public void Empty_input_yields_empty_pool()
    {
        var pool = ChampPoolCalculator.Build(new List<MatchRow>());
        Assert.Empty(pool.All);
        Assert.Empty(pool.PracticingChampionIds);
    }
}
