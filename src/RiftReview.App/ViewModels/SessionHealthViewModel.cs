using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using RiftReview.Core.Analysis;
using RiftReview.Core.Data;
using RiftReview.Core.DataDragon;

namespace RiftReview.App.ViewModels;

public sealed partial class SessionHealthViewModel : ObservableObject
{
    private readonly RiftReviewDb _db;
    private readonly DataDragonClient _ddragon;

    public SessionHealthViewModel(RiftReviewDb db, DataDragonClient ddragon)
    {
        _db = db; _ddragon = ddragon;
        Refresh();
    }

    public ObservableCollection<SessionRowViewModel> Sessions { get; } = new();
    [ObservableProperty] private SessionRowViewModel? _latest;
    [ObservableProperty] private bool _isEmpty;

    [ObservableProperty] private bool _bannerVisible;
    [ObservableProperty] private string _bannerHeadline = "";
    [ObservableProperty] private TiltSeverity _bannerSeverity;

    public async Task InitializeAsync()
    {
        try { await _ddragon.EnsureLoadedAsync(); } catch { /* offline — names not needed here */ }
        Refresh();
    }

    public void Refresh()
    {
        var sessions = SessionCalculator.BuildSessions(_db.AllMatches(rankedOnly: false));
        Sessions.Clear();
        foreach (var s in sessions) Sessions.Add(new SessionRowViewModel(s));
        Latest = Sessions.FirstOrDefault();
        IsEmpty = Sessions.Count == 0;

        if (Latest is { } l && l.Severity >= TiltSeverity.Caution)
        {
            BannerVisible = true;
            BannerSeverity = l.Severity;
            BannerHeadline = l.IsTilted
                ? $"Tilt check: {l.ReasonsText}. Consider stepping away."
                : $"Heads up: {l.ReasonsText}.";
        }
        else { BannerVisible = false; BannerSeverity = TiltSeverity.Calm; BannerHeadline = ""; }
    }
}
