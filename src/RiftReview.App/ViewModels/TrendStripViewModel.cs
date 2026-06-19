using CommunityToolkit.Mvvm.ComponentModel;
using RiftReview.Core.Data;

namespace RiftReview.App.ViewModels;

public sealed partial class TrendStripViewModel : ObservableObject
{
    [ObservableProperty] private IReadOnlyList<bool> _wins = Array.Empty<bool>();
    [ObservableProperty] private IReadOnlyList<int> _deaths = Array.Empty<int>();
    [ObservableProperty] private IReadOnlyList<int> _csAt10 = Array.Empty<int>();
    [ObservableProperty] private IReadOnlyList<int> _goldAt15 = Array.Empty<int>();

    public void Load(IReadOnlyList<MatchRow> rows)
    {
        var ordered = rows.OrderBy(r => r.GameStartUtc).ToList();
        Wins = ordered.Select(r => r.Win).ToList();
        Deaths = ordered.Select(r => r.Deaths).ToList();
        CsAt10 = ordered.Select(r => r.CsAt10 ?? 0).ToList();
        GoldAt15 = ordered.Select(r => r.GoldDiffAt15 ?? 0).ToList();
    }
}
