# RiftReview M1 — Deep spine + Champion pool — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deepen RiftReview's match history, add a navigation shell + Settings, begin LP snapshotting, and ship the hybrid Champion-pool dashboard.

**Architecture:** Extend the existing v1 spine. New pure Core unit (`ChampPoolCalculator`) aggregates the lean `matches` rows into per-champ stats + trends. New `lp_snapshots` table (schema → v2) accrues rank/LP each sync. Match depth becomes a `meta`-backed setting; sync paginates MATCH-V5 ids. The WPF app gains a WPF-UI `NavigationView` shell hosting three pages: Review (existing screen, unchanged), Champions (new), Settings (new).

**Tech Stack:** C# / .NET 10, WPF + WPF-UI 4.3, CommunityToolkit.Mvvm, Microsoft.Data.Sqlite, xUnit.

**Spec:** `docs/superpowers/specs/2026-06-19-riftreview-m1-design.md`

---

## Build-time verification gates (confirm BEFORE trusting representative code)

These are flagged inline on the tasks that depend on them. Do not invent API surface.

1. **LEAGUE-V4 endpoint + DTO** (Task 4): confirm `/lol/league/v4/entries/by-puuid/{puuid}` exists on the **platform** host and returns a JSON array of entries with fields `queueType`, `tier`, `rank`, `leaguePoints`, `wins`, `losses` (verify at developer.riotgames.com). If by-puuid is unavailable, fall back to SUMMONER-V4 `id` → `entries/by-summoner/{summonerId}` (note: `SummonerDto` currently lacks `id`; add it only if you take this path).
2. **MATCH-V5 ids pagination** (Task 5): confirm `count` max is 100 per call and `start` paginates; an over-the-end page returns a short/empty list.
3. **WPF-UI 4.3 `NavigationView` API** (Task 8): confirm against the installed `Wpf.Ui` 4.3.0 package how to (a) declare `NavigationViewItem`s, (b) host pages, and (c) resolve pages from DI (`IPageService` / `SetServiceProvider` / `SetPageProviderService` — exact name varies by version). Adjust the representative XAML/code to the real 4.3 API.

## Existing code touch-points (read before starting)

- `src/RiftReview.Core/Data/RiftReviewDb.cs` — schema, `RunVersionedMigrations`, `LatestSchemaVersion=1`, CRUD, `meta` key/value.
- `src/RiftReview.Core/Data/MatchRow.cs` — 17-member positional record.
- `src/RiftReview.Core/Riot/IRiotApiClient.cs`, `Riot/RiotApiClient.cs`, `Riot/Dtos/SummonerDtos.cs`.
- `src/RiftReview.Core/Sync/SyncService.cs`, `Sync/SyncProgress.cs`, `Sync/AccountResolver.cs`.
- `src/RiftReview.Core/Analysis/BaselineCalculator.cs`, `Analysis/AnalysisModels.cs` (has `ChartPoint`).
- `src/RiftReview.App/App.xaml.cs` (DI graph), `MainWindow.xaml(.cs)`, `ViewModels/MainViewModel.cs`, `Demo/DemoSeeder.cs`.
- Tests: `tests/RiftReview.Core.Tests/{RiftReviewDbTests,SyncServiceTests,RiotApiClientTests,StubHttpMessageHandler}.cs`; `tests/RiftReview.App.Tests/DeepDiveViewModelTests.cs`.
- Theme brushes available (from `MainWindow.xaml` / `Themes/Colors.xaml`): `WindowBgBrush`, `PanelBgBrush`, `CardBgBrush`, `HairlineBrush`, `TextPrimaryBrush`, `TextMutedBrush`, `WinBrush`, `LossBrush`, plus the Hextech-gold accent.

**Branching:** create the work branch via `superpowers:using-git-worktrees` at execution start. Commit per task with `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.

---

## Task 1: Schema v2 — `lp_snapshots` table + CRUD

**Files:**
- Create: `src/RiftReview.Core/Data/LpSnapshot.cs`
- Modify: `src/RiftReview.Core/Data/RiftReviewDb.cs`
- Test: `tests/RiftReview.Core.Tests/RiftReviewDbTests.cs`

- [ ] **Step 1: Write the failing tests** — append to `RiftReviewDbTests.cs`:

```csharp
[Fact]
public void Schema_is_v2()
{
    using var db = NewDb();
    Assert.Equal(2, db.GetSchemaVersion());
    Assert.Equal(2, RiftReviewDb.LatestSchemaVersion);
}

[Fact]
public void Lp_snapshot_insert_and_read_roundtrips()
{
    using var db = NewDb();
    var snap = new LpSnapshot(1_700_000_000, "RANKED_SOLO_5x5", "GOLD", "II", 47, 120, 110);
    db.InsertLpSnapshot(snap);
    var all = db.GetLpSnapshots();
    Assert.Single(all);
    Assert.Equal(snap, all[0]);
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test --filter RiftReviewDbTests`
Expected: FAIL (`LpSnapshot` undefined; schema is 1).

- [ ] **Step 3: Create `LpSnapshot.cs`**

```csharp
namespace RiftReview.Core.Data;

// One recorded ranked standing at a point in time (feeds the M5 LP trend view).
public sealed record LpSnapshot(
    long TakenUtc, string QueueType, string Tier, string Division,
    int LeaguePoints, int Wins, int Losses);
```

- [ ] **Step 4: Implement migration + CRUD in `RiftReviewDb.cs`**

Change `LatestSchemaVersion` to `2`. In `RunVersionedMigrations`, after the `v < 1` block add:

```csharp
        if (v < 2)
        {
            Exec(_conn, @"CREATE TABLE IF NOT EXISTS lp_snapshots (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  taken_utc INTEGER NOT NULL,
  queue_type TEXT NOT NULL,
  tier TEXT NOT NULL,
  division TEXT NOT NULL,
  league_points INTEGER NOT NULL,
  wins INTEGER NOT NULL,
  losses INTEGER NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_lp_taken ON lp_snapshots(taken_utc);");
            Exec(_conn, "PRAGMA user_version=2;");
        }
```

Add methods (near the other CRUD):

```csharp
public void InsertLpSnapshot(LpSnapshot s)
{
    using var c = _conn.CreateCommand();
    c.CommandText = @"INSERT INTO lp_snapshots
        (taken_utc,queue_type,tier,division,league_points,wins,losses)
        VALUES ($t,$q,$tier,$div,$lp,$w,$l);";
    Bind(c, "$t", s.TakenUtc); Bind(c, "$q", s.QueueType); Bind(c, "$tier", s.Tier);
    Bind(c, "$div", s.Division); Bind(c, "$lp", s.LeaguePoints);
    Bind(c, "$w", s.Wins); Bind(c, "$l", s.Losses);
    c.ExecuteNonQuery();
}

public IReadOnlyList<LpSnapshot> GetLpSnapshots()
{
    using var c = _conn.CreateCommand();
    c.CommandText = "SELECT taken_utc,queue_type,tier,division,league_points,wins,losses FROM lp_snapshots ORDER BY taken_utc";
    var list = new List<LpSnapshot>();
    using var r = c.ExecuteReader();
    while (r.Read())
        list.Add(new LpSnapshot(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3),
            r.GetInt32(4), r.GetInt32(5), r.GetInt32(6)));
    return list;
}
```

- [ ] **Step 5: Run tests to verify pass**

Run: `dotnet test --filter RiftReviewDbTests`
Expected: PASS (existing `Initialize_sets_user_version...` still passes — it asserts `LatestSchemaVersion`, now 2).

- [ ] **Step 6: Commit**

```bash
git add src/RiftReview.Core/Data/LpSnapshot.cs src/RiftReview.Core/Data/RiftReviewDb.cs tests/RiftReview.Core.Tests/RiftReviewDbTests.cs
git commit -m "feat(core): schema v2 — lp_snapshots table + CRUD"
```

---

## Task 2: `AllMatches` read (full stored depth)

**Files:**
- Modify: `src/RiftReview.Core/Data/RiftReviewDb.cs`
- Test: `tests/RiftReview.Core.Tests/RiftReviewDbTests.cs`

The champ-pool calculator needs every stored match, not just the latest 20.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void AllMatches_returns_every_stored_row_newest_first()
{
    using var db = NewDb();
    for (int i = 0; i < 25; i++)
        db.UpsertMatch(Row($"NA1_{i}", i % 2 == 0 ? 420 : 400, start: 1000 + i), "{}", "{}");
    Assert.Equal(25, db.AllMatches(rankedOnly: false).Count);
    Assert.True(db.AllMatches(rankedOnly: true).Count < 25);     // only ranked (420/440)
    Assert.Equal("NA1_24", db.AllMatches(false)[0].MatchId);     // newest first
}
```

- [ ] **Step 2: Run to verify failure** — Run: `dotnet test --filter RiftReviewDbTests`; Expected: FAIL (`AllMatches` undefined).

- [ ] **Step 3: Implement** — add to `RiftReviewDb.cs`:

```csharp
public IReadOnlyList<MatchRow> AllMatches(bool rankedOnly)
{
    using var c = _conn.CreateCommand();
    c.CommandText = "SELECT * FROM matches" +
        (rankedOnly ? " WHERE queue_id IN (420,440)" : "") +
        " ORDER BY game_start_utc DESC";
    var list = new List<MatchRow>();
    using var r = c.ExecuteReader();
    while (r.Read()) list.Add(ReadRow(r));
    return list;
}
```

- [ ] **Step 4: Run to verify pass** — Run: `dotnet test --filter RiftReviewDbTests`; Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/RiftReview.Core/Data/RiftReviewDb.cs tests/RiftReview.Core.Tests/RiftReviewDbTests.cs
git commit -m "feat(core): AllMatches read for full-depth aggregation"
```

---

## Task 3: `ChampPoolCalculator` (pure aggregation)

**Files:**
- Create: `src/RiftReview.Core/Analysis/ChampPoolModels.cs`
- Create: `src/RiftReview.Core/Analysis/ChampPoolCalculator.cs`
- Test: `tests/RiftReview.Core.Tests/ChampPoolCalculatorTests.cs`

- [ ] **Step 1: Write the failing tests** — create `ChampPoolCalculatorTests.cs`:

```csharp
using RiftReview.Core.Analysis;
using RiftReview.Core.Data;
using Xunit;

public class ChampPoolCalculatorTests
{
    private static MatchRow M(int champ, bool win, int k, int d, int a, int? cs10, long start) =>
        new($"NA1_{start}", 420, start, 1800, "15.12", champ, "MIDDLE", win, k, d, a, 200, cs10, 0, 7, 1, start);

    [Fact]
    public void Aggregates_per_champion()
    {
        var rows = new List<MatchRow>
        {
            M(103, true,  5, 2, 10, 80, 300),
            M(103, false, 3, 4, 6,  60, 200),
            M(157, true,  9, 1, 4,  90, 100),
        };
        var pool = ChampPoolCalculator.Build(rows);
        var ahri = pool.All.Single(c => c.ChampionId == 103);
        Assert.Equal(2, ahri.Games);
        Assert.Equal(1, ahri.Wins);
        Assert.Equal(0.5, ahri.WinRate, 3);
        Assert.Equal((5 + 3 + 10 + 6) / 6.0, ahri.Kda, 3);   // (K+A)/D = 24/6 = 4.0
        Assert.Equal(70.0, ahri.AvgCs10!.Value, 3);          // (80+60)/2
        Assert.Equal(3.0, ahri.AvgDeaths, 3);                // (2+4)/2
    }

    [Fact]
    public void Kda_with_zero_deaths_uses_one_as_divisor()
    {
        var pool = ChampPoolCalculator.Build(new List<MatchRow> { M(99, true, 4, 0, 6, 70, 100) });
        Assert.Equal(10.0, pool.All.Single().Kda, 3);        // (4+6)/max(1,0)
    }

    [Fact]
    public void Trend_is_chronological_oldest_to_newest()
    {
        var rows = new List<MatchRow> { M(103, true, 1, 1, 1, 90, 300), M(103, true, 1, 1, 1, 70, 100) };
        var trend = ChampPoolCalculator.Build(rows).All.Single().TrendCs10;
        Assert.Equal(new int?[] { 70, 90 }, trend.ToArray());  // start=100 first, start=300 last
    }

    [Fact]
    public void Practicing_is_top_champs_in_recent_window_with_min_games()
    {
        var rows = new List<MatchRow>();
        long s = 0;
        for (int i = 0; i < 6; i++) rows.Add(M(103, true, 1, 1, 1, 80, ++s));  // K'Sante x6
        for (int i = 0; i < 4; i++) rows.Add(M(157, true, 1, 1, 1, 80, ++s));  // Galio x4
        rows.Add(M(7, true, 1, 1, 1, 80, ++s));                                // LeBlanc x1 (below min)
        var pool = ChampPoolCalculator.Build(rows, recentWindow: 15, maxPracticing: 3, minPracticeGames: 2);
        Assert.Equal(new[] { 103, 157 }, pool.PracticingChampionIds.ToArray());
        Assert.DoesNotContain(7, pool.PracticingChampionIds);
    }

    [Fact]
    public void Empty_input_yields_empty_pool()
    {
        var pool = ChampPoolCalculator.Build(new List<MatchRow>());
        Assert.Empty(pool.All);
        Assert.Empty(pool.PracticingChampionIds);
    }
}
```

- [ ] **Step 2: Run to verify failure** — Run: `dotnet test --filter ChampPoolCalculatorTests`; Expected: FAIL (types undefined).

- [ ] **Step 3: Create `ChampPoolModels.cs`**

```csharp
namespace RiftReview.Core.Analysis;

public sealed record ChampStat(
    int ChampionId, int Games, int Wins, int Losses, double WinRate,
    double Kda, double? AvgCs10, double AvgDeaths, IReadOnlyList<int?> TrendCs10);

public sealed record ChampPool(
    IReadOnlyList<ChampStat> All, IReadOnlyList<int> PracticingChampionIds);
```

- [ ] **Step 4: Create `ChampPoolCalculator.cs`**

```csharp
using RiftReview.Core.Data;

namespace RiftReview.Core.Analysis;

public static class ChampPoolCalculator
{
    public static ChampPool Build(IReadOnlyList<MatchRow> matches,
        int recentWindow = 15, int maxPracticing = 3, int minPracticeGames = 2)
    {
        // matches arrive newest-first (AllMatches order). Group by champion.
        var stats = matches
            .GroupBy(m => m.MyChampionId)
            .Select(g =>
            {
                var games = g.ToList();
                int wins = games.Count(m => m.Win);
                int sumK = games.Sum(m => m.Kills), sumD = games.Sum(m => m.Deaths), sumA = games.Sum(m => m.Assists);
                var cs10 = games.Where(m => m.CsAt10 is not null).Select(m => m.CsAt10!.Value).ToList();
                var trend = games.OrderBy(m => m.GameStartUtc).Select(m => m.CsAt10).ToList();   // oldest→newest
                return new ChampStat(
                    g.Key, games.Count, wins, games.Count - wins,
                    games.Count == 0 ? 0 : (double)wins / games.Count,
                    (sumK + sumA) / (double)Math.Max(1, sumD),
                    cs10.Count == 0 ? null : cs10.Average(),
                    games.Average(m => (double)m.Deaths),
                    trend);
            })
            .OrderByDescending(c => c.Games).ThenByDescending(c => c.WinRate)
            .ToList();

        // Practicing = top champs by games within the most-recent `recentWindow` games.
        var recent = matches.Take(recentWindow);
        var practicing = recent
            .GroupBy(m => m.MyChampionId)
            .Select(g => (Champ: g.Key, Count: g.Count()))
            .Where(x => x.Count >= minPracticeGames)
            .OrderByDescending(x => x.Count)
            .Take(maxPracticing)
            .Select(x => x.Champ)
            .ToList();

        return new ChampPool(stats, practicing);
    }
}
```

- [ ] **Step 5: Run to verify pass** — Run: `dotnet test --filter ChampPoolCalculatorTests`; Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/RiftReview.Core/Analysis/ChampPoolModels.cs src/RiftReview.Core/Analysis/ChampPoolCalculator.cs tests/RiftReview.Core.Tests/ChampPoolCalculatorTests.cs
git commit -m "feat(core): ChampPoolCalculator — per-champ aggregates + practice detection"
```

---

## Task 4: LEAGUE-V4 DTO + client method

> **GATE 1** — confirm the by-puuid endpoint + field names before trusting this code.

**Files:**
- Create: `src/RiftReview.Core/Riot/Dtos/LeagueDtos.cs`
- Modify: `src/RiftReview.Core/Riot/IRiotApiClient.cs`, `src/RiftReview.Core/Riot/RiotApiClient.cs`
- Modify (keep compiling): `tests/RiftReview.Core.Tests/SyncServiceTests.cs` (FakeRiotClient)
- Test: `tests/RiftReview.Core.Tests/RiotApiClientTests.cs`

- [ ] **Step 1: Write the failing test** — append to `RiotApiClientTests.cs` (mirror its existing StubHttpMessageHandler usage):

```csharp
[Fact]
public async Task GetLeagueEntries_parses_entries_and_hits_platform_host()
{
    var body = "[{\"queueType\":\"RANKED_SOLO_5x5\",\"tier\":\"GOLD\",\"rank\":\"II\",\"leaguePoints\":47,\"wins\":120,\"losses\":110}]";
    var stub = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(body));
    var client = new RiotApiClient(new HttpClient(stub), new RiotRateLimiter(new FakeClock()), "RGAPI-test", "na1");

    var entries = await client.GetLeagueEntriesAsync("PUUID-1");

    Assert.Single(entries);
    Assert.Equal("RANKED_SOLO_5x5", entries[0].QueueType);
    Assert.Equal("GOLD", entries[0].Tier);
    Assert.Equal("II", entries[0].Rank);
    Assert.Equal(47, entries[0].LeaguePoints);
    Assert.Contains("na1.api.riotgames.com", stub.Requests[0].RequestUri!.ToString());
    Assert.Contains("/lol/league/v4/entries/by-puuid/PUUID-1", stub.Requests[0].RequestUri!.ToString());
}
```

(If the existing `RiotApiClientTests.cs` constructs `RiotApiClient`/`FakeClock` differently, match that local convention.)

- [ ] **Step 2: Run to verify failure** — Run: `dotnet test --filter RiotApiClientTests`; Expected: FAIL (`GetLeagueEntriesAsync` undefined).

- [ ] **Step 3: Create `LeagueDtos.cs`**

```csharp
namespace RiftReview.Core.Riot.Dtos;

// LEAGUE-V4 entry (platform host). `Rank` is the division (I/II/III/IV); `Tier` is GOLD/PLATINUM/...
public sealed record LeagueEntryDto(
    string QueueType, string Tier, string Rank, int LeaguePoints, int Wins, int Losses);
```

- [ ] **Step 4: Extend the interface** — add to `IRiotApiClient.cs`:

```csharp
    Task<IReadOnlyList<Dtos.LeagueEntryDto>> GetLeagueEntriesAsync(string puuid, CancellationToken ct = default);
```

- [ ] **Step 5: Implement on `RiotApiClient.cs`** (platform host; reuse the private `GetAsync<T>`):

```csharp
public async Task<IReadOnlyList<LeagueEntryDto>> GetLeagueEntriesAsync(string puuid, CancellationToken ct = default)
    => await GetAsync<List<LeagueEntryDto>>(
        $"{_platformHost}/lol/league/v4/entries/by-puuid/{puuid}", ct);
```

- [ ] **Step 6: Update `FakeRiotClient`** in `SyncServiceTests.cs` so the suite compiles — add a field + method:

```csharp
    private readonly IReadOnlyList<LeagueEntryDto> _league;
    // extend the constructor with: IReadOnlyList<LeagueEntryDto>? league = null
    // and assign: _league = league ?? new List<LeagueEntryDto>();
    public Task<IReadOnlyList<LeagueEntryDto>> GetLeagueEntriesAsync(string puuid, CancellationToken ct = default)
        => Task.FromResult(_league);
```

- [ ] **Step 7: Run to verify pass** — Run: `dotnet test`; Expected: PASS (whole suite compiles + green).

- [ ] **Step 8: Commit**

```bash
git add src/RiftReview.Core/Riot/Dtos/LeagueDtos.cs src/RiftReview.Core/Riot/IRiotApiClient.cs src/RiftReview.Core/Riot/RiotApiClient.cs tests/RiftReview.Core.Tests/
git commit -m "feat(core): LEAGUE-V4 entries client (by-puuid) + DTO"
```

---

## Task 5: Sync — depth pagination + best-effort LP snapshot

> **GATE 2** — confirm MATCH-V5 `start`/`count` pagination (max 100/call).

**Files:**
- Modify: `src/RiftReview.Core/Sync/SyncService.cs`
- Modify (keep compiling): `tests/RiftReview.Core.Tests/SyncServiceTests.cs` (FakeRiotClient paging)
- Test: `tests/RiftReview.Core.Tests/SyncServiceTests.cs`

- [ ] **Step 1: Write the failing tests** — add:

```csharp
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
```

Add the two helper fakes to the test file:

```csharp
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
```

- [ ] **Step 2: Run to verify failure** — Run: `dotnet test --filter SyncServiceTests`; Expected: FAIL (single id call; no snapshot).

- [ ] **Step 3: Implement pagination + LP snapshot in `SyncService.cs`** — replace the single `GetMatchIdsAsync` call with a paginating helper, and after the match loop (before `SetMeta("last_sync_utc"...)`) add a best-effort snapshot. Full updated method body:

```csharp
public async Task<SyncResult> SyncAsync(int count, IProgress<SyncProgress>? progress, CancellationToken ct = default)
{
    try
    {
        var puuid = _db.GetMeta("puuid") ?? throw new InvalidOperationException("No PUUID resolved.");
        var ids = await FetchIdsAsync(puuid, count, ct);
        var newIds = ids.Where(id => !_db.HasMatch(id)).ToList();
        progress?.Report(new SyncProgress(0, newIds.Count, null));
        int done = 0;
        foreach (var id in newIds)
        {
            ct.ThrowIfCancellationRequested();
            var (match, matchRaw) = await _client.GetMatchWithRawAsync(id, ct);
            var (timeline, tlRaw) = await _client.GetTimelineWithRawAsync(id, ct);
            var s = MatchExtractor.Summarize(match, puuid);
            var cs10 = TimelineExtractor.CsAtMinute(timeline, s.MyParticipantId, 10);
            var g15 = TimelineExtractor.GoldDiffAtMinute(timeline, s.MyParticipantId, s.OpponentParticipantId, 15);
            var row = new MatchRow(id, s.QueueId, s.GameStartUtc, s.DurationS, s.Patch,
                s.MyChampionId, s.MyTeamPosition, s.Win, s.Kills, s.Deaths, s.Assists, s.Cs,
                cs10, g15, s.OpponentParticipantId, s.OpponentChampionId, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            _db.UpsertMatch(row, matchRaw, tlRaw);
            progress?.Report(new SyncProgress(++done, newIds.Count, id));
        }
        await TrySnapshotLpAsync(puuid, ct);
        _db.SetMeta("last_sync_utc", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
        return new SyncResult(newIds.Count, ids.Count - newIds.Count, null);
    }
    catch (RiotApiException ex) when (ex.IsKeyProblem)
    { return new SyncResult(0, 0, "Your Riot API key looks expired or invalid. Set a fresh dev key and try again."); }
    catch (RiotApiException ex)
    { return new SyncResult(0, 0, ex.Message); }
    catch (InvalidOperationException ex)
    { return new SyncResult(0, 0, ex.Message); }
}

private async Task<List<string>> FetchIdsAsync(string puuid, int count, CancellationToken ct)
{
    const int page = 100;   // MATCH-V5 max per call (GATE 2)
    var all = new List<string>();
    for (int start = 0; start < count; start += page)
    {
        var take = Math.Min(page, count - start);
        var batch = await _client.GetMatchIdsAsync(puuid, start, take, ct);
        all.AddRange(batch);
        if (batch.Count < take) break;   // reached end of history
    }
    return all;
}

private async Task TrySnapshotLpAsync(string puuid, CancellationToken ct)
{
    try
    {
        var entries = await _client.GetLeagueEntriesAsync(puuid, ct);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var e in entries.Where(e => e.QueueType is "RANKED_SOLO_5x5" or "RANKED_FLEX_SR"))
            _db.InsertLpSnapshot(new LpSnapshot(now, e.QueueType, e.Tier, e.Rank, e.LeaguePoints, e.Wins, e.Losses));
    }
    catch { /* LP snapshot is best-effort; never fail the match sync over it */ }
}
```

Ensure `using RiftReview.Core.Data;` (for `LpSnapshot`) is present.

- [ ] **Step 4: Run to verify pass** — Run: `dotnet test`; Expected: PASS (all, including v1 sync tests).

- [ ] **Step 5: Commit**

```bash
git add src/RiftReview.Core/Sync/SyncService.cs tests/RiftReview.Core.Tests/SyncServiceTests.cs
git commit -m "feat(core): paginate match-id fetch + best-effort LP snapshot per sync"
```

---

## Task 6: Settings store (`meta`-backed)

**Files:**
- Create: `src/RiftReview.Core/Configuration/SettingsStore.cs`
- Test: `tests/RiftReview.Core.Tests/SettingsStoreTests.cs`

- [ ] **Step 1: Write the failing tests** — create `SettingsStoreTests.cs`:

```csharp
using RiftReview.Core.Configuration;
using RiftReview.Core.Data;
using Xunit;

public class SettingsStoreTests
{
    private static RiftReviewDb Db() => RiftReviewDb.Open("Data Source=:memory:");

    [Fact]
    public void Defaults_when_unset()
    {
        using var db = Db();
        var s = new SettingsStore(db);
        Assert.Equal(150, s.MatchDepth);
        Assert.True(s.DefaultRankedOnly);
    }

    [Fact]
    public void Persists_and_clamps_depth()
    {
        using var db = Db();
        new SettingsStore(db).MatchDepth = 5000;          // over max
        Assert.Equal(300, new SettingsStore(db).MatchDepth);   // clamped, persisted across instances
        new SettingsStore(db).MatchDepth = 1;             // under min
        Assert.Equal(20, new SettingsStore(db).MatchDepth);
    }

    [Fact]
    public void Persists_ranked_default()
    {
        using var db = Db();
        new SettingsStore(db).DefaultRankedOnly = false;
        Assert.False(new SettingsStore(db).DefaultRankedOnly);
    }
}
```

- [ ] **Step 2: Run to verify failure** — Run: `dotnet test --filter SettingsStoreTests`; Expected: FAIL.

- [ ] **Step 3: Implement `SettingsStore.cs`**

```csharp
using RiftReview.Core.Data;

namespace RiftReview.Core.Configuration;

public sealed class SettingsStore
{
    public const int MinDepth = 20, MaxDepth = 300, DefaultDepth = 150;
    private readonly RiftReviewDb _db;
    public SettingsStore(RiftReviewDb db) => _db = db;

    public int MatchDepth
    {
        get => int.TryParse(_db.GetMeta("settings.match_depth"), out var v) ? v : DefaultDepth;
        set => _db.SetMeta("settings.match_depth", Math.Clamp(value, MinDepth, MaxDepth).ToString());
    }

    public bool DefaultRankedOnly
    {
        get => _db.GetMeta("settings.ranked_only") is not "false";   // default true
        set => _db.SetMeta("settings.ranked_only", value ? "true" : "false");
    }
}
```

- [ ] **Step 4: Run to verify pass** — Run: `dotnet test --filter SettingsStoreTests`; Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/RiftReview.Core/Configuration/SettingsStore.cs tests/RiftReview.Core.Tests/SettingsStoreTests.cs
git commit -m "feat(core): meta-backed SettingsStore (match depth, default filter)"
```

---

## Task 7: Wire deeper sync into the Review VM

**Files:**
- Modify: `src/RiftReview.App/ViewModels/MainViewModel.cs`
- Modify: `src/RiftReview.App/App.xaml.cs` (register `SettingsStore`)

Use the configured depth instead of the hard-coded 20.

- [ ] **Step 1: Register `SettingsStore` in DI** — in `App.xaml.cs` `ConfigureServices`, after the `RiftReviewDb` registration:

```csharp
                s.AddSingleton<RiftReview.Core.Configuration.SettingsStore>();
```

- [ ] **Step 2: Inject + use it in `MainViewModel`** — add `SettingsStore` to the constructor, store `_settings`, and in `SyncAsync` replace `await _sync.SyncAsync(20, progress)` with:

```csharp
            var res = await _sync.SyncAsync(_settings.MatchDepth, progress);
```

Also initialize `RankedOnly` from `_settings.DefaultRankedOnly` in the constructor (before `Reload()`):

```csharp
        _rankedOnly = settings.DefaultRankedOnly;
```

(`_rankedOnly` is the `[ObservableProperty]` backing field — set it directly to avoid triggering `Reload` before construction completes.)

- [ ] **Step 3: Build + run existing tests** — Run: `dotnet build RiftReview.slnx` then `dotnet test`; Expected: build clean, all green (no behavior test here; covered by manual/screenshot later). If an App test constructs `MainViewModel` directly, update it to pass a `SettingsStore`.

- [ ] **Step 4: Commit**

```bash
git add src/RiftReview.App/ViewModels/MainViewModel.cs src/RiftReview.App/App.xaml.cs
git commit -m "feat(app): sync to configured match depth; default filter from settings"
```

---

## Task 8: Navigation shell (WPF-UI `NavigationView`)

> **GATE 3** — confirm the WPF-UI 4.3 `NavigationView` page-hosting + DI API and adjust this representative code to match.

**Files:**
- Create: `src/RiftReview.App/Views/ReviewView.xaml` (+ `.xaml.cs`)
- Create: `src/RiftReview.App/AppShell.xaml` (+ `.xaml.cs`)
- Modify: `src/RiftReview.App/App.xaml.cs` (resolve `AppShell` instead of `MainWindow`; register pages)
- (Keep `MainWindow.xaml` temporarily or delete after extraction — see Step 2)

- [ ] **Step 1: Extract the Review screen into `ReviewView`** — create `ReviewView.xaml` as a `UserControl` (or `ui:NavigationViewItem` content page per the confirmed API) whose body is the **exact** current `MainWindow.xaml` content from the inner `<Grid Grid.Row="1">` (rail + right panel), minus the window chrome (`TitleBar`, `FluentWindow` attributes). Keep all bindings (`SyncCommand`, `RankedOnly`, `Matches`, `SelectedMatch`, `TrendStrip`, `DeepDive`, `StatusMessage`, `IsError`, `IsEmpty`) and the same `conv:`/`views:` namespaces + resource converters. Code-behind:

```csharp
using System.Windows.Controls;
using RiftReview.App.ViewModels;

namespace RiftReview.App.Views;

public partial class ReviewView : UserControl
{
    public ReviewView(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Loaded += async (_, _) => await vm.InitializeAsync();
    }
}
```

- [ ] **Step 2: Create `AppShell`** — a `ui:FluentWindow` (same black-glass attributes as the old `MainWindow`: `WindowBackdropType="None"`, `ExtendsContentIntoTitleBar="True"`, `Background="{StaticResource WindowBgBrush}"`, 1120×720) containing a `ui:TitleBar` + a `ui:NavigationView` with three items (Review / Champions / Settings). Representative XAML (adjust to the confirmed 4.3 API):

```xml
<ui:NavigationView x:Name="Nav" PaneDisplayMode="LeftFluent" IsBackButtonVisible="Collapsed">
  <ui:NavigationView.MenuItems>
    <ui:NavigationViewItem Content="Review"    Icon="{ui:SymbolIcon History24}"   TargetPageType="{x:Type views:ReviewView}"/>
    <ui:NavigationViewItem Content="Champions" Icon="{ui:SymbolIcon People24}"     TargetPageType="{x:Type views:ChampPoolView}"/>
  </ui:NavigationView.MenuItems>
  <ui:NavigationView.FooterMenuItems>
    <ui:NavigationViewItem Content="Settings"  Icon="{ui:SymbolIcon Settings24}"   TargetPageType="{x:Type views:SettingsView}"/>
  </ui:NavigationView.FooterMenuItems>
</ui:NavigationView>
```

Code-behind wires DI page resolution per the confirmed API, e.g.:

```csharp
public partial class AppShell : FluentWindow
{
    public AppShell(IServiceProvider sp)
    {
        InitializeComponent();
        // GATE 3: exact call name/signature confirmed against Wpf.Ui 4.3
        Nav.SetServiceProvider(sp);
    }
}
```

- [ ] **Step 3: Update DI + startup** — in `App.xaml.cs`: register `ReviewView`, `ChampPoolView`, `SettingsView`, and `AppShell` (transient pages, singleton shell); replace `GetRequiredService<MainWindow>()` with `GetRequiredService<AppShell>()`. Remove the old `AddSingleton<MainWindow>()` (and delete `MainWindow.xaml(.cs)` once `ReviewView` renders identically). Keep `--seed-demo` handling unchanged.

- [ ] **Step 4: Build + manual smoke** — Run: `dotnet build RiftReview.slnx`; Expected: clean. (Visual verification happens in Task 12.) The Champions/Settings pages can be stubs at this point (created in Tasks 9–10); to keep the build green, create empty placeholder `ChampPoolView`/`SettingsView` UserControls now if needed, fleshed out next.

- [ ] **Step 5: Commit**

```bash
git add src/RiftReview.App/Views/ReviewView.xaml src/RiftReview.App/Views/ReviewView.xaml.cs src/RiftReview.App/AppShell.xaml src/RiftReview.App/AppShell.xaml.cs src/RiftReview.App/App.xaml.cs
git commit -m "feat(app): NavigationView shell; Review screen extracted into a page"
```

---

## Task 9: Champions dashboard (view + VM + sparkline)

**Files:**
- Create: `src/RiftReview.App/Controls/Sparkline.cs`
- Create: `src/RiftReview.App/ViewModels/ChampPoolViewModel.cs`, `ChampCardViewModel.cs`, `ChampRowViewModel.cs`
- Create: `src/RiftReview.App/Views/ChampPoolView.xaml` (+ `.xaml.cs`)
- Test: `tests/RiftReview.App.Tests/ChampPoolViewModelTests.cs`

- [ ] **Step 1: Write the failing VM test** — create `ChampPoolViewModelTests.cs` (mirrors `DeepDiveViewModelTests` DB setup):

```csharp
using RiftReview.App.ViewModels;
using RiftReview.Core.Data;
using RiftReview.Core.DataDragon;
using RiftReview.Core.Configuration;
using Xunit;

public class ChampPoolViewModelTests
{
    private static MatchRow M(int champ, bool win, int cs10, long start) =>
        new($"NA1_{start}", 420, start, 1800, "15.12", champ, "MIDDLE", win, 5, 2, 7, 200, cs10, 0, 7, 1, start);

    [Fact]
    public void Load_builds_rows_and_practice_cards()
    {
        using var db = RiftReviewDb.Open("Data Source=:memory:");
        long s = 0;
        for (int i = 0; i < 5; i++) db.UpsertMatch(M(103, i % 2 == 0, 80, ++s), "{}", "{}");
        for (int i = 0; i < 3; i++) db.UpsertMatch(M(157, true, 70, ++s), "{}", "{}");
        var dd = new DataDragonClient(new HttpClient(), System.IO.Path.GetTempPath());  // names fall back to "Champ N" offline
        var vm = new ChampPoolViewModel(db, dd, new SettingsStore(db));

        vm.Load();

        Assert.Equal(2, vm.AllChampions.Count);
        Assert.Contains(vm.Practicing, c => c.ChampionId == 103);
        Assert.Equal(5, vm.AllChampions.First(c => c.ChampionId == 103).Games);
    }
}
```

- [ ] **Step 2: Run to verify failure** — Run: `dotnet test --filter ChampPoolViewModelTests`; Expected: FAIL (types undefined).

- [ ] **Step 3: Create `Sparkline.cs`** (thin `FrameworkElement`; no axes):

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace RiftReview.App.Controls;

// Minimal gold polyline over a value series. Null values are skipped (gaps).
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points), typeof(IReadOnlyList<int?>), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<int?>? Points
    {
        get => (IReadOnlyList<int?>?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    private static readonly Pen GoldPen = MakePen();
    private static Pen MakePen()
    {
        var p = new Pen(new SolidColorBrush(Color.FromRgb(0xC8, 0xAA, 0x6E)), 1.5);
        p.Freeze();
        return p;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var pts = Points?.Where(v => v.HasValue).Select(v => (double)v!.Value).ToList();
        if (pts is null || pts.Count < 2) return;
        double w = ActualWidth, h = ActualHeight;
        double min = pts.Min(), max = pts.Max(), range = max - min < 1e-6 ? 1 : max - min;
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            for (int i = 0; i < pts.Count; i++)
            {
                double x = pts.Count == 1 ? 0 : i * (w / (pts.Count - 1));
                double y = h - ((pts[i] - min) / range) * h;
                if (i == 0) g.BeginFigure(new Point(x, y), false, false);
                else g.LineTo(new Point(x, y), true, false);
            }
        }
        geo.Freeze();
        dc.DrawGeometry(null, GoldPen, geo);
    }
}
```

- [ ] **Step 4: Create the row + card VMs**

`ChampRowViewModel.cs`:

```csharp
using RiftReview.Core.Analysis;

namespace RiftReview.App.ViewModels;

public sealed class ChampRowViewModel
{
    private readonly ChampStat _s;
    public ChampRowViewModel(ChampStat s, string name) { _s = s; ChampionName = name; }
    public int ChampionId => _s.ChampionId;
    public string ChampionName { get; }
    public int Games => _s.Games;
    public string WinRate => $"{_s.WinRate * 100:0}%";
    public string Record => $"{_s.Wins}W {_s.Losses}L";
    public bool Winning => _s.WinRate >= 0.5;
    public string Kda => _s.Kda.ToString("0.0");
    public string Cs10 => _s.AvgCs10 is null ? "—" : _s.AvgCs10.Value.ToString("0");
}
```

`ChampCardViewModel.cs`:

```csharp
using System.Collections.Generic;
using RiftReview.Core.Analysis;

namespace RiftReview.App.ViewModels;

public sealed class ChampCardViewModel
{
    private readonly ChampStat _s;
    public ChampCardViewModel(ChampStat s, string name) { _s = s; ChampionName = name; }
    public int ChampionId => _s.ChampionId;
    public string ChampionName { get; }
    public string WinRate => $"{_s.WinRate * 100:0}%";
    public bool Winning => _s.WinRate >= 0.5;
    public string Subtitle => $"{_s.Games} games · mid";
    public string Kda => $"KDA {_s.Kda:0.0}";
    public string Cs10 => _s.AvgCs10 is null ? "CS@10 —" : $"CS@10 {_s.AvgCs10.Value:0}";
    public string Deaths => $"Dth {_s.AvgDeaths:0.0}";
    public IReadOnlyList<int?> Trend => _s.TrendCs10;
}
```

- [ ] **Step 5: Create `ChampPoolViewModel.cs`**

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using RiftReview.Core.Analysis;
using RiftReview.Core.Configuration;
using RiftReview.Core.Data;
using RiftReview.Core.DataDragon;

namespace RiftReview.App.ViewModels;

public sealed partial class ChampPoolViewModel : ObservableObject
{
    private readonly RiftReviewDb _db;
    private readonly DataDragonClient _ddragon;
    private readonly SettingsStore _settings;

    public ChampPoolViewModel(RiftReviewDb db, DataDragonClient ddragon, SettingsStore settings)
    {
        _db = db; _ddragon = ddragon; _settings = settings;
        _rankedOnly = settings.DefaultRankedOnly;
    }

    public ObservableCollection<ChampCardViewModel> Practicing { get; } = new();
    public ObservableCollection<ChampRowViewModel> AllChampions { get; } = new();

    [ObservableProperty] private bool _rankedOnly;
    public bool HasPracticing => Practicing.Count > 0;
    public bool IsEmpty => AllChampions.Count == 0;

    public async Task InitializeAsync()
    {
        try { await _ddragon.EnsureLoadedAsync(); } catch { /* names fall back to placeholders */ }
        Load();
    }

    public void Load()
    {
        var pool = ChampPoolCalculator.Build(_db.AllMatches(RankedOnly));
        AllChampions.Clear();
        foreach (var c in pool.All)
            AllChampions.Add(new ChampRowViewModel(c, _ddragon.ChampionName(c.ChampionId)));
        Practicing.Clear();
        foreach (var id in pool.PracticingChampionIds)
        {
            var stat = pool.All.First(c => c.ChampionId == id);
            Practicing.Add(new ChampCardViewModel(stat, _ddragon.ChampionName(id)));
        }
        OnPropertyChanged(nameof(HasPracticing));
        OnPropertyChanged(nameof(IsEmpty));
    }

    partial void OnRankedOnlyChanged(bool value) => Load();
}
```

- [ ] **Step 6: Run the VM test to verify pass** — Run: `dotnet test --filter ChampPoolViewModelTests`; Expected: PASS.

- [ ] **Step 7: Create `ChampPoolView.xaml`** — a `UserControl` constructed with the VM (DI), calling `InitializeAsync` on `Loaded` (mirror `ReviewView`). Representative XAML using existing brushes; "Currently practicing" cards bind `Practicing` (each card: monogram/name, `WinRate` colored by `Winning`, a `<controls:Sparkline Points="{Binding Trend}" Height="38"/>`, and the `Kda`/`Cs10`/`Deaths` line), "All champions" binds `AllChampions` in a `ListView`/`GridView` (or styled `ItemsControl`) with columns Champion / Games / WinRate / Kda / Cs10, plus a `RankedOnly` toggle bound two-way. Code-behind:

```csharp
using System.Windows.Controls;
using RiftReview.App.ViewModels;

namespace RiftReview.App.Views;

public partial class ChampPoolView : UserControl
{
    public ChampPoolView(ChampPoolViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Loaded += async (_, _) => await vm.InitializeAsync();
    }
}
```

- [ ] **Step 8: Register VMs/View in DI** — in `App.xaml.cs`: `s.AddTransient<ChampPoolViewModel>();` and `s.AddTransient<Views.ChampPoolView>();` (and the placeholder created in Task 8 is now replaced).

- [ ] **Step 9: Build + test** — Run: `dotnet build RiftReview.slnx` then `dotnet test`; Expected: clean + green.

- [ ] **Step 10: Commit**

```bash
git add src/RiftReview.App/Controls/Sparkline.cs src/RiftReview.App/ViewModels/Champ*.cs src/RiftReview.App/Views/ChampPoolView.xaml src/RiftReview.App/Views/ChampPoolView.xaml.cs src/RiftReview.App/App.xaml.cs tests/RiftReview.App.Tests/ChampPoolViewModelTests.cs
git commit -m "feat(app): champion-pool dashboard (practice cards + table + sparkline)"
```

---

## Task 10: Settings page

**Files:**
- Create: `src/RiftReview.App/ViewModels/SettingsViewModel.cs`
- Create: `src/RiftReview.App/Views/SettingsView.xaml` (+ `.xaml.cs`)
- Modify: `src/RiftReview.App/App.xaml.cs` (register)
- Test: `tests/RiftReview.App.Tests/SettingsViewModelTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using RiftReview.App.ViewModels;
using RiftReview.Core.Configuration;
using RiftReview.Core.Data;
using Xunit;

public class SettingsViewModelTests
{
    [Fact]
    public void Editing_persists_to_store()
    {
        using var db = RiftReviewDb.Open("Data Source=:memory:");
        var store = new SettingsStore(db);
        var vm = new SettingsViewModel(store);
        Assert.Equal(150, vm.MatchDepth);

        vm.MatchDepth = 250;
        vm.DefaultRankedOnly = false;

        Assert.Equal(250, new SettingsStore(db).MatchDepth);
        Assert.False(new SettingsStore(db).DefaultRankedOnly);
    }
}
```

- [ ] **Step 2: Run to verify failure** — Run: `dotnet test --filter SettingsViewModelTests`; Expected: FAIL.

- [ ] **Step 3: Implement `SettingsViewModel.cs`**

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using RiftReview.Core.Configuration;

namespace RiftReview.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsStore _store;
    public SettingsViewModel(SettingsStore store)
    {
        _store = store;
        _matchDepth = store.MatchDepth;
        _defaultRankedOnly = store.DefaultRankedOnly;
    }

    [ObservableProperty] private int _matchDepth;
    [ObservableProperty] private bool _defaultRankedOnly;

    public int MinDepth => SettingsStore.MinDepth;
    public int MaxDepth => SettingsStore.MaxDepth;

    partial void OnMatchDepthChanged(int value)
    {
        _store.MatchDepth = value;               // clamps + persists
        if (_store.MatchDepth != value) { _matchDepth = _store.MatchDepth; OnPropertyChanged(nameof(MatchDepth)); }
    }
    partial void OnDefaultRankedOnlyChanged(bool value) => _store.DefaultRankedOnly = value;
}
```

- [ ] **Step 4: Run to verify pass** — Run: `dotnet test --filter SettingsViewModelTests`; Expected: PASS.

- [ ] **Step 5: Create `SettingsView.xaml`** — `UserControl` (DI-constructed with `SettingsViewModel`): a label + numeric input (`ui:NumberBox` or a `Slider` bound to `MatchDepth`, range `MinDepth`..`MaxDepth`) for match depth, and a toggle for `DefaultRankedOnly`, on a `PanelBgBrush` surface with `TextPrimaryBrush`/`TextMutedBrush`. Code-behind sets `DataContext = vm` (no async load needed).

- [ ] **Step 6: Register in DI** — `s.AddTransient<SettingsViewModel>(); s.AddTransient<Views.SettingsView>();`

- [ ] **Step 7: Build + test** — Run: `dotnet build RiftReview.slnx` then `dotnet test`; Expected: clean + green.

- [ ] **Step 8: Commit**

```bash
git add src/RiftReview.App/ViewModels/SettingsViewModel.cs src/RiftReview.App/Views/SettingsView.xaml src/RiftReview.App/Views/SettingsView.xaml.cs src/RiftReview.App/App.xaml.cs tests/RiftReview.App.Tests/SettingsViewModelTests.cs
git commit -m "feat(app): Settings page (match depth, default filter)"
```

---

## Task 11: Demo seeder — multi-champ pool

**Files:**
- Modify: `src/RiftReview.App/Demo/DemoSeeder.cs`

The Champions dashboard needs concentration (multiple games on a few champs) so practice detection fires and aggregates are meaningful.

- [ ] **Step 1: Change the seed shape** — keep the per-game `BuildGame` machinery, but drive champ selection so the dataset concentrates on two "practice" champs plus a few one-offs. Replace the `for (int i = 0; i < 8; i++)` loop with ~24 games whose champion is chosen by a fixed plan:

```csharp
        // Concentrated demo pool: champ 103 (x12), champ 157 (x8), then 4 one-offs — mirrors a two-champ grind.
        int[] plan =
        {
            103,103,103,103,103,103,103,103,103,103,103,103,
            157,157,157,157,157,157,157,157,
            7, 238, 99, 142
        };
        for (int i = 0; i < plan.Length; i++)
        {
            var (match, tl) = BuildGame(i, baseCreation - i * 86_400_000L, plan[i]);
            var s = MatchExtractor.Summarize(match, "ME");
            var cs10 = TimelineExtractor.CsAtMinute(tl, s.MyParticipantId, 10);
            var g15 = TimelineExtractor.GoldDiffAtMinute(tl, s.MyParticipantId, s.OpponentParticipantId, 15);
            var row = new MatchRow(match.Metadata.MatchId, s.QueueId, s.GameStartUtc, s.DurationS, s.Patch,
                s.MyChampionId, s.MyTeamPosition, s.Win, s.Kills, s.Deaths, s.Assists, s.Cs,
                cs10, g15, s.OpponentParticipantId, s.OpponentChampionId, s.GameStartUtc);
            db.UpsertMatch(row, JsonSerializer.Serialize(match, Json), JsonSerializer.Serialize(tl, Json));
        }
```

Change `BuildGame(int i, long gameCreation)` to `BuildGame(int i, long gameCreation, int myChamp)` and delete the internal `int myChamp = MidChamps[i % MidChamps.Length];` line (use the parameter). Keep everything else (the unique `NA1_DEMO_{i}` ids keep games distinct).

- [ ] **Step 2: Build + smoke under demo** — Run: `dotnet build RiftReview.slnx -c Debug`; then optionally launch `RiftReview.App.exe --seed-demo` to eyeball. Expected: clean build; Champions shows 103 + 157 as practice cards and ~6 rows total.

- [ ] **Step 3: Commit**

```bash
git add src/RiftReview.App/Demo/DemoSeeder.cs
git commit -m "feat(app): demo seeder seeds a concentrated multi-champ pool"
```

---

## Task 12: Screenshot verification gate (real data + demo)

**Files:** none (verification only).

- [ ] **Step 1: Build Debug** — Run: `dotnet build RiftReview.slnx -c Debug`; Expected: clean.

- [ ] **Step 2: Verify on the populated real DB** — the owner's `%LOCALAPPDATA%\RiftReview\riftreview.db` already holds real matches. Launch `RiftReview.App.exe` (no `--seed-demo`), navigate to each page, and **inside a Sonnet subagent** capture + judge (PrintWindow flag 2; return a TEXT verdict + PNG paths — do NOT load PNGs into the main context). Verify:
  - Nav rail shows Review / Champions / Settings; switching works.
  - **Champions:** practice cards for K'Sante/Galio with gold CS@10 sparklines + WR/KDA/deaths; all-champs table populated with real names; Ranked/All toggle filters (Arena rows show "—" CS@10 in All).
  - **Settings:** match-depth control (default 150) + ranked toggle render and are editable.
  - **Review:** unchanged from v1 (rail + trend + deep-dive).
  - No crash / empty-state-with-data / error banner.

- [ ] **Step 3: Verify under `--seed-demo`** — relaunch with `--seed-demo`; confirm Champions renders the concentrated demo pool (103/157 cards + table). Same subagent text-verdict method.

- [ ] **Step 4: Holistic review + commit (if any fixes)** — run a final spec-coverage pass; fix any defects in their own commits. No code change → no commit.

---

## Acceptance criteria (from spec §13)

- `dotnet build RiftReview.slnx` clean; `dotnet test` all green.
- Sync pulls up to the configured depth (default 150), paginated, resumable; existing matches skipped.
- Champions page shows auto-detected practice cards (CS@10 sparkline + WR/KDA/deaths) and a sortable all-champs table honoring Ranked/All; renders on real data + demo.
- Settings changes match depth + default filter and persist across launches.
- Each sync appends an `lp_snapshots` row when LP is fetchable; LP failure never breaks match sync.
- Schema at v2; a v1 DB upgrades in place without data loss.
- No secrets added; appsettings.json still placeholders only.

## Hand-back (cannot verify here — owner runs locally)

- Live LEAGUE-V4 LP pull with a real key (confirm a real standing is fetched + a snapshot row written).
- A full ~150-game live backfill end-to-end (time + dev-key-expiry resume behavior).
