namespace RiftReview.Core.Analysis;

public sealed record StreakSummary(
    int CurrentStreak,
    int LongestWinStreak,
    int LongestLossStreak,
    IReadOnlyList<bool> RecentForm);

public sealed record LpSegment(
    string QueueType,
    long FromUtc, long ToUtc,
    int FromPoints, int ToPoints, int NetLp,
    int GamesInWindow,
    string FromLabel, string ToLabel);
