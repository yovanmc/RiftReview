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
}
