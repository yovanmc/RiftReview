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
}
