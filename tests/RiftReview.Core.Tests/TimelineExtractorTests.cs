using System.Text.Json;
using RiftReview.Core.Analysis;
using RiftReview.Core.Riot.Dtos;
using Xunit;

public class TimelineExtractorPre15Tests
{
    [Fact]
    public void Counts_only_my_deaths_before_the_minute_mark()
    {
        var frames = new List<FrameDto>
        {
            new(0, new(), new List<EventDto>
            {
                new("CHAMPION_KILL", 5*60000L, 8, 3),   // me (pid 3) dies at 5:00  -> counts
                new("CHAMPION_KILL", 9*60000L, 8, 7),   // someone else dies        -> no
            }),
            new(60000, new(), new List<EventDto>
            {
                new("CHAMPION_KILL", 14*60000L, 8, 3),  // me dies at 14:00         -> counts
                new("CHAMPION_KILL", 16*60000L, 8, 3),  // me dies at 16:00         -> after 15, no
            }),
        };
        var tl = new TimelineDto(new TimelineMetadata("NA1_1", new()), new TimelineInfo(60000, frames));
        Assert.Equal(2, TimelineExtractor.DeathsBeforeMinute(tl, myParticipantId: 3, minute: 15));
    }
}

public class TimelineExtractorVisionTests
{
    private static TimelineDto VoTl() => JsonSerializer.Deserialize<TimelineDto>(
        FixtureLoader.Read("vision_objectives_timeline.json"),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    [Fact]
    public void Vision_counts_my_wards_placed_cleared_and_control()
    {
        var r = TimelineExtractor.BuildVisionObjectives(VoTl(), myParticipantId: 3, myTeamId: 100);
        Assert.Equal(2, r.Vision.WardsPlaced);
        Assert.Equal(1, r.Vision.WardsCleared);
        Assert.Equal(1, r.Vision.ControlWardsPlaced);
        Assert.Equal(4, r.Vision.VisionProxy);
    }
}

public class TimelineExtractorTests
{
    private static TimelineDto Tl() => JsonSerializer.Deserialize<TimelineDto>(
        FixtureLoader.Read("sample_timeline.json"), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    [Fact]
    public void DeepDive_builds_two_gold_lines_and_deaths()
    {
        var dd = TimelineExtractor.BuildDeepDive(Tl(), myParticipantId: 3, opponentParticipantId: 8);
        Assert.True(dd.HasLaneOpponent);
        Assert.Equal(412, dd.GoldDiffVsLane.Single(p => p.Minute == 15).Value);
        Assert.Equal(new[] { 14.0, 21.0 }, dd.DeathMinutes.ToArray());
        Assert.All(dd.CsPerMinute, p => Assert.True(p.Value >= 0));
    }

    [Fact]
    public void CsAt10_and_GoldDiffAt15_pick_nearest_frames()
    {
        Assert.Equal(75, TimelineExtractor.CsAtMinute(Tl(), participantId: 3, minute: 10));
        Assert.Equal(412, TimelineExtractor.GoldDiffAtMinute(Tl(), 3, 8, minute: 15));
    }

    [Fact]
    public void No_opponent_returns_team_line_only()
    {
        var dd = TimelineExtractor.BuildDeepDive(Tl(), myParticipantId: 3, opponentParticipantId: null);
        Assert.False(dd.HasLaneOpponent);
        Assert.Empty(dd.GoldDiffVsLane);
        Assert.NotEmpty(dd.GoldDiffVsTeam);
    }
}
