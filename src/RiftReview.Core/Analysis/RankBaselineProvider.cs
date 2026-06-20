using RiftReview.Core.Data;

namespace RiftReview.Core.Analysis;

public static class RankBaselineProvider
{
    private static readonly string[] NonApexHighToLow =
        { "DIAMOND", "EMERALD", "PLATINUM", "GOLD", "SILVER", "BRONZE", "IRON" };

    public static string CanonicalRole(string teamPosition) => (teamPosition ?? "").ToUpperInvariant() switch
    {
        "MIDDLE" or "MID" => "MID",
        "BOTTOM" or "BOT" or "ADC" => "ADC",
        "UTILITY" or "SUPPORT" or "SUPP" => "SUPPORT",
        "TOP" => "TOP",
        "JUNGLE" or "JNG" or "JUNG" => "JUNGLE",
        var other => other,
    };

    public static string NormalizeTier(string tier) => (tier ?? "").ToUpperInvariant() switch
    {
        "MASTER" or "GRANDMASTER" or "CHALLENGER" => "DIAMOND",
        var t => t,
    };

    public static double? Resolve(RankBaselineTable table, string teamPosition, string tier, string metricKey)
    {
        var role = CanonicalRole(teamPosition);
        var normTier = NormalizeTier(tier);
        if (table.Cells.TryGetValue(role, out var byTier)
            && byTier.TryGetValue(normTier, out var byMetric)
            && byMetric.TryGetValue(metricKey, out var v))
            return v;
        return null;
    }
}
