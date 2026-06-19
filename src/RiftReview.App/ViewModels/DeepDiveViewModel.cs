using System.Text.Json;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using RiftReview.App.Controls;
using RiftReview.Core.Analysis;
using RiftReview.Core.Data;
using RiftReview.Core.DataDragon;
using RiftReview.Core.Riot.Dtos;

namespace RiftReview.App.ViewModels;

public sealed partial class DeepDiveViewModel : ObservableObject
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private readonly RiftReviewDb _db;
    private readonly DataDragonClient? _ddragon;

    // Static frozen brushes — safe to create off-UI-thread (SolidColorBrush.Freeze() is thread-safe).
    private static readonly Brush LaneBrush     = Frozen(0xC8, 0xAA, 0x6E); // Hextech Gold (vs lane)
    private static readonly Brush TeamBrush     = Frozen(0x5A, 0xA9, 0xE6); // blue (vs team)
    private static readonly Brush CsLineBrush   = Frozen(0xC8, 0xAA, 0x6E); // gold (your CS pace)
    private static readonly Brush BaselineBrush = Frozen(0x8A, 0x8A, 0x8A); // gray dashed baseline

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var br = new SolidColorBrush(Color.FromRgb(r, g, b));
        br.Freeze();
        return br;
    }

    public DeepDiveViewModel(RiftReviewDb db, DataDragonClient? ddragon = null)
    {
        _db = db;
        _ddragon = ddragon;
    }

    [ObservableProperty] private string _header = "";
    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private bool _hasLaneOpponent;
    [ObservableProperty] private IReadOnlyList<ChartPoint> _goldVsLane = Array.Empty<ChartPoint>();
    [ObservableProperty] private IReadOnlyList<ChartPoint> _goldVsTeam = Array.Empty<ChartPoint>();
    [ObservableProperty] private IReadOnlyList<ChartPoint> _csCurve = Array.Empty<ChartPoint>();
    [ObservableProperty] private IReadOnlyList<ChartPoint> _csBaseline = Array.Empty<ChartPoint>();
    [ObservableProperty] private IReadOnlyList<double> _deathMinutes = Array.Empty<double>();

    // Assembled ChartSeries for the LineChart controls in DeepDiveView.
    [ObservableProperty] private IReadOnlyList<ChartSeries> _goldSeries = Array.Empty<ChartSeries>();
    [ObservableProperty] private IReadOnlyList<ChartSeries> _csSeries   = Array.Empty<ChartSeries>();

    public void Load(MatchRow selected)
    {
        var puuid = _db.GetMeta("puuid");
        var matchJson = _db.GetMatchJson(selected.MatchId);
        var tlJson = _db.GetTimelineJson(selected.MatchId);
        if (puuid is null || matchJson is null || tlJson is null) { Clear(); return; }

        try
        {
            var match = JsonSerializer.Deserialize<MatchDto>(matchJson, Json)!;
            var summary = MatchExtractor.Summarize(match, puuid);
            var tl = JsonSerializer.Deserialize<TimelineDto>(tlJson, Json)!;
            var dd = TimelineExtractor.BuildDeepDive(tl, summary.MyParticipantId, summary.OpponentParticipantId);

            GoldVsLane = dd.GoldDiffVsLane;
            GoldVsTeam = dd.GoldDiffVsTeam;
            CsCurve = dd.CsPerMinute;
            DeathMinutes = dd.DeathMinutes;
            HasLaneOpponent = dd.HasLaneOpponent;
            var csBaseline = BuildBaseline(selected, puuid);
            CsBaseline = csBaseline;

            var champ = _ddragon?.ChampionName(summary.MyChampionId) ?? $"Champ {summary.MyChampionId}";
            Header = $"{champ} · {summary.MyTeamPosition} · {(summary.Win ? "Win" : "Loss")} · {summary.Kills}/{summary.Deaths}/{summary.Assists}";

            // Assemble chart series for XAML binding.
            GoldSeries = new List<ChartSeries>
            {
                new(dd.GoldDiffVsTeam, TeamBrush),
                new(dd.GoldDiffVsLane, LaneBrush),  // lane drawn on top of team
            };
            CsSeries = new List<ChartSeries>
            {
                new(dd.CsPerMinute, CsLineBrush),
                new(csBaseline, BaselineBrush, Dashed: true),
            };
            HasData = true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            Clear(); // a corrupt/unexpected stored match → show the empty state, never crash
        }
    }

    private IReadOnlyList<ChartPoint> BuildBaseline(MatchRow selected, string puuid)
    {
        var curves = new List<IReadOnlyList<ChartPoint>>();
        foreach (var m in _db.RecentMatches(rankedOnly: false, limit: 20)
                 .Where(m => m.MyTeamPosition == selected.MyTeamPosition && m.MatchId != selected.MatchId))
        {
            var mj = _db.GetMatchJson(m.MatchId);
            var tj = _db.GetTimelineJson(m.MatchId);
            if (mj is null || tj is null) continue;
            try
            {
                var mm = JsonSerializer.Deserialize<MatchDto>(mj, Json)!;
                var ms = MatchExtractor.Summarize(mm, puuid);
                var mtl = JsonSerializer.Deserialize<TimelineDto>(tj, Json)!;
                var mdd = TimelineExtractor.BuildDeepDive(mtl, ms.MyParticipantId, ms.OpponentParticipantId);
                curves.Add(mdd.CsPerMinute);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            { /* skip a match whose stored blobs can't be parsed/summarized */ }
        }
        return BaselineCalculator.Average(curves);
    }

    private void Clear()
    {
        HasData = false;
        HasLaneOpponent = false;
        Header = "";
        GoldVsLane = Array.Empty<ChartPoint>();
        GoldVsTeam = Array.Empty<ChartPoint>();
        CsCurve = Array.Empty<ChartPoint>();
        CsBaseline = Array.Empty<ChartPoint>();
        DeathMinutes = Array.Empty<double>();
        GoldSeries = Array.Empty<ChartSeries>();
        CsSeries   = Array.Empty<ChartSeries>();
    }
}
