using System;
using System.Linq;
using RiftReview.App.ViewModels;
using RiftReview.Core.Data;
using RiftReview.Core.DataDragon;
using Xunit;

public class SessionHealthViewModelTests
{
    private static DataDragonClient Dd() => new(new System.Net.Http.HttpClient(), System.IO.Path.GetTempPath());

    private static MatchRow G(long start, bool win, int deaths = 3) =>
        new("NA1_" + start, 420, start, 1800, "15.12", 103, "MIDDLE", win,
            5, deaths, 7, 200, 70, 100, 8, 145, 100, 0.6, 0.25, 1);

    [Fact]
    public void Builds_sessions_and_banner_when_latest_is_tilted()
    {
        using var db = RiftReviewDb.Open("Data Source=:memory:");
        db.SetMeta("puuid", "ME");
        db.UpsertMatch(G(0, true), "{}", "{}");
        db.UpsertMatch(G(2400, false), "{}", "{}");
        db.UpsertMatch(G(4800, false), "{}", "{}");
        db.UpsertMatch(G(7200, false), "{}", "{}");
        db.UpsertMatch(G(9600, false), "{}", "{}");

        var vm = new SessionHealthViewModel(db, Dd());

        Assert.False(vm.IsEmpty);
        Assert.NotNull(vm.Latest);
        Assert.True(vm.BannerVisible);
        Assert.Equal(RiftReview.Core.Analysis.TiltSeverity.Tilted, vm.BannerSeverity);
        Assert.True(vm.Latest!.IsTilted);
    }

    [Fact]
    public void Calm_latest_hides_banner()
    {
        using var db = RiftReviewDb.Open("Data Source=:memory:");
        db.SetMeta("puuid", "ME");
        db.UpsertMatch(G(0, true), "{}", "{}");
        db.UpsertMatch(G(2400, true), "{}", "{}");
        db.UpsertMatch(G(4800, true), "{}", "{}");

        var vm = new SessionHealthViewModel(db, Dd());
        Assert.False(vm.BannerVisible);
        Assert.True(vm.Latest!.IsCalm);
    }

    [Fact]
    public void Empty_db_is_empty_no_banner()
    {
        using var db = RiftReviewDb.Open("Data Source=:memory:");
        var vm = new SessionHealthViewModel(db, Dd());
        Assert.True(vm.IsEmpty);
        Assert.False(vm.BannerVisible);
        Assert.Null(vm.Latest);
    }
}
