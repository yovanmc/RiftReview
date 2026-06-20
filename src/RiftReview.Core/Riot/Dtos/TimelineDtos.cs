namespace RiftReview.Core.Riot.Dtos;

// MATCH-V5 timeline (subset)
public sealed record TimelineDto(TimelineMetadata Metadata, TimelineInfo Info);
// Participants = ordered PUUID strings
public sealed record TimelineMetadata(string MatchId, List<string> Participants);
public sealed record TimelineInfo(long FrameInterval, List<FrameDto> Frames);
public sealed record FrameDto(
    long Timestamp,
    Dictionary<string, ParticipantFrameDto> ParticipantFrames,
    List<EventDto> Events);
public sealed record ParticipantFrameDto(
    int ParticipantId, int TotalGold, int MinionsKilled, int JungleMinionsKilled);
public sealed record EventDto(
    string Type, long Timestamp, int? KillerId, int? VictimId,
    int? CreatorId = null,
    string? WardType = null,
    int? KillerTeamId = null,
    string? MonsterType = null,
    string? MonsterSubType = null,
    string? BuildingType = null,
    string? TowerType = null,
    string? LaneType = null,
    int? TeamId = null,
    List<int>? AssistingParticipantIds = null,
    int? ParticipantId = null,    // ITEM_PURCHASED / ITEM_SOLD use participantId (NOT killerId)
    int? ItemId = null);          // ITEM_PURCHASED itemId (not rendered; reserved for future)
