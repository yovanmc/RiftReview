using RiftReview.Core.Data;

namespace RiftReview.Core.Analysis;

public enum TiltSeverity { Calm, Caution, Tilted }

// One play session: a run of consecutive games with small idle gaps, plus tilt analysis.
public sealed record PlaySession(
    long StartUtc,
    long EndUtc,
    int Games,
    int Wins,
    int Losses,
    int LongestLossStreak,
    int EndLossStreak,
    int EndWinStreak,
    double? DeathsDelta,
    double? Cs10Delta,
    double? KdaDelta,
    bool DecayPresent,
    TiltSeverity Severity,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<MatchRow> GamesList);
