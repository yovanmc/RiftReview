using System.Text.Json;
using RiftReview.Core.Analysis;
using RiftReview.Core.Data;
using RiftReview.Core.Riot.Dtos;

namespace RiftReview.Core.Sync;

// One-time, idempotent, local recompute of derived scalars (KP, damage share, pre-15 deaths)
// for pre-M2 rows. Reads the immutable stored blobs only; never re-fetches or mutates raw source.
public static class DerivedMetricsBackfill
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static int Run(RiftReviewDb db, IProgress<(int done, int total)>? progress = null)
    {
        var puuid = db.GetMeta("puuid");
        if (puuid is null) return 0;
        var ids = db.MatchIdsMissingDerivedMetrics();
        int done = 0;
        foreach (var id in ids)
        {
            var matchJson = db.GetMatchJson(id);
            var tlJson = db.GetTimelineJson(id);
            if (matchJson is null || tlJson is null) { progress?.Report((++done, ids.Count)); continue; }
            try
            {
                var match = JsonSerializer.Deserialize<MatchDto>(matchJson, Json)!;
                var tl = JsonSerializer.Deserialize<TimelineDto>(tlJson, Json)!;
                if (!match.Info.Participants.Any(p => p.Puuid == puuid)) { progress?.Report((++done, ids.Count)); continue; }
                var s = MatchExtractor.Summarize(match, puuid);
                int pre15 = TimelineExtractor.DeathsBeforeMinute(tl, s.MyParticipantId, 15);
                db.UpdateDerivedMetrics(id, s.KillParticipation, s.DamageShare, pre15);
            }
            catch { /* a single unparseable blob never aborts the backfill */ }
            progress?.Report((++done, ids.Count));
        }
        return db.MatchIdsMissingDerivedMetrics().Count == 0 ? ids.Count : ids.Count - db.MatchIdsMissingDerivedMetrics().Count;
    }
}
