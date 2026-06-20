using System;
using System.Linq;
using RiftReview.App.ViewModels;
using RiftReview.Core.Data;
using RiftReview.Core.DataDragon;
using Xunit;

public class ClimbViewModelTests
{
    private static DataDragonClient Dd() => new(new System.Net.Http.HttpClient(), System.IO.Path.GetTempPath());

    private static MatchRow R(long start, bool win, int queue = 420) =>
        new("NA1_" + start, queue, start, 1800, "15.12", 103, "MIDDLE", win,
            5, 3, 7, 200, 70, 100, 8, 145, 100, 0.6, 0.25, 1);

    [Fact]
    public void Load_builds_standing_streak_and_segments()
    {
        using var db = RiftReviewDb.Open("Data Source=:memory:");
        db.SetMeta("puuid", "ME");
        db.UpsertMatch(R(100, true), "{}", "{}");
        db.UpsertMatch(R(150, true), "{}", "{}");
        db.UpsertMatch(R(180, false), "{}", "{}");
        db.InsertLpSnapshot(new LpSnapshot(90,  "RANKED_SOLO_5x5", "GOLD", "IV", 30, 1, 0));
        db.InsertLpSnapshot(new LpSnapshot(200, "RANKED_SOLO_5x5", "GOLD", "III", 10, 2, 1));

        var vm = new ClimbViewModel(db, Dd());
        vm.Load();

        Assert.NotNull(vm.Solo);
        Assert.Equal("Gold III · 10 LP", vm.Solo!.RankText);
        Assert.True(vm.HasAnyStanding);
        Assert.Single(vm.SoloSegments);
        Assert.True(vm.HasSoloSegments);
        Assert.Equal(3, vm.RecentForm.Count);
        Assert.False(vm.IsEmpty);
    }

    [Fact]
    public void Empty_db_is_empty()
    {
        using var db = RiftReviewDb.Open("Data Source=:memory:");
        var vm = new ClimbViewModel(db, Dd());
        vm.Load();
        Assert.True(vm.IsEmpty);
        Assert.False(vm.HasAnyStanding);
    }
}
