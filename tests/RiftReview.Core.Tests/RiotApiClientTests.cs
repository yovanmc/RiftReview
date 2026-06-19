using System.Net;
using RiftReview.Core.Riot;

public class RiotApiClientTests
{
    private static RiotApiClient Make(StubHttpMessageHandler h)
    {
        var http = new HttpClient(h);
        var clock = new FakeClock();
        var rl = new RiotRateLimiter(clock, (_, _) => Task.CompletedTask);
        return new RiotApiClient(http, rl, apiKey: "RGAPI-test", platform: "na1");
    }

    [Fact]
    public async Task ResolvePuuid_uses_regional_host_and_sends_token()
    {
        var h = new StubHttpMessageHandler(_ =>
            StubHttpMessageHandler.Json("{\"puuid\":\"P1\",\"gameName\":\"Yovan\",\"tagLine\":\"NA1\"}"));
        var c = Make(h);
        var acc = await c.ResolvePuuidAsync("Yovan", "NA1");
        Assert.Equal("P1", acc.Puuid);
        var req = h.Requests[0];
        Assert.Contains("americas.api.riotgames.com", req.RequestUri!.ToString());
        Assert.Contains("/riot/account/v1/accounts/by-riot-id/Yovan/NA1", req.RequestUri!.ToString());
        Assert.Equal("RGAPI-test", req.Headers.GetValues("X-Riot-Token").Single());
    }

    [Fact]
    public async Task GetSummoner_uses_platform_host()
    {
        var h = new StubHttpMessageHandler(_ =>
            StubHttpMessageHandler.Json("{\"puuid\":\"P1\",\"summonerLevel\":321,\"profileIconId\":7}"));
        var c = Make(h);
        await c.GetSummonerByPuuidAsync("P1");
        Assert.Contains("na1.api.riotgames.com", h.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task Non2xx_throws_RiotApiException_with_status()
    {
        var h = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json("{}", HttpStatusCode.Forbidden));
        var c = Make(h);
        var ex = await Assert.ThrowsAsync<RiotApiException>(() => c.ResolvePuuidAsync("x", "y"));
        Assert.True(ex.IsKeyProblem);
    }

    [Fact]
    public async Task Rate_limited_429_throws_and_backs_off_per_retry_after()
    {
        var clock = new FakeClock();
        var totalDelay = TimeSpan.Zero;
        Func<TimeSpan, CancellationToken, Task> delay = (d, _) => { totalDelay += d; clock.Advance(d); return Task.CompletedTask; };
        var rl = new RiotRateLimiter(clock, delay);
        var h = new StubHttpMessageHandler(_ =>
        {
            var m = new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.TooManyRequests)
                { Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json") };
            m.Headers.TryAddWithoutValidation("Retry-After", "5");
            return m;
        });
        var c = new RiotApiClient(new HttpClient(h), rl, apiKey: "RGAPI-test", platform: "na1");

        var ex = await Assert.ThrowsAsync<RiotApiException>(() => c.ResolvePuuidAsync("x", "y"));
        Assert.True(ex.IsRateLimited);

        // The client must have called NotifyRetryAfter(5s): the next slot wait must back off >= 5s.
        await rl.WaitForSlotAsync();
        Assert.True(totalDelay >= TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task GetLeagueEntries_parses_entries_and_hits_platform_host()
    {
        var body = "[{\"queueType\":\"RANKED_SOLO_5x5\",\"tier\":\"GOLD\",\"rank\":\"II\",\"leaguePoints\":47,\"wins\":120,\"losses\":110}]";
        var stub = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(body));
        var c = Make(stub);

        var entries = await c.GetLeagueEntriesAsync("PUUID-1");

        Assert.Single(entries);
        Assert.Equal("RANKED_SOLO_5x5", entries[0].QueueType);
        Assert.Equal("GOLD", entries[0].Tier);
        Assert.Equal("II", entries[0].Rank);
        Assert.Equal(47, entries[0].LeaguePoints);
        Assert.Contains("na1.api.riotgames.com", stub.Requests[0].RequestUri!.ToString());
        Assert.Contains("/lol/league/v4/entries/by-puuid/PUUID-1", stub.Requests[0].RequestUri!.ToString());
    }
}
