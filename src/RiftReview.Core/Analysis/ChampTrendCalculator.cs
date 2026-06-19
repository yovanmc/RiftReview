using RiftReview.Core.Data;

namespace RiftReview.Core.Analysis;

public static class ChampTrendCalculator
{
    private sealed record Def(string Key, string Name, string Unit, int Dir, double Floor, Func<MatchRow, double?> Sel);

    // Floors are the dead-band half-width in each metric's own units.
    private static readonly Def[] Defs =
    {
        new("winRate", "Win rate",            "%", +1, 0.05, m => m.Win ? 1.0 : 0.0),
        new("cs10",    "CS @ 10",             "",  +1, 2.0,  m => m.CsAt10),
        new("gold15",  "Gold @ 15 (vs lane)", "g", +1, 75.0, m => m.GoldDiffAt15),
        new("kda",     "KDA",                 "",  +1, 0.3,  m => (m.Kills + m.Assists) / (double)Math.Max(1, m.Deaths)),
        new("deaths",  "Deaths / game",       "",  -1, 0.4,  m => m.Deaths),
        new("pre15",   "Pre-15 deaths",       "",  -1, 0.3,  m => m.DeathsPre15.HasValue ? m.DeathsPre15.Value : (double?)null),
        new("kp",      "Kill participation",  "%", +1, 0.03, m => m.KillParticipation),
        new("dmg",     "Damage share",        "%", +1, 0.02, m => m.DamageShare),
    };

    public static IReadOnlyList<int> EligibleChampions(IReadOnlyList<MatchRow> rankedMatches, int n) =>
        rankedMatches.GroupBy(m => m.MyChampionId)
            .Where(g => g.Count() >= 2 * n)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key).ToList();

    public static ChampTrend Build(IReadOnlyList<MatchRow> champGames, int n)
    {
        var games = champGames.OrderBy(m => m.GameStartUtc).ToList(); // oldest -> newest
        int k = games.Count;
        var metrics = new List<MetricTrend>(Defs.Length);
        foreach (var d in Defs)
        {
            var vals = games.Select(d.Sel).ToList();
            var rolling = Rolling(vals, n);
            double? current = Block(vals, k - n, n);
            double? prior   = Block(vals, k - 2 * n, n);
            double delta = (current.HasValue && prior.HasValue) ? d.Dir * (current.Value - prior.Value) : 0.0;
            metrics.Add(new MetricTrend(d.Key, d.Name, d.Unit, d.Dir, current, prior, delta,
                Classify(current, prior, k, n, delta, d.Floor), rolling));
        }
        return new ChampTrend(k > 0 ? games[^1].MyChampionId : 0, k, metrics);
    }

    private static TrendVerdict Classify(double? cur, double? prior, int k, int n, double delta, double floor)
    {
        if (cur is null || prior is null || k < 2 * n) return TrendVerdict.Building;
        if (delta > floor) return TrendVerdict.Improving;
        if (delta < -floor) return TrendVerdict.Declining;
        return TrendVerdict.Steady;
    }

    // Average of non-null values in [start, start+count); null if window starts before 0 or holds no values.
    private static double? Block(IReadOnlyList<double?> vals, int start, int count)
    {
        if (start < 0) return null;
        double sum = 0; int n = 0;
        for (int i = start; i < start + count && i < vals.Count; i++)
            if (vals[i].HasValue) { sum += vals[i]!.Value; n++; }
        return n == 0 ? null : sum / n;
    }

    private static IReadOnlyList<double?> Rolling(IReadOnlyList<double?> vals, int n)
    {
        var outp = new List<double?>(vals.Count);
        for (int i = 0; i < vals.Count; i++)
        {
            double sum = 0; int c = 0;
            for (int j = Math.Max(0, i - n + 1); j <= i; j++)
                if (vals[j].HasValue) { sum += vals[j]!.Value; c++; }
            outp.Add(c == 0 ? (double?)null : sum / c);
        }
        return outp;
    }
}
