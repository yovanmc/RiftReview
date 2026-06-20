using System.Reflection;
using System.Text.Json;

namespace RiftReview.Core.Data;

public static class RankBaselineLoader
{
    private static RankBaselineTable? _cached;

    public static RankBaselineTable Load()
    {
        if (_cached is not null) return _cached;

        var asm = typeof(RankBaselineLoader).Assembly;
        var resName = asm.GetManifestResourceNames()
            .Single(n => n.EndsWith("rank-baselines.json", System.StringComparison.Ordinal));
        using var stream = asm.GetManifestResourceStream(resName)!;
        using var doc = JsonDocument.Parse(stream);

        var root = doc.RootElement;
        var m = root.GetProperty("meta");
        var meta = new RankBaselineMeta(
            m.GetProperty("source").GetString() ?? "",
            m.GetProperty("patch").GetString() ?? "",
            m.GetProperty("approximate").GetBoolean());

        var cells = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>>>();
        foreach (var role in root.GetProperty("cells").EnumerateObject())
        {
            var byTier = new Dictionary<string, IReadOnlyDictionary<string, double>>();
            foreach (var tier in role.Value.EnumerateObject())
            {
                var byMetric = new Dictionary<string, double>();
                foreach (var metric in tier.Value.EnumerateObject())
                    byMetric[metric.Name] = metric.Value.GetDouble();
                byTier[tier.Name] = byMetric;
            }
            cells[role.Name] = byTier;
        }
        _cached = new RankBaselineTable(meta, cells);
        return _cached;
    }
}
