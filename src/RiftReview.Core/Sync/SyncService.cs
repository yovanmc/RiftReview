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
            var ids = await FetchIdsAsync(puuid, count, ct);
            var newIds = ids.Where(id => !_db.HasMatch(id)).ToList();
            progress?.Report(new SyncProgress(0, newIds.Count, null));
            int done = 0;
            foreach (var id in newIds)
            {
                ct.ThrowIfCancellationRequested();
                var (match, matchRaw) = await _client.GetMatchWithRawAsync(id, ct);
                var (timeline, tlRaw) = await _client.GetTimelineWithRawAsync(id, ct);

                // Riot deep-history quirk: very old matches can come back without the account's PUUID
                // in the participant list. We can't attribute stats to the player, so skip such a match
                // rather than aborting the whole sync. (The display path already tolerates this.)
                if (!match.Info.Participants.Any(p => p.Puuid == puuid))
                {
                    progress?.Report(new SyncProgress(done, newIds.Count, $"skipped {id} (not in match)"));
                    continue;
                }

                var s = MatchExtractor.Summarize(match, puuid);
                var cs10 = TimelineExtractor.CsAtMinute(timeline, s.MyParticipantId, 10);
                var g15 = TimelineExtractor.GoldDiffAtMinute(timeline, s.MyParticipantId, s.OpponentParticipantId, 15);
                var row = new MatchRow(id, s.QueueId, s.GameStartUtc, s.DurationS, s.Patch,
                    s.MyChampionId, s.MyTeamPosition, s.Win, s.Kills, s.Deaths, s.Assists, s.Cs,
                    cs10, g15, s.OpponentParticipantId, s.OpponentChampionId, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                _db.UpsertMatch(row, matchRaw, tlRaw);
                done++;
                progress?.Report(new SyncProgress(done, newIds.Count, id));
            }
            await TrySnapshotLpAsync(puuid, ct);
            _db.SetMeta("last_sync_utc", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
            return new SyncResult(done, ids.Count - newIds.Count, null);
        }
        catch (RiotApiException ex) when (ex.IsKeyProblem)
        { return new SyncResult(0, 0, "Your Riot API key looks expired or invalid. Set a fresh dev key and try again."); }
        catch (RiotApiException ex)
        { return new SyncResult(0, 0, ex.Message); }
        catch (InvalidOperationException ex)
        { return new SyncResult(0, 0, ex.Message); }
    }

    private async Task<List<string>> FetchIdsAsync(string puuid, int count, CancellationToken ct)
    {
        const int page = 100;   // MATCH-V5 max per call
        var all = new List<string>();
        for (int start = 0; start < count; start += page)
        {
            var take = Math.Min(page, count - start);
            var batch = await _client.GetMatchIdsAsync(puuid, start, take, ct);
            all.AddRange(batch);
            if (batch.Count < take) break;   // reached end of history
        }
        return all;
    }

    private async Task TrySnapshotLpAsync(string puuid, CancellationToken ct)
    {
        try
        {
            var entries = await _client.GetLeagueEntriesAsync(puuid, ct);
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            foreach (var e in entries.Where(e => e.QueueType is "RANKED_SOLO_5x5" or "RANKED_FLEX_SR"))
                _db.InsertLpSnapshot(new LpSnapshot(now, e.QueueType, e.Tier, e.Rank, e.LeaguePoints, e.Wins, e.Losses));
        }
        catch { /* LP snapshot is best-effort; never fail the match sync over it */ }
    }
}
