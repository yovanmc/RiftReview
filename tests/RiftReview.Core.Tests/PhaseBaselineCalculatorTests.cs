using System.Collections.Generic;
using System.Linq;
using RiftReview.Core.Analysis;
using Xunit;

namespace RiftReview.Core.Tests;

public class PhaseBaselineCalculatorTests
{
    private static PhaseStat S(string label, double gold, double cs, int deaths, double? kp) =>
        new(label, 0, 10, gold, cs, deaths, 0, 0, 0, kp is null ? 0 : 1, kp);

    [Fact]
    public void Averages_each_metric_across_games_that_reached_the_phase()
    {
        var games = new List<IReadOnlyList<PhaseStat>>
        {
            new[] { S("Early", 100, 8, 1, 0.5) },
            new[] { S("Early", 200, 6, 3, 0.7) },
            new[] { S("Early", 300, 7, 2, 0.6) },
        };
        var b = PhaseBaselineCalculator.Average(games);
        var early = b.Single(x => x.Label == "Early");
        Assert.Equal(200, early.GoldDiffDelta);
        Assert.Equal(7, early.CsPerMinute);
        Assert.Equal(2, early.Deaths);
        Assert.Equal(0.6, early.KillParticipation!.Value, 3);
    }

    [Fact]
    public void Metric_with_too_few_samples_is_null_while_siblings_populate()
    {
        var games = new List<IReadOnlyList<PhaseStat>>
        {
            new[] { S("Early", 100, 8, 1, 0.5) },
            new[] { S("Early", 200, 6, 3, null) }, // KP missing
            new[] { S("Early", 300, 7, 2, null) }, // KP missing
        };
        var early = PhaseBaselineCalculator.Average(games).Single(x => x.Label == "Early");
        Assert.NotNull(early.GoldDiffDelta);                 // 3 samples
        Assert.Null(early.KillParticipation);                // only 1 sample (<3)
    }

    [Fact]
    public void Phase_reached_by_fewer_than_min_games_is_all_null()
    {
        var games = new List<IReadOnlyList<PhaseStat>>
        {
            new[] { S("Early", 100, 8, 1, 0.5), S("Late", 50, 5, 0, 0.4) },
            new[] { S("Early", 200, 6, 3, 0.7), S("Late", 60, 4, 1, 0.5) },
            new[] { S("Early", 300, 7, 2, 0.6) }, // no Late
        };
        var bl = PhaseBaselineCalculator.Average(games);
        Assert.NotNull(bl.Single(x => x.Label == "Early").GoldDiffDelta);   // 3
        var late = bl.Single(x => x.Label == "Late");                        // only 2
        Assert.Null(late.GoldDiffDelta);
        Assert.Null(late.CsPerMinute);
        Assert.Null(late.Deaths);
        Assert.Null(late.KillParticipation);
    }

    [Fact]
    public void Always_returns_the_three_phase_labels()
    {
        var b = PhaseBaselineCalculator.Average(new List<IReadOnlyList<PhaseStat>>());
        Assert.Equal(new[] { "Early", "Mid", "Late" }, b.Select(x => x.Label));
    }
}
