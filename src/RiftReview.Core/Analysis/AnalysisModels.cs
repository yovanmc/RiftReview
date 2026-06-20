namespace RiftReview.Core.Analysis;

public readonly record struct ChartPoint(double Minute, double Value);

public sealed record MatchSummary(
    int QueueId, long GameStartUtc, int DurationS, string Patch,
    int MyChampionId, string MyTeamPosition, bool Win,
    int Kills, int Deaths, int Assists, int Cs,
    int MyParticipantId, int MyTeamId, int? OpponentParticipantId, int? OpponentChampionId,
    double KillParticipation, double DamageShare);

public sealed record DeepDive(
    IReadOnlyList<ChartPoint> GoldDiffVsLane,
    IReadOnlyList<ChartPoint> GoldDiffVsTeam,
    IReadOnlyList<ChartPoint> CsPerMinute,
    IReadOnlyList<double> DeathMinutes,
    bool HasLaneOpponent);

public sealed record VisionStats(
    int WardsPlaced, int WardsCleared, int ControlWardsPlaced, int VisionProxy);

public sealed record ObjectiveParticipation(
    string Label, int Participated, int TeamTotal);

public sealed record VisionObjectivesResult(
    VisionStats Vision, IReadOnlyList<ObjectiveParticipation> Objectives);

/// One inflection window on the TEAM gold-diff curve. Favorable = my team's lead grew.
public sealed record SwingPoint(
    double StartMinute, double EndMinute,
    double StartGold, double EndGold,
    double Delta, bool Favorable);

/// Team gold-diff (signed; + = my team ahead) at the minute I died.
public sealed record DeathContext(double Minute, double GoldDiff);

/// A recall, inferred from a cluster of my ITEM_PURCHASED events. ItemCount = items bought that trip.
public sealed record BackEvent(double Minute, int ItemCount);

public sealed record CausalityResult(
    SwingPoint? Swing,                       // null = no decisive swing
    IReadOnlyList<DeathContext> Deaths,
    IReadOnlyList<BackEvent> Backs,
    double? TurningPointLagMinutes);         // min from preceding back to swing start; null if N/A
