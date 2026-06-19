using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using RiftReview.Core.Analysis;
using RiftReview.Core.Configuration;
using RiftReview.Core.Data;
using RiftReview.Core.DataDragon;

namespace RiftReview.App.ViewModels;

public sealed partial class TrendsViewModel : ObservableObject
{
    private readonly RiftReviewDb      _db;
    private readonly DataDragonClient  _ddragon;
    private readonly SettingsStore     _settings;

    public TrendsViewModel(RiftReviewDb db, DataDragonClient ddragon, SettingsStore settings)
    {
        _db = db; _ddragon = ddragon; _settings = settings;
    }

    [ObservableProperty] private bool   _isPreparing;
    [ObservableProperty] private string _prepareStatus = "";

    public async Task InitializeWithBackfillAsync()
    {
        try { await _ddragon.EnsureLoadedAsync(); } catch { }
        try
        {
            if (_db.MatchIdsMissingDerivedMetrics().Count > 0)
            {
                IsPreparing = true;
                PrepareStatus = "Preparing trends data…";
                var progress = new Progress<(int done, int total)>(p =>
                    PrepareStatus = $"Preparing trends data… {p.done}/{p.total}");
                await Task.Run(() => RiftReview.Core.Sync.DerivedMetricsBackfill.Run(_db, progress));
            }
        }
        catch { /* a backfill failure must not hang the page; Load() shows whatever is already available */ }
        finally { IsPreparing = false; }
        Load();
    }

    public ObservableCollection<ChampChoice>         Champions { get; } = new();
    public ObservableCollection<MetricTrendViewModel> Metrics  { get; } = new();

    [ObservableProperty] private ChampChoice?          _selectedChampion;
    [ObservableProperty] private MetricTrendViewModel? _selectedMetric;
    [ObservableProperty] private string                _lpHeadline = "";
    [ObservableProperty] private bool                  _hasLp;
    [ObservableProperty] private bool                  _isEmpty;

    public sealed record ChampChoice(int ChampionId, string Name);

    // NOTE: the View calls InitializeWithBackfillAsync() (added in Task 11) from its Loaded handler;
    // that runs the one-time backfill, ensures Data Dragon names, then calls Load(). The Task 8 test
    // calls Load() directly (champ names fall back to placeholders when Data Dragon isn't loaded).
    public void Load()
    {
        int n       = _settings.TrendWindow;
        var ranked  = _db.AllMatches(rankedOnly: true);
        var eligible = ChampTrendCalculator.EligibleChampions(ranked, n);

        Champions.Clear();
        foreach (var id in eligible)
            Champions.Add(new ChampChoice(id, _ddragon.ChampionName(id)));
        IsEmpty = Champions.Count == 0;

        LoadLp();

        SelectedChampion = Champions.FirstOrDefault();   // triggers OnSelectedChampionChanged
        if (SelectedChampion is null) Metrics.Clear();
    }

    partial void OnSelectedChampionChanged(ChampChoice? value)
    {
        Metrics.Clear();
        if (value is null) return;
        int n     = _settings.TrendWindow;
        var games = _db.AllMatches(rankedOnly: true)
                       .Where(m => m.MyChampionId == value.ChampionId)
                       .ToList();
        var trend = ChampTrendCalculator.Build(games, n);
        foreach (var m in trend.Metrics)
            Metrics.Add(new MetricTrendViewModel(m));
        SelectedMetric = Metrics.FirstOrDefault();
    }

    private void LoadLp()
    {
        var snaps = _db.GetLpSnapshots();
        var solo  = snaps.Where(s => s.QueueType == "RANKED_SOLO_5x5").ToList();
        if (solo.Count == 0) { HasLp = false; LpHeadline = ""; return; }
        var latest = solo[^1];   // GetLpSnapshots orders oldest→newest
        HasLp      = true;
        LpHeadline = $"{Cap(latest.Tier)} {latest.Division} · {latest.LeaguePoints} LP";
    }

    private static string Cap(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..].ToLower();
}
