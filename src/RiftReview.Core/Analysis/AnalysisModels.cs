namespace RiftReview.Core.Analysis;

public readonly record struct ChartPoint(double Minute, double Value);

public sealed record MatchSummary(
    int QueueId, long GameStartUtc, int DurationS, string Patch,
    int MyChampionId, string MyTeamPosition, bool Win,
    int Kills, int Deaths, int Assists, int Cs,
    int MyParticipantId, int? OpponentParticipantId, int? OpponentChampionId,
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
