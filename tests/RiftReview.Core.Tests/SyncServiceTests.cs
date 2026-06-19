using RiftReview.Core.Data;
using RiftReview.Core.Riot;
using RiftReview.Core.Riot.Dtos;
using RiftReview.Core.Sync;
using Xunit;

public sealed class FakeRiotClient : IRiotApiClient
{
    private readonly List<string> _ids;
    private readonly MatchDto _match;
    private readonly TimelineDto _timeline;
    private readonly AccountDto? _account;
    private readonly RiotApiException? _throwOnIds;
    public FakeRiotClient(List<string> ids, MatchDto match, TimelineDto timeline,
        AccountDto? account = null, RiotApiException? throwOnIds = null)
    { _ids = ids; _match = match; _timeline = timeline; _account = account; _throwOnIds = throwOnIds; }

    public Task<AccountDto> ResolvePuuidAsync(string gameName, string tagLine, CancellationToken ct = default)
        => Task.FromResult(_account ?? new AccountDto("ME", gameName, tagLine));
    public Task<List<string>> GetMatchIdsAsync(string puuid, int start, int count, CancellationToken ct = default)
        => _throwOnIds is null ? Task.FromResult(_ids) : Task.FromException<List<string>>(_throwOnIds);
    public Task<(MatchDto Dto, string Raw)> GetMatchWithRawAsync(string id, CancellationToken ct = default)
        => Task.FromResult((_match, "{\"match\":\"raw\"}"));
    public Task<(TimelineDto Dto, string Raw)> GetTimelineWithRawAsync(string id, CancellationToken ct = default)
        => Task.FromResult((_timeline, "{\"timeline\":\"raw\"}"));
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
}
