using System.Net;
using RiftReview.Core.DataDragon;
using Xunit;

public class DataDragonClientTests
{
    [Fact]
    public async Task Resolves_latest_version_and_champion_names()
    {
        var h = new StubHttpMessageHandler(req =>
            req.RequestUri!.ToString().Contains("versions.json")
                ? StubHttpMessageHandler.Json("[\"15.12.1\",\"15.11.1\"]")
                : StubHttpMessageHandler.Json("{\"data\":{\"Ahri\":{\"key\":\"103\",\"id\":\"Ahri\",\"name\":\"Ahri\"}}}"));
        var tmp = Directory.CreateTempSubdirectory().FullName;
        var dd = new DataDragonClient(new HttpClient(h), tmp);
        await dd.EnsureLoadedAsync();
        Assert.Equal("15.12.1", dd.Version);
        Assert.Equal("Ahri", dd.ChampionName(103));
    }

    [Fact]
    public async Task Caches_champion_json_to_disk_and_reuses_it()
    {
        int champFetches = 0;
        var h = new StubHttpMessageHandler(req =>
        {
            if (req.RequestUri!.ToString().Contains("versions.json"))
                return StubHttpMessageHandler.Json("[\"15.12.1\"]");
            champFetches++;
            return StubHttpMessageHandler.Json("{\"data\":{\"Ahri\":{\"key\":\"103\",\"id\":\"Ahri\",\"name\":\"Ahri\"}}}");
        });
        var tmp = Directory.CreateTempSubdirectory().FullName;
        var dd1 = new DataDragonClient(new HttpClient(h), tmp);
        await dd1.EnsureLoadedAsync();
        var dd2 = new DataDragonClient(new HttpClient(h), tmp); // same cache dir
        await dd2.EnsureLoadedAsync();
        Assert.Equal("Ahri", dd2.ChampionName(103));
        Assert.Equal(1, champFetches); // champion.json fetched once; second load read it from disk
    }

    [Fact]
    public void Unknown_champion_returns_placeholder()
    {
        var dd = new DataDragonClient(new HttpClient(new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json("{}"))),
            Directory.CreateTempSubdirectory().FullName);
        Assert.Equal("Champ 999", dd.ChampionName(999));
    }
}
