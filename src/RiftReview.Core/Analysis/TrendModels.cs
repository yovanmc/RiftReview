namespace RiftReview.Core.Analysis;

public enum TrendVerdict { Improving, Steady, Declining, Building }

public sealed record MetricTrend(
    string Key, string DisplayName, string Unit, int Direction,
    double? Current, double? Prior, double ImprovementDelta,
    TrendVerdict Verdict, IReadOnlyList<double?> RollingSeries);

public sealed record ChampTrend(int ChampionId, int Games, IReadOnlyList<MetricTrend> Metrics);
