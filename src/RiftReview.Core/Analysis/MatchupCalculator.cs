using RiftReview.Core.Data;

namespace RiftReview.Core.Analysis;

// Pure: group a champ's ranked games by lane opponent, compute laning+combat aggregates.
// No DB, no I/O, deterministic. Mirrors ChampTrendCalculator.
public static class MatchupCalculator
{
    public static IReadOnlyList<int> EligibleChampions(IReadOnlyList<MatchRow> rankedMatches, int minGames = 5) =>
        rankedMatches.GroupBy(m => m.MyChampionId)
            .Where(g => g.Count() >= minGames)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key).ToList();

    public static IReadOnlyList<MatchupRow> Build(IReadOnlyList<MatchRow> champGames) =>
        champGames
            .Where(m => m.OpponentChampionId.HasValue)   // no lane opponent → cannot attribute a matchup
            .GroupBy(m => m.OpponentChampionId!.Value)
            .Select(g =>
            {
                var games = g.OrderByDescending(m => m.GameStartUtc).ToList();   // newest first
                int n = games.Count;
                int wins = games.Count(m => m.Win);
                return new MatchupRow(
                    OpponentChampionId: g.Key,
                    Games: n,
                    Wins: wins,
                    WinRate: wins / (double)n,
                    AvgGoldDiff15: AvgNullable(games, m => m.GoldDiffAt15),
                    AvgCs10: AvgNullable(games, m => m.CsAt10),
                    AvgDeaths: games.Average(m => m.Deaths),
                    AvgKda: games.Average(m => (m.Kills + m.Assists) / (double)Math.Max(1, m.Deaths)),
                    AvgPre15: AvgNullable(games, m => m.DeathsPre15),
                    GamesList: games);
            })
            .OrderByDescending(r => r.Games)
            .ToList();

    // Mean over games whose value is non-null; null if none have a value.
    private static double? AvgNullable(IReadOnlyList<MatchRow> games, Func<MatchRow, int?> sel)
    {
        var vals = games.Where(m => sel(m).HasValue).Select(m => (double)sel(m)!.Value).ToList();
        return vals.Count == 0 ? null : vals.Average();
    }
}
