using System;
using System.Linq;
using Microsoft.Extensions.Options;
using RiftReview.App.Services;
using RiftReview.App.ViewModels;
using RiftReview.Core.Configuration;
using RiftReview.Core.Data;
using RiftReview.Core.DataDragon;
using Xunit;

public class MatchupsViewModelTests
{
    private static DataDragonClient Dd() => new(new System.Net.Http.HttpClient(), System.IO.Path.GetTempPath());

    private static MainViewModel MakeMain()
    {
        var empty = RiftReviewDb.Open("Data Source=:memory:");   // no matches -> ctor never loads a deep-dive
        return new MainViewModel(empty, null!, null!, Dd(), Options.Create(new RiotOptions()), new SettingsStore(empty));
    }

    private static MatchRow Row(long start, int champ, int oppChamp, bool win) =>
        new("NA1_" + start, 420, start, 1800, "15.12", champ, "TOP", win, 5, 3, 7, 200, 60, 100, 8, oppChamp, 100, 0.6, 0.25, 1);

    private static MatchupsViewModel Make(RiftReviewDb db, NavigationService nav) =>
        new(db, Dd(), MakeMain(), nav);

    [Fact]
    public void Lists_eligible_champ_and_builds_opponent_rows()
    {
        using var db = RiftReviewDb.Open("Data Source=:memory:");
        db.SetMeta("puuid", "ME");
        // champ 103: 5 games vs 114, 3 vs 24  -> eligible (8 >= 5); both opponents appear
        for (int i = 0; i < 5; i++) db.UpsertMatch(Row(i, 103, 114, i == 0), "{}", "{}");
        for (int i = 5; i < 8; i++) db.UpsertMatch(Row(i, 103, 24, true), "{}", "{}");

        var vm = Make(db, new NavigationService());
        vm.Load();

        Assert.Contains(vm.Champions, c => c.ChampionId == 103);
        Assert.Equal(2, vm.Opponents.Count);            // faced 114 (5g) and 24 (3g)
        Assert.Equal(5, vm.Opponents[0].GamesCount);    // 114 first (games desc)
    }

    [Fact]
    public void Min_games_filter_hides_rows_below_it()
    {
        using var db = RiftReviewDb.Open("Data Source=:memory:");
        db.SetMeta("puuid", "ME");
        for (int i = 0; i < 6; i++) db.UpsertMatch(Row(i, 103, 114, true), "{}", "{}");   // 6 games vs 114
        for (int i = 6; i < 8; i++) db.UpsertMatch(Row(i, 103, 24, true), "{}", "{}");     // 2 games vs 24

        var vm = Make(db, new NavigationService());
        vm.Load();
        Assert.Equal(2, vm.Opponents.Count);          // default filter 1 -> both shown
        vm.MinGamesFilter = 3;                         // hide the 2-game matchup
        Assert.Single(vm.Opponents);
        Assert.Equal(6, vm.Opponents[0].GamesCount);
    }

    [Fact]
    public void Open_deep_dive_command_requests_review_navigation()
    {
        using var db = RiftReviewDb.Open("Data Source=:memory:");
        db.SetMeta("puuid", "ME");
        for (int i = 0; i < 5; i++) db.UpsertMatch(Row(i, 103, 114, true), "{}", "{}");
        var nav = new NavigationService();
        Type? navigatedTo = null;
        nav.NavigationRequested += t => navigatedTo = t;

        var vm = Make(db, nav);
        vm.Load();
        vm.OpenDeepDiveCommand.Execute("NA1_0");

        Assert.Equal(typeof(RiftReview.App.Views.ReviewView), navigatedTo);
    }
}
