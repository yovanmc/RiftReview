using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using RiftReview.Core.Analysis;
using RiftReview.Core.Configuration;
using RiftReview.Core.Data;
using RiftReview.Core.DataDragon;

namespace RiftReview.App.ViewModels;

public sealed partial class ChampPoolViewModel : ObservableObject
{
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
}
