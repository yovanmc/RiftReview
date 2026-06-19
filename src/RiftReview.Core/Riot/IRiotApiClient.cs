namespace RiftReview.Core.Riot;

public interface IRiotApiClient
{
    Task<Dtos.AccountDto> ResolvePuuidAsync(string gameName, string tagLine, CancellationToken ct = default);
    Task<List<string>> GetMatchIdsAsync(string puuid, int start, int count, CancellationToken ct = default);
    Task<(Dtos.MatchDto Dto, string Raw)> GetMatchWithRawAsync(string id, CancellationToken ct = default);
    Task<(Dtos.TimelineDto Dto, string Raw)> GetTimelineWithRawAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<Dtos.LeagueEntryDto>> GetLeagueEntriesAsync(string puuid, CancellationToken ct = default);
}
