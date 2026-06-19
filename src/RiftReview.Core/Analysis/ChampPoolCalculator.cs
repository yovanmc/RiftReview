using RiftReview.Core.Data;

namespace RiftReview.Core.Analysis;

public static class ChampPoolCalculator
{
    public static ChampPool Build(IReadOnlyList<MatchRow> matches,
        int recentWindow = 15, int maxPracticing = 3, int minPracticeGames = 2)
    {
        // matches arrive newest-first (AllMatches order). Group by champion.
        var stats = matches
            .GroupBy(m => m.MyChampionId)
            .Select(g =>
            {
                var games = g.ToList();
                int wins = games.Count(m => m.Win);
                int sumK = games.Sum(m => m.Kills), sumD = games.Sum(m => m.Deaths), sumA = games.Sum(m => m.Assists);
                var cs10 = games.Where(m => m.CsAt10 is not null).Select(m => m.CsAt10!.Value).ToList();
                var trend = games.OrderBy(m => m.GameStartUtc).Select(m => m.CsAt10).ToList();   // oldest→newest
                var dominantRole = games
                    .Where(m => !string.IsNullOrEmpty(m.MyTeamPosition))
                    .GroupBy(m => m.MyTeamPosition)
                    .OrderByDescending(x => x.Count())
                    .Select(x => x.Key)
                    .FirstOrDefault() ?? "";
                return new ChampStat(
                    g.Key, games.Count, wins, games.Count - wins,
                    games.Count == 0 ? 0 : (double)wins / games.Count,
                    (sumK + sumA) / (double)Math.Max(1, sumD),
                    cs10.Count == 0 ? null : cs10.Average(),
                    games.Average(m => (double)m.Deaths),
                    trend,
                    dominantRole);
            })
            .OrderByDescending(c => c.Games).ThenByDescending(c => c.WinRate)
            .ToList();

        // Practicing = top champs by games within the most-recent `recentWindow` games.
        var recent = matches.Take(recentWindow);
        var practicing = recent
            .GroupBy(m => m.MyChampionId)
            .Select(g => (Champ: g.Key, Count: g.Count()))
            .Where(x => x.Count >= minPracticeGames)
            .OrderByDescending(x => x.Count)
            .Take(maxPracticing)
            .Select(x => x.Champ)
            .ToList();

        return new ChampPool(stats, practicing);
    }
}
