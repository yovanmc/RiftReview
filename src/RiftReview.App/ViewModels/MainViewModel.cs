using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using RiftReview.Core.Configuration;
using RiftReview.Core.Data;
using RiftReview.Core.DataDragon;
using RiftReview.Core.Riot;
using RiftReview.Core.Sync;

namespace RiftReview.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly RiftReviewDb _db;
    private readonly SyncService _sync;
    private readonly IRiotApiClient _client;
    private readonly DataDragonClient _ddragon;
    private readonly RiotOptions _options;

    public MainViewModel(RiftReviewDb db, SyncService sync, IRiotApiClient client,
        DataDragonClient ddragon, IOptions<RiotOptions> options)
    {
        _db = db;
        _sync = sync;
        _client = client;
        _ddragon = ddragon;
        _options = options.Value;
        DeepDive = new DeepDiveViewModel(_db, _ddragon);
        TrendStrip = new TrendStripViewModel();
        Reload();
    }

    public DeepDiveViewModel DeepDive { get; }
    public TrendStripViewModel TrendStrip { get; }
    public ObservableCollection<MatchListItemViewModel> Matches { get; } = new();

    [ObservableProperty] private MatchListItemViewModel? _selectedMatch;
    [ObservableProperty] private bool _rankedOnly;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "Press Sync to pull your recent matches.";
    [ObservableProperty] private bool _isError;

    public bool IsEmpty => Matches.Count == 0;

    // NOTE: the View calls InitializeAsync() from its Loaded handler so Data Dragon names load and the list refreshes.
    public async Task InitializeAsync()
    {
        try { await _ddragon.EnsureLoadedAsync(); } catch { /* names fall back to placeholders offline */ }
        Reload();
    }

    [RelayCommand]
    private async Task SyncAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        IsError = false;
        StatusMessage = "Syncing…";
        try
        {
            try { await _ddragon.EnsureLoadedAsync(); } catch { }
            await AccountResolver.EnsurePuuidAsync(_db, _client, _options.RiotId);
            var progress = new Progress<SyncProgress>(p => StatusMessage = $"Syncing… {p.Fetched}/{p.Total}");
            var res = await _sync.SyncAsync(20, progress);
            IsError = res.Error is not null;
            StatusMessage = res.Error ?? $"Synced: {res.NewMatches} new, {res.Skipped} already stored.";
            Reload();
        }
        catch (Exception ex)
        {
            IsError = true;
            StatusMessage = ex.Message;
        }
        finally { IsBusy = false; }
    }

    partial void OnRankedOnlyChanged(bool value) => Reload();

    partial void OnSelectedMatchChanged(MatchListItemViewModel? value)
    {
        if (value is not null) DeepDive.Load(value.Row);
    }

    private void Reload()
    {
        var rows = _db.RecentMatches(RankedOnly, 20);
        Matches.Clear();
        foreach (var r in rows)
            Matches.Add(new MatchListItemViewModel(r, _ddragon.ChampionName(r.MyChampionId)));
        TrendStrip.Load(rows);
        SelectedMatch = Matches.FirstOrDefault();
        OnPropertyChanged(nameof(IsEmpty));
    }
}
