using System.Net;
using System.Text.Json;
using RiftReview.Core.Riot.Dtos;

namespace RiftReview.Core.Riot;

public sealed class RiotApiClient : IRiotApiClient
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _http;
    private readonly RiotRateLimiter _rl;
    private readonly string _apiKey;
    private readonly string _regionalHost;
    private readonly string _platformHost;

    public RiotApiClient(HttpClient http, RiotRateLimiter rl, string apiKey, string platform)
    {
        _http = http;
        _rl = rl;
        _apiKey = apiKey;
        _regionalHost = RiotRouting.RegionalHost(platform);
        _platformHost = RiotRouting.PlatformHost(platform);
    }

    public Task<AccountDto> ResolvePuuidAsync(string gameName, string tagLine, CancellationToken ct = default)
        => GetAsync<AccountDto>(
            $"{_regionalHost}/riot/account/v1/accounts/by-riot-id/{Uri.EscapeDataString(gameName)}/{Uri.EscapeDataString(tagLine)}",
            ct);

    public Task<SummonerDto> GetSummonerByPuuidAsync(string puuid, CancellationToken ct = default)
        => GetAsync<SummonerDto>(
            $"{_platformHost}/lol/summoner/v4/summoners/by-puuid/{puuid}",
            ct);

    public Task<List<string>> GetMatchIdsAsync(string puuid, int start, int count, CancellationToken ct = default)
        => GetAsync<List<string>>(
            $"{_regionalHost}/lol/match/v5/matches/by-puuid/{puuid}/ids?start={start}&count={count}",
            ct);

    public async Task<string> GetRawAsync(string url, CancellationToken ct = default)
    {
        await _rl.WaitForSlotAsync(ct);
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("X-Riot-Token", _apiKey);
        using var resp = await _http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (resp.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var ra = resp.Headers.RetryAfter?.Delta
                     ?? (resp.Headers.TryGetValues("Retry-After", out var raVals)
                         && int.TryParse(raVals.FirstOrDefault(), out var raSec)
                         ? TimeSpan.FromSeconds(raSec)
                         : TimeSpan.FromSeconds(5));
            _rl.NotifyRetryAfter(ra);
            throw new RiotApiException(429, $"Rate limited; retry after {ra.TotalSeconds:0}s");
        }

        if (!resp.IsSuccessStatusCode)
            throw new RiotApiException((int)resp.StatusCode, $"Riot API {(int)resp.StatusCode} for {url}: {body}");

        return body;
    }

    public async Task<(MatchDto Dto, string Raw)> GetMatchWithRawAsync(string id, CancellationToken ct = default)
    {
        var raw = await GetRawAsync($"{_regionalHost}/lol/match/v5/matches/{id}", ct);
        return (JsonSerializer.Deserialize<MatchDto>(raw, Json)!, raw);
    }

    public async Task<(TimelineDto Dto, string Raw)> GetTimelineWithRawAsync(string id, CancellationToken ct = default)
    {
        var raw = await GetRawAsync($"{_regionalHost}/lol/match/v5/matches/{id}/timeline", ct);
        return (JsonSerializer.Deserialize<TimelineDto>(raw, Json)!, raw);
    }

    public async Task<IReadOnlyList<LeagueEntryDto>> GetLeagueEntriesAsync(string puuid, CancellationToken ct = default)
        => await GetAsync<List<LeagueEntryDto>>(
            $"{_platformHost}/lol/league/v4/entries/by-puuid/{puuid}", ct);

    private async Task<T> GetAsync<T>(string url, CancellationToken ct)
    {
        var body = await GetRawAsync(url, ct);
        return JsonSerializer.Deserialize<T>(body, Json)
               ?? throw new RiotApiException(0, $"Empty/invalid JSON from {url}");
    }
}
