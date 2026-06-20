using RiftReview.Core.Riot.Dtos;

namespace RiftReview.Core.Analysis;

public static class BuildExtractor
{
    /// <summary>
    /// My completed-item purchases from one timeline, in first-purchase order, deduped (an item counts
    /// once even if re-bought). Only ids present in <paramref name="completedItemIds"/> are kept, so
    /// components/consumables/trinkets fall away. Pure: no DB, no network.
    /// </summary>
    public static IReadOnlyList<int> CompletedItemsPurchased(
        TimelineDto tl, int myParticipantId, IReadOnlySet<int> completedItemIds)
    {
        var result = new List<int>();
        var seen = new HashSet<int>();
        var frames = tl?.Info?.Frames;
        if (frames is null) return result;

        foreach (var f in frames)
        {
            var events = f?.Events;
            if (events is null) continue;
            foreach (var e in events)
            {
                if (e.Type != "ITEM_PURCHASED") continue;
                if (e.ParticipantId != myParticipantId) continue;
                if (e.ItemId is not int id) continue;
                if (!completedItemIds.Contains(id)) continue;
                if (seen.Add(id)) result.Add(id);   // first-purchase order, deduped
            }
        }
        return result;
    }

    /// <summary>
    /// Resolve my participantId from the timeline. Prefers Metadata.Participants (ordered PUUID list,
    /// guaranteed present; index 0 → participantId 1). Falls back to Info.Participants (puuid match)
    /// if Metadata is null/empty or puuid not found there. Returns null if neither resolves.
    /// </summary>
    public static int? MyParticipantId(TimelineDto tl, string puuid)
    {
        if (string.IsNullOrEmpty(puuid)) return null;

        // Prefer Metadata.Participants: ordered list of PUUIDs, index 0 = participantId 1.
        var metaPuuids = tl?.Metadata?.Participants;
        if (metaPuuids is { Count: > 0 })
        {
            for (int i = 0; i < metaPuuids.Count; i++)
            {
                if (string.Equals(metaPuuids[i], puuid, StringComparison.OrdinalIgnoreCase))
                    return i + 1;
            }
        }

        // Fall back to Info.Participants (TimelineParticipantDto list).
        var infoParts = tl?.Info?.Participants;
        if (infoParts is null) return null;
        foreach (var p in infoParts)
            if (string.Equals(p.Puuid, puuid, StringComparison.OrdinalIgnoreCase))
                return p.ParticipantId;

        return null;
    }
}
