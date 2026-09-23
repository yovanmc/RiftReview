namespace RiftReview.Core.Data;

// One recorded ranked standing at a point in time (feeds the LP trend view).
public sealed record LpSnapshot(
    long TakenUtc, string QueueType, string Tier, string Division,
    int LeaguePoints, int Wins, int Losses);
