using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using RiftReview.Core.Analysis;
using RiftReview.Core.Configuration;
using RiftReview.Core.Data;
using RiftReview.Core.DataDragon;

namespace RiftReview.App.ViewModels;

public enum CompareMode { Rank, Own }

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

    // Compare mode & baseline state
    [ObservableProperty] private CompareMode _compareMode = CompareMode.Rank;
    [ObservableProperty] private string      _compareTier = "GOLD";
    [ObservableProperty] private bool        _rankSelectorVisible;
    [ObservableProperty] private string      _baselineProvenance = "";

    public IReadOnlyList<string> Tiers { get; } =
        new[] { "IRON", "BRONZE", "SILVER", "GOLD", "PLATINUM", "EMERALD", "DIAMOND", "MASTER", "GRANDMASTER", "CHALLENGER" };

    // Convenience bools for XAML segmented toggle (RadioButton.IsChecked two-way binds to these)
    public bool IsRankMode { get => CompareMode == CompareMode.Rank; set { if (value) CompareMode = CompareMode.Rank; } }
    public bool IsOwnMode  { get => CompareMode == CompareMode.Own;  set { if (value) CompareMode = CompareMode.Own;  } }

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

    public ObservableCollection<ChampChoice>          Champions { get; } = new();
    public ObservableCollection<MetricTrendViewModel> Metrics   { get; } = new();

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
        // Load baseline table and set provenance once
        var baseTable = RankBaselineLoader.Load();
        BaselineProvenance = $"baseline: {baseTable.Meta.Source} · patch {baseTable.Meta.Patch}" +
                             (baseTable.Meta.Approximate ? " · approximate" : "");

        int n       = _settings.TrendWindow;
        var ranked  = _db.AllMatches(rankedOnly: true);
        var eligible = ChampTrendCalculator.EligibleChampions(ranked, n);

        Champions.Clear();
        foreach (var id in eligible)
            Champions.Add(new ChampChoice(id, _ddragon.ChampionName(id)));
        IsEmpty = Champions.Count == 0;

        LoadLp();   // sets CompareTier before champion is selected

        SelectedChampion = Champions.FirstOrDefault();   // triggers OnSelectedChampionChanged
        if (SelectedChampion is null) Metrics.Clear();
    }

    partial void OnSelectedChampionChanged(ChampChoice? value) => RebuildSelectedMetrics();

    partial void OnCompareModeChanged(CompareMode value)
    {
        OnPropertyChanged(nameof(IsRankMode));
        OnPropertyChanged(nameof(IsOwnMode));
        RebuildSelectedMetrics();
    }

    partial void OnCompareTierChanged(string value) => RebuildSelectedMetrics();

    private void RebuildSelectedMetrics()
    {
        Metrics.Clear();
        if (SelectedChampion is null) return;

        int n     = _settings.TrendWindow;
        var games = _db.AllMatches(rankedOnly: true)
                       .Where(m => m.MyChampionId == SelectedChampion.ChampionId)
                       .ToList();
        var trend = ChampTrendCalculator.Build(games, n);

        var table = RankBaselineLoader.Load();

        // Compute champion's dominant role from all ranked games for this champ
        string champRole = _db.AllMatches(rankedOnly: true)
            .Where(m => m.MyChampionId == SelectedChampion.ChampionId)
            .GroupBy(m => m.MyTeamPosition)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault() ?? "";

        foreach (var mt in trend.Metrics)
        {
            var metricVm = new MetricTrendViewModel(mt);

            double? rankBaseline = RankBaselineProvider.Resolve(table, champRole, CompareTier, mt.Key);
            var nonNull = mt.RollingSeries.Where(v => v.HasValue).Select(v => v!.Value).ToList();
            double? ownBaseline = nonNull.Count > 0 ? nonNull.Average() : (double?)null;

            double? active = CompareMode == CompareMode.Rank ? rankBaseline : ownBaseline;
            if (active is double baseVal && mt.Current is double cur)
            {
                var d = BaselineDelta.Compute(cur, baseVal, mt.Direction, mt.Unit);
                metricVm.HasBaseline     = true;
                metricVm.BaselineValue   = baseVal;
                metricVm.BaselineLabel   = CompareMode == CompareMode.Rank ? $"{Cap(CompareTier)} avg" : "Your avg";
                metricVm.DeltaVsBaseline = d.Text;
                metricVm.BaselineIsGood  = d.IsGood;
                metricVm.BaselineIsBad   = d.IsBad;
            }
            else
            {
                metricVm.HasBaseline = false; // absent rank cell → no fabricated number
            }

            Metrics.Add(metricVm);
        }

        SelectedMetric = Metrics.FirstOrDefault();
    }

    private void LoadLp()
    {
        var snaps = _db.GetLpSnapshots();
        var solo  = snaps.Where(s => s.QueueType == "RANKED_SOLO_5x5").ToList();
        if (solo.Count == 0)
        {
            HasLp = false;
            LpHeadline = "";
            CompareMode = CompareMode.Own;
            RankSelectorVisible = false;
            return;
        }
        var latest = solo[^1];   // GetLpSnapshots orders oldest→newest
        HasLp      = true;
        LpHeadline = RankLadder.Format(latest.Tier, latest.Division, latest.LeaguePoints);
        CompareTier         = latest.Tier;
        RankSelectorVisible = true;
    }

    private static string Cap(string tier) =>
        string.IsNullOrEmpty(tier) ? "" : char.ToUpperInvariant(tier[0]) + tier[1..].ToLowerInvariant();
}
