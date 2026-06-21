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

/// One game phase for one match. All metrics exact (no baseline applied here).
/// KillParticipation is null when the player's team scored no kills in the phase (never fabricate).
public sealed record PhaseStat(
    string Label,            // "Early" | "Mid" | "Late"
    double StartMinute,      // 0, 10, 20
    double EndMinute,        // capped at game end (partial phases allowed)
    double GoldDiffDelta,    // team gold-diff change across the phase (+ = my team gained)
    double CsPerMinute,      // CS gained in the phase / phase duration (minutes)
    int Deaths,              // my deaths in the phase
    int DeathsWhileBehind,   // subset of Deaths where team gold-diff < 0 at the death
    int Kills,               // my kills in the phase
    int Assists,             // my assists in the phase
    int TeamKills,           // my team's kills in the phase (KP denominator)
    double? KillParticipation); // (Kills+Assists)/TeamKills, null if TeamKills == 0

/// Per-phase own same-role baseline. Each metric is independently null when fewer than
/// `minGames` prior games contributed a value for it (never fabricate).
public sealed record PhaseBaseline(
    string Label,
    double? GoldDiffDelta,
    double? CsPerMinute,
    double? Deaths,
    double? KillParticipation);
