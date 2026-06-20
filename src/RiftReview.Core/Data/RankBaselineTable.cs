namespace RiftReview.Core.Data;

public sealed record RankBaselineMeta(string Source, string Patch, bool Approximate);

public sealed record RankBaselineTable(
    RankBaselineMeta Meta,
    // role -> tier -> metricKey -> value
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>>> Cells);
