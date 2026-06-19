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

public class MatchExtractorDerivedTests
{
    private static ParticipantDto P(int pid, int team, int k, int a, int dmg, string puuid) =>
        new(puuid, pid, 100 + pid, team, "MIDDLE", team == 100, k, 2, a, 100, 0, dmg);

    private static MatchDto Match()
    {
        var parts = new List<ParticipantDto>
        {
            P(1,100, 5,3, 1000, "ME"),   // me: kills 5, assists 3, dmg 1000
            P(2,100, 3,1, 500,  "B"),
            P(3,100, 2,0, 500,  "C"),    // team100 kills total = 5+3+2 = 10; dmg = 1000+500+500 = 2000
            P(6,200, 4,4, 800,  "X"),
            P(7,200, 1,1, 700,  "Y"),
        };
        return new MatchDto(new MatchMetadata("NA1_1", parts.Select(p => p.Puuid).ToList()),
            new MatchInfo(420, 1_700_000_000_000, 1800, "15.12.1", parts));
    }

    [Fact]
    public void Computes_kill_participation_and_damage_share()
    {
        var s = MatchExtractor.Summarize(Match(), "ME");
        // KP = (5 + 3) / teamKills(10) = 0.8
        Assert.Equal(0.8, s.KillParticipation, 3);
        // damage share = 1000 / teamDmg(2000) = 0.5
        Assert.Equal(0.5, s.DamageShare, 3);
    }

    [Fact]
    public void Guards_divide_by_zero_when_team_has_no_kills_or_damage()
    {
        var parts = new List<ParticipantDto>
        {
            new("ME", 1, 101, 100, "MIDDLE", true, 0, 0, 0, 0, 0, 0),
            new("B",  2, 102, 100, "MIDDLE", true, 0, 0, 0, 0, 0, 0),
        };
        var m = new MatchDto(new MatchMetadata("NA1_2", new List<string> { "ME", "B" }),
            new MatchInfo(420, 1, 1800, "15.12.1", parts));
        var s = MatchExtractor.Summarize(m, "ME");
        Assert.Equal(0.0, s.KillParticipation, 3);
        Assert.Equal(0.0, s.DamageShare, 3);
    }
}
