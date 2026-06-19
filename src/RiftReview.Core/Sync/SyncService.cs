using RiftReview.Core.Analysis;
using RiftReview.Core.Data;
using RiftReview.Core.Riot;

namespace RiftReview.Core.Sync;

public sealed class SyncService
{
    private readonly RiftReviewDb _db;
    private readonly IRiotApiClient _client;
    public SyncService(RiftReviewDb db, IRiotApiClient client) { _db = db; _client = client; }

    public async Task<SyncResult> SyncAsync(int count, IProgress<SyncProgress>? progress, CancellationToken ct = default)
    {
        try
        {
            var puuid = _db.GetMeta("puuid") ?? throw new InvalidOperationException("No PUUID resolved.");
            var ids = await _client.GetMatchIdsAsync(puuid, 0, count, ct);
            var newIds = ids.Where(id => !_db.HasMatch(id)).ToList();
            progress?.Report(new SyncProgress(0, newIds.Count, null));
            int done = 0;
            foreach (var id in newIds)
            {
                ct.ThrowIfCancellationRequested();
                var (match, matchRaw) = await _client.GetMatchWithRawAsync(id, ct);
                var (timeline, tlRaw) = await _client.GetTimelineWithRawAsync(id, ct);
                var s = MatchExtractor.Summarize(match, puuid);
                var cs10 = TimelineExtractor.CsAtMinute(timeline, s.MyParticipantId, 10);
                var g15 = TimelineExtractor.GoldDiffAtMinute(timeline, s.MyParticipantId, s.OpponentParticipantId, 15);
                var row = new MatchRow(id, s.QueueId, s.GameStartUtc, s.DurationS, s.Patch,
                    s.MyChampionId, s.MyTeamPosition, s.Win, s.Kills, s.Deaths, s.Assists, s.Cs,
                    cs10, g15, s.OpponentParticipantId, s.OpponentChampionId, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                _db.UpsertMatch(row, matchRaw, tlRaw);
                progress?.Report(new SyncProgress(++done, newIds.Count, id));
            }
            _db.SetMeta("last_sync_utc", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
            return new SyncResult(newIds.Count, ids.Count - newIds.Count, null);
        }
        catch (RiotApiException ex) when (ex.IsKeyProblem)
        { return new SyncResult(0, 0, "Your Riot API key looks expired or invalid. Set a fresh dev key and try again."); }
        catch (RiotApiException ex)
        { return new SyncResult(0, 0, ex.Message); }
        catch (InvalidOperationException ex)
        { return new SyncResult(0, 0, ex.Message); }
    }
}
