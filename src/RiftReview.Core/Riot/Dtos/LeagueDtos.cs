namespace RiftReview.Core.Riot.Dtos;

// LEAGUE-V4 entry (platform host). `Rank` is the division (I/II/III/IV); `Tier` is GOLD/PLATINUM/...
public sealed record LeagueEntryDto(
    string QueueType, string Tier, string Rank, int LeaguePoints, int Wins, int Losses);
