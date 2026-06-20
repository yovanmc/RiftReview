using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RiftReview.App.Services;
using RiftReview.App.Views;
using RiftReview.Core.Analysis;
using RiftReview.Core.Data;
using RiftReview.Core.DataDragon;

namespace RiftReview.App.ViewModels;

public sealed partial class MatchupsViewModel : ObservableObject
{
    private readonly RiftReviewDb _db;
    private readonly DataDragonClient _ddragon;
    private readonly MainViewModel _main;
    private readonly NavigationService _nav;

    // Matchups are always ranked-only (locked design decision), so no SettingsStore dependency.
    public MatchupsViewModel(RiftReviewDb db, DataDragonClient ddragon, MainViewModel main, NavigationService nav)
    {
        _db = db; _ddragon = ddragon; _main = main; _nav = nav;
    }

    public ObservableCollection<ChampChoice> Champions { get; } = new();
    public ObservableCollection<MatchupRowViewModel> Opponents { get; } = new();

    [ObservableProperty] private ChampChoice? _selectedChampion;
    [ObservableProperty] private MatchupRowViewModel? _selectedOpponent;
    [ObservableProperty] private int _minGamesFilter = 1;
    [ObservableProperty] private bool _isEmpty;

    public sealed record ChampChoice(int ChampionId, string Name);

    public async Task InitializeAsync()
    {
        try { await _ddragon.EnsureLoadedAsync(); } catch { /* names fall back to placeholders */ }
        Load();
    }

    public void Load()
    {
        var ranked = _db.AllMatches(rankedOnly: true);
        var eligible = MatchupCalculator.EligibleChampions(ranked);   // >= 5 ranked games
        Champions.Clear();
        foreach (var id in eligible) Champions.Add(new ChampChoice(id, _ddragon.ChampionName(id)));
        IsEmpty = Champions.Count == 0;
        SelectedChampion = Champions.FirstOrDefault();   // triggers OnSelectedChampionChanged
        if (SelectedChampion is null) Opponents.Clear();
    }

    partial void OnSelectedChampionChanged(ChampChoice? value) => RebuildOpponents();
    partial void OnMinGamesFilterChanged(int value) => RebuildOpponents();

    private void RebuildOpponents()
    {
        Opponents.Clear();
        if (SelectedChampion is null) return;
        var games = _db.AllMatches(rankedOnly: true).Where(m => m.MyChampionId == SelectedChampion.ChampionId).ToList();
        foreach (var mr in MatchupCalculator.Build(games).Where(r => r.Games >= MinGamesFilter))
            Opponents.Add(new MatchupRowViewModel(mr, _ddragon.ChampionName(mr.OpponentChampionId)));
        SelectedOpponent = Opponents.FirstOrDefault();
    }

    [RelayCommand]
    private void OpenDeepDive(string matchId)
    {
        _main.ShowMatch(matchId);
        _nav.NavigateTo(typeof(ReviewView));
    }
}
