namespace RiftReview.Core.Riot.Dtos;

// MATCH-V5 match detail (subset we use)
public sealed record MatchDto(MatchMetadata Metadata, MatchInfo Info);
// Participants = ordered PUUID strings
public sealed record MatchMetadata(string MatchId, List<string> Participants);
public sealed record MatchInfo(
    long QueueId, long GameCreation, long GameDuration, string GameVersion,
    List<ParticipantDto> Participants);
public sealed record ParticipantDto(
    string Puuid, int ParticipantId, int ChampionId, int TeamId, string TeamPosition, bool Win,
    int Kills, int Deaths, int Assists, int TotalMinionsKilled, int NeutralMinionsKilled);
