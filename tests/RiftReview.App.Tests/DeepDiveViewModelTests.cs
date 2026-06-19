using System.Text.Json;
using RiftReview.App.ViewModels;
using RiftReview.Core.Data;
using RiftReview.Core.Riot.Dtos;

public class DeepDiveViewModelTests
{
    [Fact]
    public void Load_builds_curves_deaths_and_header_from_stored_blobs()
    {
        using var db = RiftReviewDb.Open("Data Source=:memory:");
        db.SetMeta("puuid", "ME");

        var match = new MatchDto(
            new MatchMetadata("M1", new List<string> { "ME", "E" }),
            new MatchInfo(420, 1_700_000_000_000, 1800, "15.12.1", new List<ParticipantDto>
            {
                new("ME", 3, 103, 100, "MIDDLE", true, 5, 1, 3, 100, 0),
                new("E",  8, 157, 200, "MIDDLE", false, 1, 5, 1, 90, 0),
            }));
        var tl = new TimelineDto(
            new TimelineMetadata("M1", new List<string> { "ME", "E" }),
            new TimelineInfo(60000, new List<FrameDto>
            {
                new(0,      Frames(500, 480),  new List<EventDto>()),
                new(60000,  Frames(1200, 1100), new List<EventDto> { new("CHAMPION_KILL", 60000, 8, 3) }),
                new(120000, Frames(2000, 1700), new List<EventDto>()),
            }));
        var opts = new JsonSerializerOptions();
        var row = new MatchRow("M1", 420, 1_700_000_000, 1800, "15.12", 103, "MIDDLE", true,
            5, 1, 3, 100, null, null, 8, 157, 1);
        db.UpsertMatch(row, JsonSerializer.Serialize(match, opts), JsonSerializer.Serialize(tl, opts));

        var vm = new DeepDiveViewModel(db);
        vm.Load(row);

        Assert.True(vm.HasData);
        Assert.True(vm.HasLaneOpponent);
        Assert.Equal(3, vm.GoldVsLane.Count);
        Assert.Equal(300, vm.GoldVsLane[2].Value);     // 2000 - 1700 at minute 2
        Assert.Equal(new[] { 1.0 }, vm.DeathMinutes.ToArray());
        Assert.Empty(vm.CsBaseline);                    // only 1 game → no baseline
        Assert.Contains("MIDDLE", vm.Header);
        Assert.Contains("Win", vm.Header);
    }

    private static Dictionary<string, ParticipantFrameDto> Frames(int meGold, int oppGold) => new()
    {
        ["3"] = new ParticipantFrameDto(3, meGold, 10, 0),
        ["8"] = new ParticipantFrameDto(8, oppGold, 8, 0),
    };
}
