namespace RiftReview.Core.Riot.Dtos;

// MATCH-V5 timeline (subset)
public sealed record TimelineDto(TimelineMetadata Metadata, TimelineInfo Info);
public sealed record TimelineMetadata(string MatchId, List<string> Participants);
public sealed record TimelineInfo(long FrameInterval, List<FrameDto> Frames);
public sealed record FrameDto(
    long Timestamp,
    Dictionary<string, ParticipantFrameDto> ParticipantFrames,
    List<EventDto> Events);
public sealed record ParticipantFrameDto(
    int ParticipantId, int TotalGold, int MinionsKilled, int JungleMinionsKilled);
public sealed record EventDto(string Type, long Timestamp, int? KillerId, int? VictimId);
