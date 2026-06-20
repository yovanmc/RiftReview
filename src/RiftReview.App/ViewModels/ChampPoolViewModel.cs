using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using RiftReview.Core.Analysis;
using RiftReview.Core.Configuration;
using RiftReview.Core.Data;
using RiftReview.Core.DataDragon;
using RiftReview.Core.Riot.Dtos;

namespace RiftReview.App.ViewModels;

public sealed partial class ChampPoolViewModel : ObservableObject
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly RiftReviewDb _db;
    private readonly DataDragonClient _ddragon;
    private readonly SettingsStore _settings;

    public ChampPoolViewModel(RiftReviewDb db, DataDragonClient ddragon, SettingsStore settings)
    {
        _db = db; _ddragon = ddragon; _settings = settings;
        _rankedOnly = settings.DefaultRankedOnly;
    }

    public ObservableCollection<ChampCardViewModel> Practicing { get; } = new();
    public ObservableCollection<ChampRowViewModel> AllChampions { get; } = new();

    [ObservableProperty] private bool _rankedOnly;
    public bool HasPracticing => Practicing.Count > 0;
    public bool IsEmpty => AllChampions.Count == 0;

    public async Task InitializeAsync()
    {
        try { await _ddragon.EnsureLoadedAsync(); } catch { /* names fall back to placeholders */ }
        Load();

        // After pool + item data are both loaded, compute builds for practicing champs off-UI-thread.
        var puuid = _db.GetMeta("puuid");
        if (!string.IsNullOrEmpty(puuid))
        {
            var practicing = Practicing.ToList(); // snapshot of the observable collection
            var built = await Task.Run(() => practicing
                .Select(card => (card, vm: BuildFor(card.ChampionId, card.DominantRole, puuid)))
                .ToList());
            foreach (var (card, vm) in built)
                card.BestBuild = vm;
        }
    }

    public void Load()
    {
        var pool = ChampPoolCalculator.Build(_db.AllMatches(RankedOnly));
        AllChampions.Clear();
        foreach (var c in pool.All)
            AllChampions.Add(new ChampRowViewModel(c, _ddragon.ChampionName(c.ChampionId)));
        Practicing.Clear();
        foreach (var id in pool.PracticingChampionIds)
        {
            var stat = pool.All.First(c => c.ChampionId == id);
            Practicing.Add(new ChampCardViewModel(stat, _ddragon.ChampionName(id)));
        }
        OnPropertyChanged(nameof(HasPracticing));
        OnPropertyChanged(nameof(IsEmpty));
    }

    partial void OnRankedOnlyChanged(bool value) => Load();

    private BestBuildViewModel BuildFor(int championId, string role, string puuid)
    {
        var roleLabel = string.IsNullOrEmpty(role) ? "" : role;
        if (!_ddragon.HasItemData)
            return new BestBuildViewModel
            {
                ChampionId = championId,
                RoleLabel = roleLabel,
                ItemDataUnavailable = true
            };

        var matches = _db.AllMatches(RankedOnly)
            .Where(m => m.MyChampionId == championId &&
                        (string.IsNullOrEmpty(role) || m.MyTeamPosition == role))
            .ToList();

        var builds = new List<MatchBuild>();
        foreach (var m in matches)
        {
            var tlJson = _db.GetTimelineJson(m.MatchId);
            if (tlJson is null) continue;
            TimelineDto tl;
            try { tl = JsonSerializer.Deserialize<TimelineDto>(tlJson, Json)!; }
            catch { continue; }
            var pid = BuildExtractor.MyParticipantId(tl, puuid);
            if (pid is null) continue;
            var items = BuildExtractor.CompletedItemsPurchased(tl, pid.Value, _ddragon.CompletedItemIds);
            builds.Add(new MatchBuild(championId, roleLabel, m.Win, items));
        }

        var best = BuildAnalyzer.Analyze(championId, roleLabel, builds);
        if (best.TotalGames < 3)
            return new BestBuildViewModel
            {
                ChampionId = championId,
                RoleLabel = roleLabel,
                TotalGames = best.TotalGames,
                NotEnoughGames = true
            };

        var rows = best.Items
            .Select(s => new BuildItemRow
            {
                ItemName = _ddragon.ItemName(s.ItemId),
                Games = s.Games,
                WinRate = s.WinRate
            })
            .ToList();

        return new BestBuildViewModel
        {
            ChampionId = championId,
            RoleLabel = roleLabel,
            TotalGames = best.TotalGames,
            Items = rows
        };
    }
}
