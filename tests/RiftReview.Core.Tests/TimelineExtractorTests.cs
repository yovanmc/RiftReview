using System.Text.Json;
using RiftReview.Core.Analysis;
using RiftReview.Core.Riot.Dtos;
using Xunit;

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
