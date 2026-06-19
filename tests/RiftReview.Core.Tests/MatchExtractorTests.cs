using System.Text.Json;
using RiftReview.Core.Analysis;
using RiftReview.Core.Riot.Dtos;
using Xunit;

public class MatchExtractorTests
{
    private static MatchDto Match() => JsonSerializer.Deserialize<MatchDto>(
        FixtureLoader.Read("sample_match.json"), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    [Fact]
    public void Summarize_finds_me_and_lane_opponent()
    {
        var s = MatchExtractor.Summarize(Match(), myPuuid: "ME");
        Assert.Equal(3, s.MyParticipantId);
        Assert.Equal("MIDDLE", s.MyTeamPosition);
        Assert.Equal(8, s.OpponentParticipantId);
        Assert.True(s.Win);
        Assert.Equal(210, s.Cs); // totalMinionsKilled + neutralMinionsKilled from fixture
    }

    [Fact]
    public void Summarize_returns_no_opponent_when_position_empty()
    {
        var me = new ParticipantDto("ME", 1, 103, 100, "", true, 5, 5, 5, 100, 20);
        var enemy = new ParticipantDto("E1", 6, 110, 200, "", false, 3, 4, 2, 90, 10);
        var dto = new MatchDto(
            new MatchMetadata("NA1_ARAM", new List<string> { "ME", "E1" }),
            new MatchInfo(450, 1_700_000_000_000, 1500, "15.12.1", new List<ParticipantDto> { me, enemy }));

        var s = MatchExtractor.Summarize(dto, "ME");

        Assert.Null(s.OpponentParticipantId);
        Assert.Null(s.OpponentChampionId);
        Assert.Equal(120, s.Cs); // 100 + 20
    }
}
