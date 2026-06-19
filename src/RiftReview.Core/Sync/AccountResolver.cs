using RiftReview.Core.Data;
using RiftReview.Core.Riot;

namespace RiftReview.Core.Sync;

public static class AccountResolver
{
    public static async Task EnsurePuuidAsync(RiftReviewDb db, IRiotApiClient client, string riotId, CancellationToken ct = default)
    {
        if (db.GetMeta("puuid") is not null) return;
        var parts = riotId.Split('#', 2);
        if (parts.Length != 2) throw new InvalidOperationException("Riot ID must be 'GameName#TAG'.");
        var acc = await client.ResolvePuuidAsync(parts[0], parts[1], ct);
        db.SetMeta("puuid", acc.Puuid);
        db.SetMeta("riot_id", riotId);
    }
}
