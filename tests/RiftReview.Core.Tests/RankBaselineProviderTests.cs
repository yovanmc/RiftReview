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

    [Fact]
    public void Resolve_returns_seeded_value() =>
        Assert.Equal(60.0, RankBaselineProvider.Resolve(Table(), "MIDDLE", "GOLD", "cs10"));

    [Fact]
    public void Resolve_returns_null_for_absent_metric() =>
        Assert.Null(RankBaselineProvider.Resolve(Table(), "UTILITY", "GOLD", "cs10"));

    [Fact]
    public void Resolve_returns_null_for_unranked_tier() =>
        Assert.Null(RankBaselineProvider.Resolve(Table(), "MIDDLE", "UNRANKED", "cs10"));
}
