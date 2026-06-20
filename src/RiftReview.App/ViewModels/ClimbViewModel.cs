using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using RiftReview.Core.Analysis;
using RiftReview.Core.Data;
using RiftReview.Core.DataDragon;

namespace RiftReview.App.ViewModels;

public sealed class StandingViewModel
{
    public StandingViewModel(string queueLabel, LpSnapshot s)
    {
        QueueLabel = queueLabel;
        RankText = RankLadder.Format(s.Tier, s.Division, s.LeaguePoints);
        int total = s.Wins + s.Losses;
        SeasonRecord = $"{s.Wins}W / {s.Losses}L";
        WinRateText = total > 0 ? $"{Math.Round(100.0 * s.Wins / total)}%" : "—";
    }
    public string QueueLabel { get; }
    public string RankText { get; }
    public string SeasonRecord { get; }
    public string WinRateText { get; }
}

public sealed class FormPip { public bool Win { get; init; } }

public sealed class LpSegmentViewModel
{
    public LpSegmentViewModel(LpSegment s)
    {
        WhenRange = $"{Local(s.FromUtc)} → {Local(s.ToUtc)}";
        GamesLabel = s.GamesInWindow == 1 ? "1 game" : $"{s.GamesInWindow} games";
        NetLpText = $"{(s.NetLp >= 0 ? "+" : "−")}{Math.Abs(s.NetLp)} LP";
        IsGain = s.NetLp >= 0;
        RankRange = $"{s.FromLabel} → {s.ToLabel}";
    }
    private static string Local(long utc) => DateTimeOffset.FromUnixTimeSeconds(utc).LocalDateTime.ToString("MMM d");
    public string WhenRange { get; }
    public string GamesLabel { get; }
    public string NetLpText { get; }
    public bool IsGain { get; }
    public string RankRange { get; }
}

public sealed partial class ClimbViewModel : ObservableObject
{
    private readonly RiftReviewDb _db;
    private readonly DataDragonClient _ddragon;

    public ClimbViewModel(RiftReviewDb db, DataDragonClient ddragon)
    {
        _db = db; _ddragon = ddragon;
    }

    [ObservableProperty] private StandingViewModel? _solo;
    [ObservableProperty] private StandingViewModel? _flex;
    [ObservableProperty] private bool _hasAnyStanding;

    [ObservableProperty] private string _streakText = "";
    [ObservableProperty] private bool _streakIsPositive;
    [ObservableProperty] private string _longestStreaksText = "";

    public ObservableCollection<FormPip> RecentForm { get; } = new();
    public ObservableCollection<LpSegmentViewModel> SoloSegments { get; } = new();
    [ObservableProperty] private bool _hasSoloSegments;
    [ObservableProperty] private string _lpHistoryNote = "";
    [ObservableProperty] private bool _isEmpty;

    public async Task InitializeAsync()
    {
        try { await _ddragon.EnsureLoadedAsync(); } catch { /* offline — names not needed here */ }
        Load();
    }

    public void Load()
    {
        var snaps = _db.GetLpSnapshots();
        var ranked = _db.AllMatches(rankedOnly: true);

        var soloSnap = snaps.Where(s => s.QueueType == "RANKED_SOLO_5x5").OrderByDescending(s => s.TakenUtc).FirstOrDefault();
        var flexSnap = snaps.Where(s => s.QueueType == "RANKED_FLEX_SR").OrderByDescending(s => s.TakenUtc).FirstOrDefault();
        Solo = soloSnap is null ? null : new StandingViewModel("Ranked Solo/Duo", soloSnap);
        Flex = flexSnap is null ? null : new StandingViewModel("Ranked Flex", flexSnap);
        HasAnyStanding = Solo is not null || Flex is not null;

        var streak = ClimbCalculator.Streaks(ranked);
        StreakIsPositive = streak.CurrentStreak > 0;
        StreakText = streak.CurrentStreak switch
        {
            0   => ranked.Count == 0 ? "No ranked games yet" : "No active streak",
            > 0 => $"On a {streak.CurrentStreak}-win streak",
            _   => $"{-streak.CurrentStreak}-loss skid",
        };
        LongestStreaksText = $"Best: {streak.LongestWinStreak}W · Worst: {streak.LongestLossStreak}L";
        RecentForm.Clear();
        foreach (var w in streak.RecentForm) RecentForm.Add(new FormPip { Win = w });

        SoloSegments.Clear();
        foreach (var seg in ClimbCalculator.Segments(snaps, ranked, "RANKED_SOLO_5x5"))
            SoloSegments.Add(new LpSegmentViewModel(seg));
        HasSoloSegments = SoloSegments.Count > 0;
        LpHistoryNote = "LP history fills in as you sync — Riot doesn't expose past per-game LP.";

        IsEmpty = snaps.Count == 0 && ranked.Count == 0;
    }
}
