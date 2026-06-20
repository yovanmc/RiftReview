using System.Text.Json;
using RiftReview.Core.Analysis;
using RiftReview.Core.Riot.Dtos;
using Xunit;

namespace RiftReview.Core.Tests;

public class TimelineExtractorCausalityTests
{
    // Mirror the loader idiom used by TimelineExtractorTests (adjust if that file differs).
    private static TimelineDto Tl() => JsonSerializer.Deserialize<TimelineDto>(
        FixtureLoader.Read("causality_timeline.json"),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    private static readonly JsonSerializerOptions Ci = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public void Swing_finds_largest_3min_window_and_sign()
    {
        var c = TimelineExtractor.BuildCausality(Tl(), myParticipantId: 1);
        Assert.NotNull(c.Swing);
        Assert.Equal(5, c.Swing!.StartMinute);
        Assert.Equal(8, c.Swing.EndMinute);
        Assert.Equal(500,  c.Swing.StartGold);
        Assert.Equal(2500, c.Swing.EndGold);
        Assert.Equal(2000, c.Swing.Delta);
        Assert.True(c.Swing.Favorable);
    }

    [Fact]
    public void Death_context_reports_team_gold_diff_at_each_death()
    {
        var c = TimelineExtractor.BuildCausality(Tl(), myParticipantId: 1);
        Assert.Equal(2, c.Deaths.Count);
        Assert.Equal(2, c.Deaths[0].Minute);
        Assert.Equal(200, c.Deaths[0].GoldDiff);
        Assert.Equal(7, c.Deaths[1].Minute);
        Assert.Equal(1700, c.Deaths[1].GoldDiff);
    }

    [Fact]
    public void Backs_cluster_purchases_by_gap()
    {
        var c = TimelineExtractor.BuildCausality(Tl(), myParticipantId: 1);
        Assert.Equal(3, c.Backs.Count);
        Assert.Equal((1.0, 3), (c.Backs[0].Minute, c.Backs[0].ItemCount));
        Assert.Equal((4.0, 2), (c.Backs[1].Minute, c.Backs[1].ItemCount));
        Assert.Equal((8.0, 1), (c.Backs[2].Minute, c.Backs[2].ItemCount));
    }

    [Fact]
    public void Turning_point_lag_is_from_preceding_back()
    {
        var c = TimelineExtractor.BuildCausality(Tl(), myParticipantId: 1);
        Assert.Equal(1.0, c.TurningPointLagMinutes); // swing start 5 − back at 4
    }

    [Fact]
    public void Flat_game_has_no_decisive_swing()
    {
        var tl = Build(new[] { (0, 1000, 1000), (1, 1000, 1000), (2, 1000, 1000), (3, 1000, 1000) });
        var c = TimelineExtractor.BuildCausality(tl, myParticipantId: 1);
        Assert.Null(c.Swing);
    }

    [Fact]
    public void Single_frame_has_no_swing()
    {
        var tl = Build(new[] { (0, 1000, 800) });
        var c = TimelineExtractor.BuildCausality(tl, myParticipantId: 1);
        Assert.Null(c.Swing);
    }

    [Fact]
    public void No_purchases_yields_no_backs_and_null_lag()
    {
        var tl = Build(new[] { (0, 500, 500), (1, 900, 600), (2, 1400, 700), (3, 2000, 700) });
        var c = TimelineExtractor.BuildCausality(tl, myParticipantId: 1);
        Assert.Empty(c.Backs);
        Assert.Null(c.TurningPointLagMinutes);
    }

    // Build a minimal timeline: each tuple = (minute, myGold p1/team100, enemyGold p6/team200).
    private static TimelineDto Build((int min, int mine, int enemy)[] pts)
    {
        var frames = pts.Select(p => new FrameDto(
            p.min * 60000L,
            new Dictionary<string, ParticipantFrameDto>
            {
                ["1"] = new(1, p.mine, 0, 0),
                ["6"] = new(6, p.enemy, 0, 0),
            },
            new List<EventDto>())).ToList();
        return new TimelineDto(new TimelineMetadata("T", new()), new TimelineInfo(60000, frames));
    }
}
