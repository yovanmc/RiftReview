using RiftReview.Core.Analysis;
using RiftReview.Core.Data;
using Xunit;

public class MatchupCalculatorTests
{
    // champ 103 (K'Sante) vs an opponent; nullable scalars set explicitly per test
    private static MatchRow Row(long start, int champ, int? oppChamp, bool win,
        int deaths = 3, int? gold15 = 100, int? cs10 = 60, int? pre15 = 1) =>
        new("NA1_" + start, 420, start, 1800, "15.12", champ, "TOP", win,
            5, deaths, 7, 200, cs10, gold15, 8, oppChamp, 100, 0.6, 0.25, pre15);

    [Fact]
    public void Build_groups_by_opponent_computes_winrate_and_sorts_by_games_desc()
    {
        var games = new List<MatchRow>();
        // champ 103 vs 114 (Camille): 4 games, 1 win
        for (int i = 0; i < 4; i++) games.Add(Row(i, 103, 114, win: i == 0));
        // champ 103 vs 24 (Jax): 2 games, 2 wins
        for (int i = 4; i < 6; i++) games.Add(Row(i, 103, 24, win: true));

        var rows = MatchupCalculator.Build(games);

        Assert.Equal(2, rows.Count);
        Assert.Equal(114, rows[0].OpponentChampionId);     // 4 games first (games desc)
        Assert.Equal(4, rows[0].Games);
        Assert.Equal(0.25, rows[0].WinRate, 3);
        Assert.Equal(24, rows[1].OpponentChampionId);
        Assert.Equal(1.0, rows[1].WinRate, 3);
    }

    [Fact]
    public void Build_excludes_games_with_null_opponent()
    {
        var games = new List<MatchRow>
        {
            Row(0, 103, 114, true),
            Row(1, 103, null, true),   // no lane opponent — excluded
        };
        var rows = MatchupCalculator.Build(games);
        Assert.Single(rows);
        Assert.Equal(114, rows[0].OpponentChampionId);
        Assert.Equal(1, rows[0].Games);
    }

    [Fact]
    public void Build_averages_skip_null_scalars_and_null_when_all_missing()
    {
        var games = new List<MatchRow>
        {
            Row(0, 103, 114, true,  gold15: 100, pre15: 2),
            Row(1, 103, 114, false, gold15: null, pre15: null),   // gold/pre15 null here
            Row(2, 103, 114, true,  gold15: 300, pre15: 4),
        };
        var rows = MatchupCalculator.Build(games);
        Assert.Equal(200, rows[0].AvgGoldDiff15!.Value, 1);   // (100+300)/2, null skipped
        Assert.Equal(3, rows[0].AvgPre15!.Value, 1);          // (2+4)/2
    }

    [Fact]
    public void Build_avg_is_null_when_every_value_missing()
    {
        var games = new List<MatchRow>
        {
            Row(0, 103, 114, true,  gold15: null),
            Row(1, 103, 114, false, gold15: null),
        };
        var rows = MatchupCalculator.Build(games);
        Assert.Null(rows[0].AvgGoldDiff15);
    }

    [Fact]
    public void Build_games_list_is_newest_first()
    {
        var games = new List<MatchRow> { Row(10, 103, 114, true), Row(30, 103, 114, false), Row(20, 103, 114, true) };
        var rows = MatchupCalculator.Build(games);
        Assert.Equal(new long[] { 30, 20, 10 }, rows[0].GamesList.Select(g => g.GameStartUtc).ToArray());
    }

    [Fact]
    public void EligibleChampions_filters_by_min_games_and_sorts_desc()
    {
        var rows = new List<MatchRow>();
        for (int i = 0; i < 8; i++) rows.Add(Row(i, 103, 114, true));      // champ 103: 8 games
        for (int i = 8; i < 11; i++) rows.Add(Row(i, 24, 114, true));      // champ 24: 3 games
        var eligible = MatchupCalculator.EligibleChampions(rows, minGames: 5);
        Assert.Equal(new[] { 103 }, eligible.ToArray());                   // 24 excluded (<5)
    }
}
