namespace RiftReview.Core.Analysis;

public static class BaselineCalculator
{
    public static IReadOnlyList<ChartPoint> Average(IEnumerable<IReadOnlyList<ChartPoint>> games, int minGames = 3)
    {
        var list = games.ToList();
        if (list.Count < minGames) return Array.Empty<ChartPoint>();
        var byMinute = new Dictionary<double, (double sum, int n)>();
        foreach (var g in list)
            foreach (var p in g)
            { var cur = byMinute.GetValueOrDefault(p.Minute); byMinute[p.Minute] = (cur.sum + p.Value, cur.n + 1); }
        return byMinute.OrderBy(kv => kv.Key)
            .Select(kv => new ChartPoint(kv.Key, kv.Value.sum / kv.Value.n)).ToList();
    }
}
