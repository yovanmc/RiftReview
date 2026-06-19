namespace RiftReview.Core.Data;

public sealed record MatchRow(
    string MatchId, int QueueId, long GameStartUtc, int DurationS, string Patch,
    int MyChampionId, string MyTeamPosition, bool Win,
    int Kills, int Deaths, int Assists, int Cs,
    int? CsAt10, int? GoldDiffAt15,
    int? OpponentParticipantId, int? OpponentChampionId, long SyncedAt,
    double? KillParticipation = null, double? DamageShare = null, int? DeathsPre15 = null);
