using System.Linq;
using RiftReview.App.ViewModels;
using RiftReview.Core.Configuration;
using RiftReview.Core.Data;
using RiftReview.Core.DataDragon;
using Xunit;

public class TrendsViewModelTests
{
    private static MatchRow Row(long start, int champ, bool win, int cs10) =>
        new("NA1_" + start, 420, start, 1800, "15.12", champ, "MIDDLE", win,
            5, 3, 5, 200, cs10, 100, 8, 145, 100, 0.6, 0.25, 1);

    [Fact]
    public void Lists_eligible_champ_and_builds_metric_rows()
    {
        using var db = RiftReviewDb.Open("Data Source=:memory:");
        db.SetMeta("puuid", "ME");
        for (int i = 0; i < 10; i++) db.UpsertMatch(Row(i, 103, true, 60), "{}", "{}");
        for (int i = 10; i < 20; i++) db.UpsertMatch(Row(i, 103, true, 75), "{}", "{}");

        var vm = new TrendsViewModel(db, new DataDragonClient(new System.Net.Http.HttpClient(), System.IO.Path.GetTempPath()), new SettingsStore(db));
        vm.Load();

        Assert.Contains(vm.Champions, c => c.ChampionId == 103);
        Assert.NotEmpty(vm.Metrics);
        Assert.Contains(vm.Metrics, m => m.Key == "cs10" && m.Verdict == "Improving");
    }
}
