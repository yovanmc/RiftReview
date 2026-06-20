namespace RiftReview.Core.Analysis;

public static class BuildAnalyzer
{
    /// <summary>
    /// Aggregate a champ's per-game builds into its best build: per completed item, how many of the
    /// champ's games contained it (once per game) and the win rate of those games. Top <paramref
    /// name="topN"/> by Games, then WinRate. Pure. Caller passes only this champ+role's matches.
    /// </summary>
    public static ChampionBestBuild Analyze(
        int championId, string role, IReadOnlyList<MatchBuild> matches, int topN = 6)
    {
        var games = new Dictionary<int, int>();
        var wins  = new Dictionary<int, int>();
        foreach (var m in matches)
            foreach (var id in m.CompletedItems.Distinct())   // defensive: already deduped upstream
            {
                games[id] = games.GetValueOrDefault(id) + 1;
                if (m.Win) wins[id] = wins.GetValueOrDefault(id) + 1;
            }

        var items = games.Keys
            .Select(id =>
            {
                int g = games[id], w = wins.GetValueOrDefault(id);
                return new BuildItemStat(id, g, w, g == 0 ? 0 : (double)w / g);
            })
            .OrderByDescending(s => s.Games)
            .ThenByDescending(s => s.WinRate)
            .ThenBy(s => s.ItemId)              // stable tie-break for deterministic tests
            .Take(topN)
            .ToList();

        return new ChampionBestBuild(championId, role, matches.Count, items);
    }
}
