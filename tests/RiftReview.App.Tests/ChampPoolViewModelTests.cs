using RiftReview.App.ViewModels;
using RiftReview.Core.Data;
using RiftReview.Core.DataDragon;
using RiftReview.Core.Configuration;
using Xunit;

public class ChampPoolViewModelTests
{
    private static MatchRow M(int champ, bool win, int cs10, long start) =>
        new($"NA1_{start}", 420, start, 1800, "15.12", champ, "MIDDLE", win, 5, 2, 7, 200, cs10, 0, 7, 1, start);

    [Fact]
    public void Load_builds_rows_and_practice_cards()
    {
        using var db = RiftReviewDb.Open("Data Source=:memory:");
        long s = 0;
        for (int i = 0; i < 5; i++) db.UpsertMatch(M(103, i % 2 == 0, 80, ++s), "{}", "{}");
        for (int i = 0; i < 3; i++) db.UpsertMatch(M(157, true, 70, ++s), "{}", "{}");
        var dd = new DataDragonClient(new HttpClient(), System.IO.Path.GetTempPath());  // names fall back to "Champ N" offline
        var vm = new ChampPoolViewModel(db, dd, new SettingsStore(db));

        vm.Load();

        Assert.Equal(2, vm.AllChampions.Count);
        Assert.Contains(vm.Practicing, c => c.ChampionId == 103);
        Assert.Equal(5, vm.AllChampions.First(c => c.ChampionId == 103).Games);
    }
}
