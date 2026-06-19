using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
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

    public void Load(MatchRow selected)
    {
        var puuid = _db.GetMeta("puuid");
        var matchJson = _db.GetMatchJson(selected.MatchId);
        var tlJson = _db.GetTimelineJson(selected.MatchId);
        if (puuid is null || matchJson is null || tlJson is null) { Clear(); return; }

        var match = JsonSerializer.Deserialize<MatchDto>(matchJson, Json)!;
        var summary = MatchExtractor.Summarize(match, puuid);
        var tl = JsonSerializer.Deserialize<TimelineDto>(tlJson, Json)!;
        var dd = TimelineExtractor.BuildDeepDive(tl, summary.MyParticipantId, summary.OpponentParticipantId);

        GoldVsLane = dd.GoldDiffVsLane;
        GoldVsTeam = dd.GoldDiffVsTeam;
        CsCurve = dd.CsPerMinute;
        DeathMinutes = dd.DeathMinutes;
        HasLaneOpponent = dd.HasLaneOpponent;
        CsBaseline = BuildBaseline(selected, puuid);

        var champ = _ddragon?.ChampionName(summary.MyChampionId) ?? $"Champ {summary.MyChampionId}";
        Header = $"{champ} · {summary.MyTeamPosition} · {(summary.Win ? "Win" : "Loss")} · {summary.Kills}/{summary.Deaths}/{summary.Assists}";
        HasData = true;
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
            catch { /* skip a match whose blobs can't be parsed/summarized */ }
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
    }
}
