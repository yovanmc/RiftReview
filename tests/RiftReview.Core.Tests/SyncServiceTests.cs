using RiftReview.Core.Data;
using RiftReview.Core.Riot;
using RiftReview.Core.Riot.Dtos;
using RiftReview.Core.Sync;
using Xunit;

public sealed class PagingFakeClient : IRiotApiClient
{
    private readonly int _total; private readonly MatchDto _m; private readonly TimelineDto _t;
    public List<(int Start, int Count)> IdCalls { get; } = new();
    public PagingFakeClient(int total, MatchDto match, TimelineDto timeline) { _total = total; _m = match; _t = timeline; }
    public Task<AccountDto> ResolvePuuidAsync(string g, string t, CancellationToken ct = default) => Task.FromResult(new AccountDto("ME", g, t));
    public Task<List<string>> GetMatchIdsAsync(string puuid, int start, int count, CancellationToken ct = default)
    {
        IdCalls.Add((start, count));
        var ids = Enumerable.Range(start, Math.Max(0, Math.Min(count, _total - start))).Select(i => $"NA1_{i}").ToList();
        return Task.FromResult(ids);
    }
    public Task<(MatchDto, string)> GetMatchWithRawAsync(string id, CancellationToken ct = default) => Task.FromResult((_m, "{}"));
    public Task<(TimelineDto, string)> GetTimelineWithRawAsync(string id, CancellationToken ct = default) => Task.FromResult((_t, "{}"));
    public Task<IReadOnlyList<LeagueEntryDto>> GetLeagueEntriesAsync(string puuid, CancellationToken ct = default)
        => Task.FromResult((IReadOnlyList<LeagueEntryDto>)new List<LeagueEntryDto>());
}

public sealed class ThrowingLeagueClient : IRiotApiClient
{
    private readonly List<string> _ids; private readonly MatchDto _m; private readonly TimelineDto _t;
    public ThrowingLeagueClient(List<string> ids, MatchDto m, TimelineDto t) { _ids = ids; _m = m; _t = t; }
    public Task<AccountDto> ResolvePuuidAsync(string g, string t, CancellationToken ct = default) => Task.FromResult(new AccountDto("ME", g, t));
    public Task<List<string>> GetMatchIdsAsync(string puuid, int start, int count, CancellationToken ct = default)
        => Task.FromResult(start == 0 ? _ids : new List<string>());
    public Task<(MatchDto, string)> GetMatchWithRawAsync(string id, CancellationToken ct = default) => Task.FromResult((_m, "{}"));
    public Task<(TimelineDto, string)> GetTimelineWithRawAsync(string id, CancellationToken ct = default) => Task.FromResult((_t, "{}"));
    public Task<IReadOnlyList<LeagueEntryDto>> GetLeagueEntriesAsync(string puuid, CancellationToken ct = default)
        => Task.FromException<IReadOnlyList<LeagueEntryDto>>(new RiotApiException(500, "boom"));
}

public sealed class FakeRiotClient : IRiotApiClient
{
    private readonly List<string> _ids;
    private readonly MatchDto _match;
    private readonly TimelineDto _timeline;
    private readonly AccountDto? _account;
    private readonly RiotApiException? _throwOnIds;
    private readonly IReadOnlyList<LeagueEntryDto> _league;
    public FakeRiotClient(List<string> ids, MatchDto match, TimelineDto timeline,
        AccountDto? account = null, RiotApiException? throwOnIds = null,
        IReadOnlyList<LeagueEntryDto>? league = null)
    { _ids = ids; _match = match; _timeline = timeline; _account = account; _throwOnIds = throwOnIds; _league = league ?? new List<LeagueEntryDto>(); }

    public Task<AccountDto> ResolvePuuidAsync(string gameName, string tagLine, CancellationToken ct = default)
        => Task.FromResult(_account ?? new AccountDto("ME", gameName, tagLine));
    public Task<List<string>> GetMatchIdsAsync(string puuid, int start, int count, CancellationToken ct = default)
        => _throwOnIds is null ? Task.FromResult(_ids) : Task.FromException<List<string>>(_throwOnIds);
    public Task<(MatchDto Dto, string Raw)> GetMatchWithRawAsync(string id, CancellationToken ct = default)
        => Task.FromResult((_match, "{\"match\":\"raw\"}"));
    public Task<(TimelineDto Dto, string Raw)> GetTimelineWithRawAsync(string id, CancellationToken ct = default)
        => Task.FromResult((_timeline, "{\"timeline\":\"raw\"}"));
    public Task<IReadOnlyList<LeagueEntryDto>> GetLeagueEntriesAsync(string puuid, CancellationToken ct = default)
        => Task.FromResult(_league);
}

public static class TestData
{
    public static MatchDto Match(string matchId, string myPuuid) => new(
        new MatchMetadata(matchId, new List<string> { myPuuid }),
        new MatchInfo(420, 1_700_000_000_000, 1800, "15.12.1", new List<ParticipantDto>
        {
            new(myPuuid, 1, 103, 100, "MIDDLE", true, 1, 1, 1, 100, 0),
        }));

    public static TimelineDto Timeline() => new(
        new TimelineMetadata("NA1_2", new List<string> { "ME" }),
        new TimelineInfo(60000, new List<FrameDto>
        {
            new(0, new Dictionary<string, ParticipantFrameDto> { ["1"] = new(1, 500, 0, 0) }, new List<EventDto>()),
        }));
}

public sealed class PerIdFakeClient : IRiotApiClient
{
    private readonly List<string> _ids;
    private readonly Dictionary<string, MatchDto> _matches;
    private readonly TimelineDto _t;
    public PerIdFakeClient(List<string> ids, Dictionary<string, MatchDto> matches, TimelineDto timeline)
    { _ids = ids; _matches = matches; _t = timeline; }
    public Task<AccountDto> ResolvePuuidAsync(string g, string t, CancellationToken ct = default) => Task.FromResult(new AccountDto("ME", g, t));
    public Task<List<string>> GetMatchIdsAsync(string puuid, int start, int count, CancellationToken ct = default)
        => Task.FromResult(start == 0 ? _ids : new List<string>());
    public Task<(MatchDto, string)> GetMatchWithRawAsync(string id, CancellationToken ct = default) => Task.FromResult((_matches[id], "{}"));
    public Task<(TimelineDto, string)> GetTimelineWithRawAsync(string id, CancellationToken ct = default) => Task.FromResult((_t, "{}"));
    public Task<IReadOnlyList<LeagueEntryDto>> GetLeagueEntriesAsync(string puuid, CancellationToken ct = default)
        => Task.FromResult((IReadOnlyList<LeagueEntryDto>)new List<LeagueEntryDto>());
}

public class SyncServiceTests
{
    [Fact]
    public async Task Sync_skips_existing_and_inserts_new()
    {
        using var db = RiftReviewDb.Open("Data Source=:memory:");
        db.SetMeta("puuid", "ME");
        db.UpsertMatch(new MatchRow("NA1_1", 420, 1, 1800, "15.12", 103, "MIDDLE", true, 1, 1, 1, 1, null, null, null, null, 1), "{}", "{}");

        var fake = new FakeRiotClient(
            ids: new() { "NA1_2", "NA1_1" },
            match: TestData.Match("NA1_2", "ME"),
            timeline: TestData.Timeline());
        var svc = new SyncService(db, fake);

        var res = await svc.SyncAsync(count: 20, progress: null);
        Assert.Equal(1, res.NewMatches);
        Assert.Equal(1, res.Skipped);
        Assert.True(db.HasMatch("NA1_2"));
        Assert.NotNull(db.GetTimelineJson("NA1_2"));
        Assert.Null(res.Error);
    }

    [Fact]
    public async Task Sync_returns_friendly_message_on_expired_key()
    {
        using var db = RiftReviewDb.Open("Data Source=:memory:");
        db.SetMeta("puuid", "ME");
        var fake = new FakeRiotClient(new(), TestData.Match("X", "ME"), TestData.Timeline(),
            throwOnIds: new RiotApiException(403, "Forbidden"));
        var svc = new SyncService(db, fake);

        var res = await svc.SyncAsync(20, null);
        Assert.Equal(0, res.NewMatches);
        Assert.NotNull(res.Error);
        Assert.Contains("key", res.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnsurePuuid_resolves_and_stores_when_missing()
    {
        using var db = RiftReviewDb.Open("Data Source=:memory:");
        var fake = new FakeRiotClient(new(), TestData.Match("X", "ME"), TestData.Timeline(),
            account: new AccountDto("PUUID-123", "Yovan", "NA1"));
        await AccountResolver.EnsurePuuidAsync(db, fake, "Yovan#NA1");
        Assert.Equal("PUUID-123", db.GetMeta("puuid"));
        Assert.Equal("Yovan#NA1", db.GetMeta("riot_id"));
    }

    [Fact]
    public async Task EnsurePuuid_is_noop_when_already_set()
    {
        using var db = RiftReviewDb.Open("Data Source=:memory:");
        db.SetMeta("puuid", "EXISTING");
        var fake = new FakeRiotClient(new(), TestData.Match("X", "ME"), TestData.Timeline());
        await AccountResolver.EnsurePuuidAsync(db, fake, "Yovan#NA1");
        Assert.Equal("EXISTING", db.GetMeta("puuid"));
    }

    [Fact]
    public async Task Sync_returns_error_when_no_puuid_resolved()
    {
        using var db = RiftReviewDb.Open("Data Source=:memory:");
        // intentionally no puuid set in meta
        var fake = new FakeRiotClient(new() { "NA1_2" }, TestData.Match("NA1_2", "ME"), TestData.Timeline());
        var svc = new SyncService(db, fake);

        var res = await svc.SyncAsync(20, null);

        Assert.Equal(0, res.NewMatches);
        Assert.NotNull(res.Error); // returns a SyncResult, does not throw
    }

    [Fact]
    public async Task Sync_paginates_ids_beyond_100()
    {
        using var db = RiftReviewDb.Open("Data Source=:memory:");
        db.SetMeta("puuid", "ME");
        var paged = new PagingFakeClient(total: 150, match: TestData.Match("X", "ME"), timeline: TestData.Timeline());
        var svc = new SyncService(db, paged);

        var res = await svc.SyncAsync(count: 150, progress: null);

        Assert.Equal(150, res.NewMatches);
        Assert.Equal(2, paged.IdCalls.Count);                 // 0..100, 100..150
        Assert.Equal((0, 100), paged.IdCalls[0]);
        Assert.Equal((100, 50), paged.IdCalls[1]);
    }

    [Fact]
    public async Task Sync_records_lp_snapshot_when_entries_present()
    {
        using var db = RiftReviewDb.Open("Data Source=:memory:");
        db.SetMeta("puuid", "ME");
        var fake = new FakeRiotClient(new() { "NA1_2" }, TestData.Match("NA1_2", "ME"), TestData.Timeline(),
            league: new List<LeagueEntryDto> { new("RANKED_SOLO_5x5", "GOLD", "II", 47, 10, 8) });
        var svc = new SyncService(db, fake);

        await svc.SyncAsync(20, null);

        var snaps = db.GetLpSnapshots();
        Assert.Single(snaps);
        Assert.Equal("GOLD", snaps[0].Tier);
        Assert.Equal(47, snaps[0].LeaguePoints);
    }

    [Fact]
    public async Task Sync_succeeds_even_if_lp_fetch_throws()
    {
        using var db = RiftReviewDb.Open("Data Source=:memory:");
        db.SetMeta("puuid", "ME");
        var fake = new ThrowingLeagueClient(new() { "NA1_2" }, TestData.Match("NA1_2", "ME"), TestData.Timeline());
        var svc = new SyncService(db, fake);

        var res = await svc.SyncAsync(20, null);

        Assert.Equal(1, res.NewMatches);     // match sync unaffected
        Assert.Null(res.Error);
        Assert.Empty(db.GetLpSnapshots());   // snapshot skipped, not fatal
    }

    [Fact]
    public async Task Sync_stores_derived_metrics()
    {
        using var db = RiftReviewDb.Open("Data Source=:memory:");
        db.SetMeta("puuid", "ME");
        var parts = new List<ParticipantDto>
        {
            new("ME", 3, 103, 100, "MIDDLE", true,  4, 2, 2, 200, 10, 1000),
            new("B",  1, 110, 100, "TOP",    true,  2, 1, 0, 180, 0,  1000),  // team100 kills=4+2=6, dmg=2000
            new("X",  8, 145, 200, "MIDDLE", false, 3, 3, 3, 180, 0,  1000),
        };
        var match = new MatchDto(new MatchMetadata("NA1_2", parts.Select(p => p.Puuid).ToList()),
            new MatchInfo(420, 1_700_000_000_000, 1800, "15.12.1", parts));
        var tl = new TimelineDto(new TimelineMetadata("NA1_2", new()),
            new TimelineInfo(60000, new List<FrameDto>
            {
                new(0, new(), new List<EventDto> { new("CHAMPION_KILL", 7*60000L, 8, 3) }), // me (pid 3) dies at 7:00
            }));
        var fake = new FakeRiotClient(new() { "NA1_2" }, match, tl);
        var res = await new SyncService(db, fake).SyncAsync(20, null);

        Assert.Equal(1, res.NewMatches);
        var row = db.GetMatch("NA1_2")!;
        Assert.Equal(1.0, row.KillParticipation!.Value, 3);   // (myK 4 + myA 2) / teamKills 6 = 1.0
        Assert.Equal(0.5, row.DamageShare!.Value, 3);          // myDmg 1000 / teamDmg 2000 = 0.5
        Assert.Equal(1, row.DeathsPre15);
    }

    [Fact]
    public async Task Sync_skips_matches_missing_my_puuid_and_continues()
    {
        using var db = RiftReviewDb.Open("Data Source=:memory:");
        db.SetMeta("puuid", "ME");

        // NA1_good has ME in participants; NA1_bad does NOT (old match w/o my puuid).
        var client = new PerIdFakeClient(
            ids: new() { "NA1_good", "NA1_bad" },
            matches: new()
            {
                ["NA1_good"] = TestData.Match("NA1_good", "ME"),
                ["NA1_bad"] = TestData.Match("NA1_bad", "SOMEONE_ELSE"),
            },
            timeline: TestData.Timeline());
        var svc = new SyncService(db, client);

        var res = await svc.SyncAsync(20, null);

        Assert.Null(res.Error);                 // one unattributable match must NOT abort the sync
        Assert.Equal(1, res.NewMatches);        // only the attributable match stored
        Assert.True(db.HasMatch("NA1_good"));
        Assert.False(db.HasMatch("NA1_bad"));   // skipped, not stored
    }
}
