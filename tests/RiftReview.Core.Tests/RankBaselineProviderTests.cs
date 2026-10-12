using System.Collections.Generic;
using RiftReview.Core.Analysis;
using RiftReview.Core.Data;
using Xunit;

public class RankBaselineProviderTests
{
    private static RankBaselineTable Table() => new(
        new RankBaselineMeta("seed", "16.12", true),
        new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>>>
        {
            ["MID"]     = new Dictionary<string, IReadOnlyDictionary<string, double>>
                          { ["GOLD"] = new Dictionary<string, double> { ["cs10"] = 60.0 } },
            ["SUPPORT"] = new Dictionary<string, IReadOnlyDictionary<string, double>>
                          { ["GOLD"] = new Dictionary<string, double>() }, // CS intentionally absent
        });

    [Theory]
    [InlineData("MIDDLE", "MID")]
    [InlineData("BOTTOM", "ADC")]
    [InlineData("UTILITY", "SUPPORT")]
    [InlineData("TOP", "TOP")]
    [InlineData("JUNGLE", "JUNGLE")]
    public void CanonicalRole_maps_riot_positions(string raw, string expected)
        => Assert.Equal(expected, RankBaselineProvider.CanonicalRole(raw));

    [Theory]
    [InlineData("GOLD", "GOLD")]
    [InlineData("MASTER", "DIAMOND")]
    [InlineData("GRANDMASTER", "DIAMOND")]
    [InlineData("CHALLENGER", "DIAMOND")]
    public void NormalizeTier_collapses_apex(string raw, string expected)
        => Assert.Equal(expected, RankBaselineProvider.NormalizeTier(raw));

    // A seeded value resolves. An absent metric or an unranked tier resolves to null.
    [Theory]
    [InlineData("MIDDLE", "GOLD", 60.0)]
    [InlineData("UTILITY", "GOLD", null)]
    [InlineData("MIDDLE", "UNRANKED", null)]
    public void Resolve_returns_the_seeded_value_or_null(string role, string tier, double? expected) =>
        Assert.Equal(expected, RankBaselineProvider.Resolve(Table(), role, tier, "cs10"));
}
