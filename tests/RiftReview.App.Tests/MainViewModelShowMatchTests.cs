using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RiftReview.App.ViewModels;
using RiftReview.Core.Configuration;
using RiftReview.Core.Data;
using RiftReview.Core.DataDragon;
using RiftReview.Core.Riot.Dtos;
using Xunit;

public class MainViewModelShowMatchTests
{
    private static MainViewModel Make(RiftReviewDb db)
    {
        var dd = new DataDragonClient(new System.Net.Http.HttpClient(), System.IO.Path.GetTempPath());
        // sync + client are stored but never touched by the ctor or ShowMatch -> null! is safe here.
        return new MainViewModel(db, null!, null!, dd, Options.Create(new RiotOptions()), new SettingsStore(db));
    }

    private static MatchRow Row(string id, long start, int oppChamp) =>
        new(id, 420, start, 1800, "15.12", 103, "TOP", true, 5, 3, 7, 200, 60, 100, 8, oppChamp, 100, 0.6, 0.25, 1);

    private static void Seed(RiftReviewDb db, string id, long start, int oppChamp)
    {
        var parts = new List<ParticipantDto>
        {
            new("ME", 3, 103, 100, "TOP", true, 5, 3, 7, 200, 10, 1000),
            new("OPP", 8, oppChamp, 200, "TOP", false, 3, 5, 4, 180, 0, 800),
        };
        var match = new MatchDto(new MatchMetadata(id, parts.Select(p => p.Puuid).ToList()),
            new MatchInfo(420, 1_700_000_000_000, 1800, "15.12.1", parts));
        var tl = new TimelineDto(new TimelineMetadata(id, new()), new TimelineInfo(60000, new List<FrameDto>()));
        db.UpsertMatch(Row(id, start, oppChamp), JsonSerializer.Serialize(match), JsonSerializer.Serialize(tl));
    }

    [Fact]
    public void ShowMatch_selects_an_in_list_match()
    {
        using var db = RiftReviewDb.Open("Data Source=:memory:");
        db.SetMeta("puuid", "ME");
        Seed(db, "NA1_1", 5, 114);
        Seed(db, "NA1_2", 6, 24);
        var vm = Make(db);                       // ctor selects newest (NA1_2)

        vm.ShowMatch("NA1_1");

        Assert.Equal("NA1_1", vm.SelectedMatch!.MatchId);
        Assert.True(vm.DeepDive.HasData);
    }

    [Fact]
    public void ShowMatch_unknown_id_is_a_noop()
    {
        using var db = RiftReviewDb.Open("Data Source=:memory:");
        db.SetMeta("puuid", "ME");
        Seed(db, "NA1_1", 5, 114);
        var vm = Make(db);
        var before = vm.SelectedMatch;

        vm.ShowMatch("NOPE");                    // must not throw

        Assert.Equal(before, vm.SelectedMatch);  // unchanged
    }
}
