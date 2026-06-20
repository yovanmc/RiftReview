using System.Linq;
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
    private static readonly Brush CsLineBrush      = Frozen(0xC8, 0xAA, 0x6E); // gold (your CS pace)
    private static readonly Brush BaselineBrush    = Frozen(0x8A, 0x8A, 0x8A); // gray dashed — own-role trailing avg
    private static readonly Brush RankBaselineBrush = Frozen(0x5A, 0xA9, 0xE6); // blue dashed — rank average

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

    // Vision & objectives
    [ObservableProperty] private VisionStats _vision = new(0, 0, 0, 0);
    [ObservableProperty] private IReadOnlyList<ObjectiveRowVm> _objectives = Array.Empty<ObjectiveRowVm>();

    // Swing & causality (M8)
    [ObservableProperty] private bool _hasCausality;
    [ObservableProperty] private bool _hasSwing;
    [ObservableProperty] private string _swingText = "";
    [ObservableProperty] private bool _swingFavorable;
    [ObservableProperty] private double? _swingStartMinute;
    [ObservableProperty] private double? _swingEndMinute;
    [ObservableProperty] private IReadOnlyList<DeathContextVm> _deathContexts = Array.Empty<DeathContextVm>();
    [ObservableProperty] private string _backSummary = "";
    [ObservableProperty] private IReadOnlyList<BackVm> _backs = Array.Empty<BackVm>();
    [ObservableProperty] private bool _hasLag;
    [ObservableProperty] private string _turningPointLag = "";

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

            // Resolve rank CS/min baseline for the current match's role + tier.
            var rankTable = RiftReview.Core.Data.RankBaselineLoader.Load();
            var soloSnap = _db.GetLpSnapshots()
                .Where(s => s.QueueType == "RANKED_SOLO_5x5")
                .OrderByDescending(s => s.TakenUtc)
                .FirstOrDefault();
            double? rankCsPerMin = soloSnap == null ? null
                : RiftReview.Core.Analysis.RankBaselineProvider.Resolve(
                    rankTable, summary.MyTeamPosition, soloSnap.Tier, "csPerMin");

            // Assemble chart series for XAML binding.
            GoldSeries = new List<ChartSeries>
            {
                new(dd.GoldDiffVsTeam, TeamBrush),
                new(dd.GoldDiffVsLane, LaneBrush),  // lane drawn on top of team
            };
            var csSeries = new List<ChartSeries>
            {
                new(dd.CsPerMinute, CsLineBrush),
                new(csBaseline, BaselineBrush, Dashed: true),
            };
            if (rankCsPerMin is double r)
            {
                var rankPts = dd.CsPerMinute.Select(p => new ChartPoint(p.Minute, r)).ToList();
                csSeries.Add(new ChartSeries(rankPts, RankBaselineBrush, Dashed: true));
            }
            CsSeries = csSeries;

            var vo = TimelineExtractor.BuildVisionObjectives(tl, summary.MyParticipantId, summary.MyTeamId);
            Vision = vo.Vision;
            Objectives = vo.Objectives.Select(o => new ObjectiveRowVm(
                o.Label,
                o.TeamTotal == 0 ? "none taken" : $"{o.Participated} / {o.TeamTotal}",
                o.TeamTotal == 0 ? "" : ((double)o.Participated / o.TeamTotal).ToString("P0"))).ToList();

            // Swing & causality (M8)
            var causality = TimelineExtractor.BuildCausality(tl, summary.MyParticipantId);

            if (causality.Swing is { } sw)
            {
                HasSwing = true;
                SwingFavorable = sw.Favorable;
                SwingText = $"{FormatGold(sw.Delta)} {(sw.Favorable ? "in your favor" : "against you")} · "
                          + $"{Clock(sw.StartMinute)} → {Clock(sw.EndMinute)}";
                SwingStartMinute = sw.StartMinute;
                SwingEndMinute   = sw.EndMinute;
            }
            else { HasSwing = false; SwingText = "No decisive swing this game."; SwingStartMinute = null; SwingEndMinute = null; }

            DeathContexts = causality.Deaths
                .Select(d => new DeathContextVm(Clock(d.Minute), FormatGold(d.GoldDiff), d.GoldDiff >= 0))
                .ToList();

            Backs = causality.Backs
                .Select(b => new BackVm($"{Clock(b.Minute)}{(b.ItemCount > 1 ? $" ×{b.ItemCount}" : "")}"))
                .ToList();
            BackSummary = causality.Backs.Count switch { 0 => "", 1 => "1 back", var n => $"{n} backs" };

            HasLag = causality.TurningPointLagMinutes is not null;
            TurningPointLag = causality.TurningPointLagMinutes is double lag
                ? $"Power swing began {Clock(lag)} after a back"
                : "";

            HasCausality = HasSwing || DeathContexts.Count > 0 || Backs.Count > 0;

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
        Vision = new(0, 0, 0, 0);
        Objectives = Array.Empty<ObjectiveRowVm>();
        HasCausality = false;
        HasSwing = false;
        SwingText = "";
        SwingFavorable = false;
        SwingStartMinute = null;
        SwingEndMinute = null;
        DeathContexts = Array.Empty<DeathContextVm>();
        BackSummary = "";
        Backs = Array.Empty<BackVm>();
        HasLag = false;
        TurningPointLag = "";
    }

    // 14.0 -> "14:00", 14.5 -> "14:30"
    private static string Clock(double minute)
    {
        int total = (int)Math.Round(minute * 60);
        return $"{total / 60}:{total % 60:00}";
    }

    // +1850 -> "+1,850g", -1200 -> "−1,200g" (U+2212 minus), 0 -> "0g"
    private static string FormatGold(double g)
    {
        string sign = g > 0 ? "+" : g < 0 ? "−" : "";
        return $"{sign}{Math.Abs(g):#,0}g";
    }
}

public sealed record ObjectiveRowVm(string Label, string Detail, string Percent);
public sealed record DeathContextVm(string Minute, string Gold, bool Ahead);
public sealed record BackVm(string Text);
