# RiftReview v1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a single-user Windows desktop app that pulls my own League match history from the Riot API into local SQLite and shows a post-game review (single-game deep-dive + cross-game trend strip).

**Architecture:** Two-project .NET 10 solution — `RiftReview.Core` (Riot client, Data Dragon, rate limiter, SQLite store, sync + analysis; no WPF) and `RiftReview.App` (WPF + WPF-UI MVVM, hand-rolled charts). The expensive/risky logic (extraction math, routing, rate limiting, sync) lives in Core and is covered by offline fixture-driven xUnit tests. The live Riot pull is the only thing not verifiable in CI; the app ships a `--seed-demo` flag so the UI renders from a synthetic fixture without a key.

**Tech Stack:** .NET 10 (`net10.0-windows`), WPF, WPF-UI 4.3, CommunityToolkit.Mvvm, Microsoft.Data.Sqlite, Microsoft.Extensions.Hosting, System.Text.Json, xUnit.

**Source spec:** `docs/superpowers/specs/2026-06-19-riftreview-v1-design.md` — read it first.

---

## Build-time verification gates (READ BEFORE CODING)

These facts MUST be confirmed against live docs during execution. Do not trust this plan's representative shapes blindly — Riot is mid-migration from `summonerId` to `puuid`.

1. **Riot endpoint paths + JSON field names** — confirm at developer.riotgames.com:
   - ACCOUNT-V1 `by-riot-id/{gameName}/{tagLine}` → `{ puuid, gameName, tagLine }`.
   - MATCH-V5 `matches/by-puuid/{puuid}/ids`, `matches/{matchId}`, `matches/{matchId}/timeline`.
   - SUMMONER-V4 `summoners/by-puuid/{puuid}` (optional in v1).
   - Match `info.participants[]` field names: `puuid, championId, teamId, teamPosition, win, kills, deaths, assists, totalMinionsKilled, neutralMinionsKilled, participantId`; `info.queueId, gameCreation, gameDuration, gameVersion`.
   - Timeline `info.frames[].participantFrames["1".."10"]`: `totalGold, minionsKilled, jungleMinionsKilled, participantId`; `info.frames[].events[]`: `type ("CHAMPION_KILL"), timestamp, killerId, victimId`; `info.frameInterval`; `metadata.participants` (ordered puuids; index i ↔ participantId i+1).
   - **`gameDuration` units quirk:** historically ms vs seconds across patches — confirm and normalize to seconds.
   If a field name differs, adjust the DTO `[JsonPropertyName]` only; the extraction math is unaffected.

2. **Data Dragon (no key needed, stable):**
   - `https://ddragon.leagueoflegends.com/api/versions.json` → `["15.x.x", ...]` (index 0 = latest).
   - `.../cdn/{ver}/data/en_US/champion.json` → `data[name].key` = numeric championId (string), `data[name].id` = icon basename.
   - Champion icon: `.../cdn/{ver}/img/champion/{id}.png`.

3. **WPF-UI 4.3 API** — confirm `ApplicationAccentColorManager.Apply(...)` signature and `ApplicationThemeManager.Apply(...)` against the installed package (VideoShelf uses the same version — copy its working startup pattern from `C:\Agent Projects\VideoShelf\src\VideoShelf.App\App.xaml.cs`).

4. **Queue IDs:** Ranked Solo = 420, Ranked Flex = 440 (confirm via static-data/queues.json or Riot constants).

---

## File structure

```
RiftReview/
  RiftReview.slnx
  src/
    RiftReview.Core/
      RiftReview.Core.csproj
      Configuration/RiotOptions.cs
      Riot/RiotRouting.cs              # platform<->regional map
      Riot/ISystemClock.cs
      Riot/RiotRateLimiter.cs
      Riot/RiotApiException.cs
      Riot/RiotApiClient.cs            # typed HttpClient, routing split
      Riot/Dtos/AccountDtos.cs
      Riot/Dtos/MatchDtos.cs
      Riot/Dtos/TimelineDtos.cs
      Riot/Dtos/SummonerDtos.cs
      DataDragon/DataDragonClient.cs
      Data/RiftReviewDb.cs             # schema + migrations + CRUD
      Data/MatchRow.cs
      Analysis/MatchExtractor.cs       # match detail -> scalars + opponent
      Analysis/TimelineExtractor.cs    # timeline -> series + scalars + deaths
      Analysis/AnalysisModels.cs       # ChartPoint, GoldDiff, DeepDiveResult...
      Analysis/BaselineCalculator.cs   # same-role rolling baseline
      Sync/SyncService.cs
      Sync/SyncProgress.cs
    RiftReview.App/
      RiftReview.App.csproj
      appsettings.json                 # placeholders only
      App.xaml / App.xaml.cs           # Host, DI, theme, --seed-demo
      Themes/Colors.xaml               # black-glass + Hextech Gold
      Controls/LineChart.cs            # hand-rolled chart control
      ViewModels/MainViewModel.cs
      ViewModels/MatchListItemViewModel.cs
      ViewModels/DeepDiveViewModel.cs
      ViewModels/TrendStripViewModel.cs
      Views/MainWindow.xaml(.cs)
      Views/DeepDiveView.xaml(.cs)
      Views/TrendStripView.xaml(.cs)
      Demo/DemoSeeder.cs               # loads embedded fixture into DB
  tests/
    RiftReview.Core.Tests/
      RiftReview.Core.Tests.csproj
      Fixtures/sample_match.json
      Fixtures/sample_timeline.json
      Fixtures/FixtureLoader.cs
      StubHttpMessageHandler.cs
      FakeClock.cs
      RiotRoutingTests.cs
      RiotRateLimiterTests.cs
      RiotApiClientTests.cs
      DataDragonClientTests.cs
      MatchExtractorTests.cs
      TimelineExtractorTests.cs
      BaselineCalculatorTests.cs
      RiftReviewDbTests.cs
      SyncServiceTests.cs
    RiftReview.App.Tests/
      RiftReview.App.Tests.csproj
      LineChartTests.cs                # geometry/scaling pure functions
      TrendStripViewModelTests.cs
  docs/superpowers/...
```

---

## Conventions

- TDD: write the failing test, run it red, implement, run it green, commit.
- Tests run with `dotnet test`. Single test: `dotnet test --filter "FullyQualifiedName~ClassName.MethodName"`.
- Commit after each green task. Commit author is the repo default (`yovanmc <yovanmc@users.noreply.github.com>`); use plain `git commit`. Append `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.
- `System.Text.Json` with `PropertyNameCaseInsensitive = true` for all Riot DTOs.
- All times stored as Unix epoch **seconds** (INTEGER) in SQLite.

---

## Task 1: Solution + project scaffold

**Files:**
- Create: `RiftReview.slnx`, the four `.csproj` files, `src/RiftReview.Core/Class1.cs` placeholder removed.

- [ ] **Step 1: Create projects and solution**

```bash
cd "C:/Agent Projects/RiftReview"
dotnet new classlib -n RiftReview.Core -o src/RiftReview.Core -f net10.0-windows
dotnet new wpf      -n RiftReview.App  -o src/RiftReview.App  -f net10.0-windows
dotnet new xunit    -n RiftReview.Core.Tests -o tests/RiftReview.Core.Tests -f net10.0-windows
dotnet new xunit    -n RiftReview.App.Tests  -o tests/RiftReview.App.Tests  -f net10.0-windows
rm -f src/RiftReview.Core/Class1.cs
dotnet new sln -n RiftReview --format slnx
dotnet sln RiftReview.slnx add src/RiftReview.Core src/RiftReview.App tests/RiftReview.Core.Tests tests/RiftReview.App.Tests
```

If `--format slnx` is unsupported by the installed SDK, drop it (a classic `.sln` is acceptable); VideoShelf uses `.slnx` on this machine so it should work.

- [ ] **Step 2: Set csproj contents**

`src/RiftReview.Core/RiftReview.Core.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Data.Sqlite" Version="10.0.0" />
  </ItemGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="RiftReview.Core.Tests" />
  </ItemGroup>
</Project>
```

`src/RiftReview.App/RiftReview.App.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
    <LangVersion>latest</LangVersion>
    <RuntimeIdentifiers>win-x64</RuntimeIdentifiers>
    <UserSecretsId>riftreview-dev-secrets</UserSecretsId>
    <Version>0.1.0</Version>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\RiftReview.Core\RiftReview.Core.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.2" />
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="10.0.8" />
    <PackageReference Include="Microsoft.Extensions.Http" Version="10.0.8" />
    <PackageReference Include="Microsoft.Extensions.Configuration.UserSecrets" Version="10.0.8" />
    <PackageReference Include="WPF-UI" Version="4.3.0" />
  </ItemGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="RiftReview.App.Tests" />
    <None Update="appsettings.json"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></None>
  </ItemGroup>
</Project>
```

Add `<ProjectReference>` to `RiftReview.Core` in both test csprojs, and a `ProjectReference` to `RiftReview.App` in `RiftReview.App.Tests`. Confirm package versions resolve (they match VideoShelf); if a version is unavailable, use the nearest available 10.x / matching minor and note it.

- [ ] **Step 3: Build**

Run: `dotnet build RiftReview.slnx`
Expected: build succeeds (the default WPF `MainWindow` is fine for now).

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "scaffold: RiftReview solution, Core/App + test projects"
```

---

## Task 2: SQLite store — schema, migrations, CRUD (TDD)

**Files:**
- Create: `src/RiftReview.Core/Data/RiftReviewDb.cs`, `src/RiftReview.Core/Data/MatchRow.cs`
- Test: `tests/RiftReview.Core.Tests/RiftReviewDbTests.cs`

`MatchRow.cs` (plain record matching the `matches` table):

```csharp
namespace RiftReview.Core.Data;

public sealed record MatchRow(
    string MatchId, int QueueId, long GameStartUtc, int DurationS, string Patch,
    int MyChampionId, string MyTeamPosition, bool Win,
    int Kills, int Deaths, int Assists, int Cs,
    int? CsAt10, int? GoldDiffAt15,
    int? OpponentParticipantId, int? OpponentChampionId, long SyncedAt);
```

- [ ] **Step 1: Failing test — schema init + round-trip**

```csharp
using Microsoft.Data.Sqlite;
using RiftReview.Core.Data;
using Xunit;

public class RiftReviewDbTests
{
    private static RiftReviewDb NewDb() => RiftReviewDb.Open("Data Source=:memory:;Cache=Shared");

    [Fact]
    public void Initialize_sets_user_version_and_creates_tables()
    {
        using var db = NewDb();
        Assert.Equal(RiftReviewDb.LatestSchemaVersion, db.GetSchemaVersion());
        Assert.False(db.HasMatch("NA1_1"));
    }

    [Fact]
    public void Upsert_and_get_match_roundtrips()
    {
        using var db = NewDb();
        var row = new MatchRow("NA1_1", 420, 1_700_000_000, 1800, "15.12.1",
            103, "MIDDLE", true, 8, 3, 11, 210, 75, 412, 7, 157, 1_700_000_100);
        db.UpsertMatch(row, "{\"m\":1}", "{\"t\":1}");
        Assert.True(db.HasMatch("NA1_1"));
        var got = db.GetMatch("NA1_1")!;
        Assert.Equal(row, got);
        Assert.Equal("{\"t\":1}", db.GetTimelineJson("NA1_1"));
    }

    [Fact]
    public void RecentMatches_filters_by_ranked_and_orders_desc()
    {
        using var db = NewDb();
        db.UpsertMatch(Row("NA1_1", 420, start: 100), "{}", "{}");   // ranked solo
        db.UpsertMatch(Row("NA1_2", 400, start: 200), "{}", "{}");   // normal draft
        db.UpsertMatch(Row("NA1_3", 440, start: 300), "{}", "{}");   // ranked flex
        var ranked = db.RecentMatches(rankedOnly: true, limit: 20);
        Assert.Equal(new[] { "NA1_3", "NA1_1" }, ranked.Select(m => m.MatchId).ToArray());
        Assert.Equal(3, db.RecentMatches(rankedOnly: false, limit: 20).Count);
    }

    private static MatchRow Row(string id, int queue, long start) =>
        new(id, queue, start, 1800, "15.12.1", 103, "MIDDLE", true, 1, 1, 1, 100, 60, 0, 7, 1, start);
}
```

- [ ] **Step 2: Run red**

Run: `dotnet test tests/RiftReview.Core.Tests --filter "FullyQualifiedName~RiftReviewDbTests"`
Expected: FAIL (RiftReviewDb does not exist).

- [ ] **Step 3: Implement `RiftReviewDb`**

```csharp
using Microsoft.Data.Sqlite;

namespace RiftReview.Core.Data;

public sealed class RiftReviewDb : IDisposable
{
    public const int LatestSchemaVersion = 1;
    private readonly SqliteConnection _conn;

    private RiftReviewDb(SqliteConnection conn) => _conn = conn;

    public static RiftReviewDb Open(string connectionString)
    {
        var conn = new SqliteConnection(connectionString);
        conn.Open();
        Exec(conn, "PRAGMA foreign_keys=ON;");
        var db = new RiftReviewDb(conn);
        db.RunVersionedMigrations();
        return db;
    }

    public int GetSchemaVersion()
    {
        using var c = _conn.CreateCommand();
        c.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(c.ExecuteScalar());
    }

    private void RunVersionedMigrations()
    {
        var v = GetSchemaVersion();
        if (v < 1)
        {
            Exec(_conn, Schema);
            Exec(_conn, "PRAGMA user_version=1;");
        }
        // future: if (v < 2) { ... PRAGMA user_version=2; }
    }

    private const string Schema = @"
CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT);
CREATE TABLE IF NOT EXISTS matches (
  match_id TEXT PRIMARY KEY,
  queue_id INTEGER NOT NULL,
  game_start_utc INTEGER NOT NULL,
  duration_s INTEGER NOT NULL,
  patch TEXT NOT NULL,
  my_champion_id INTEGER NOT NULL,
  my_team_position TEXT NOT NULL,
  win INTEGER NOT NULL,
  kills INTEGER NOT NULL, deaths INTEGER NOT NULL, assists INTEGER NOT NULL,
  cs INTEGER NOT NULL,
  cs_at_10 INTEGER, gold_diff_at_15 INTEGER,
  opponent_participant_id INTEGER, opponent_champion_id INTEGER,
  synced_at INTEGER NOT NULL
);
CREATE TABLE IF NOT EXISTS match_detail (
  match_id TEXT PRIMARY KEY REFERENCES matches(match_id) ON DELETE CASCADE,
  match_json TEXT NOT NULL,
  timeline_json TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_matches_start ON matches(game_start_utc DESC);
CREATE INDEX IF NOT EXISTS ix_matches_queue ON matches(queue_id);";

    public bool HasMatch(string id) => Scalar<long>("SELECT COUNT(1) FROM matches WHERE match_id=$id", ("$id", id)) > 0;

    public void UpsertMatch(MatchRow m, string matchJson, string timelineJson)
    {
        using var tx = _conn.BeginTransaction();
        using (var c = _conn.CreateCommand())
        {
            c.Transaction = tx;
            c.CommandText = @"INSERT INTO matches
              (match_id,queue_id,game_start_utc,duration_s,patch,my_champion_id,my_team_position,win,
               kills,deaths,assists,cs,cs_at_10,gold_diff_at_15,opponent_participant_id,opponent_champion_id,synced_at)
              VALUES ($id,$q,$gs,$d,$p,$champ,$pos,$win,$k,$de,$a,$cs,$cs10,$g15,$opid,$ochamp,$sync)
              ON CONFLICT(match_id) DO UPDATE SET
               queue_id=$q,game_start_utc=$gs,duration_s=$d,patch=$p,my_champion_id=$champ,my_team_position=$pos,
               win=$win,kills=$k,deaths=$de,assists=$a,cs=$cs,cs_at_10=$cs10,gold_diff_at_15=$g15,
               opponent_participant_id=$opid,opponent_champion_id=$ochamp,synced_at=$sync;";
            Bind(c, "$id", m.MatchId); Bind(c, "$q", m.QueueId); Bind(c, "$gs", m.GameStartUtc);
            Bind(c, "$d", m.DurationS); Bind(c, "$p", m.Patch); Bind(c, "$champ", m.MyChampionId);
            Bind(c, "$pos", m.MyTeamPosition); Bind(c, "$win", m.Win ? 1 : 0);
            Bind(c, "$k", m.Kills); Bind(c, "$de", m.Deaths); Bind(c, "$a", m.Assists); Bind(c, "$cs", m.Cs);
            Bind(c, "$cs10", (object?)m.CsAt10 ?? DBNull.Value); Bind(c, "$g15", (object?)m.GoldDiffAt15 ?? DBNull.Value);
            Bind(c, "$opid", (object?)m.OpponentParticipantId ?? DBNull.Value);
            Bind(c, "$ochamp", (object?)m.OpponentChampionId ?? DBNull.Value); Bind(c, "$sync", m.SyncedAt);
            c.ExecuteNonQuery();
        }
        using (var c = _conn.CreateCommand())
        {
            c.Transaction = tx;
            c.CommandText = @"INSERT INTO match_detail(match_id,match_json,timeline_json) VALUES($id,$m,$t)
              ON CONFLICT(match_id) DO UPDATE SET match_json=$m, timeline_json=$t;";
            Bind(c, "$id", m.MatchId); Bind(c, "$m", matchJson); Bind(c, "$t", timelineJson);
            c.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public MatchRow? GetMatch(string id)
    {
        using var c = _conn.CreateCommand();
        c.CommandText = "SELECT * FROM matches WHERE match_id=$id";
        Bind(c, "$id", id);
        using var r = c.ExecuteReader();
        return r.Read() ? ReadRow(r) : null;
    }

    public string? GetTimelineJson(string id) =>
        ScalarString("SELECT timeline_json FROM match_detail WHERE match_id=$id", ("$id", id));
    public string? GetMatchJson(string id) =>
        ScalarString("SELECT match_json FROM match_detail WHERE match_id=$id", ("$id", id));

    public IReadOnlyList<MatchRow> RecentMatches(bool rankedOnly, int limit)
    {
        using var c = _conn.CreateCommand();
        c.CommandText = "SELECT * FROM matches" +
            (rankedOnly ? " WHERE queue_id IN (420,440)" : "") +
            " ORDER BY game_start_utc DESC LIMIT $lim";
        Bind(c, "$lim", limit);
        var list = new List<MatchRow>();
        using var r = c.ExecuteReader();
        while (r.Read()) list.Add(ReadRow(r));
        return list;
    }

    public string? GetMeta(string key) => ScalarString("SELECT value FROM meta WHERE key=$k", ("$k", key));
    public void SetMeta(string key, string value)
    {
        using var c = _conn.CreateCommand();
        c.CommandText = "INSERT INTO meta(key,value) VALUES($k,$v) ON CONFLICT(key) DO UPDATE SET value=$v;";
        Bind(c, "$k", key); Bind(c, "$v", value); c.ExecuteNonQuery();
    }

    private static MatchRow ReadRow(SqliteDataReader r) => new(
        r.GetString(r.GetOrdinal("match_id")), r.GetInt32(r.GetOrdinal("queue_id")),
        r.GetInt64(r.GetOrdinal("game_start_utc")), r.GetInt32(r.GetOrdinal("duration_s")),
        r.GetString(r.GetOrdinal("patch")), r.GetInt32(r.GetOrdinal("my_champion_id")),
        r.GetString(r.GetOrdinal("my_team_position")), r.GetInt32(r.GetOrdinal("win")) != 0,
        r.GetInt32(r.GetOrdinal("kills")), r.GetInt32(r.GetOrdinal("deaths")), r.GetInt32(r.GetOrdinal("assists")),
        r.GetInt32(r.GetOrdinal("cs")), GetNullableInt(r, "cs_at_10"), GetNullableInt(r, "gold_diff_at_15"),
        GetNullableInt(r, "opponent_participant_id"), GetNullableInt(r, "opponent_champion_id"),
        r.GetInt64(r.GetOrdinal("synced_at")));

    private static int? GetNullableInt(SqliteDataReader r, string col)
    { var o = r.GetOrdinal(col); return r.IsDBNull(o) ? null : r.GetInt32(o); }

    private T Scalar<T>(string sql, params (string, object)[] ps)
    { using var c = _conn.CreateCommand(); c.CommandText = sql; foreach (var (n, v) in ps) Bind(c, n, v);
      var o = c.ExecuteScalar(); return (T)Convert.ChangeType(o!, typeof(T)); }
    private string? ScalarString(string sql, params (string, object)[] ps)
    { using var c = _conn.CreateCommand(); c.CommandText = sql; foreach (var (n, v) in ps) Bind(c, n, v);
      var o = c.ExecuteScalar(); return o == null || o is DBNull ? null : (string)o; }

    private static void Bind(SqliteCommand c, string name, object value)
    { var p = c.CreateParameter(); p.ParameterName = name; p.Value = value; c.Parameters.Add(p); }
    private static void Exec(SqliteConnection conn, string sql)
    { using var c = conn.CreateCommand(); c.CommandText = sql; c.ExecuteNonQuery(); }

    public void Dispose() => _conn.Dispose();
}
```

Note: `:memory:;Cache=Shared` keeps one in-memory DB alive for the connection's lifetime. For the app, use a file path under `%LOCALAPPDATA%\RiftReview\riftreview.db`.

- [ ] **Step 4: Run green**

Run: `dotnet test tests/RiftReview.Core.Tests --filter "FullyQualifiedName~RiftReviewDbTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit** — `git commit -m "feat(core): SQLite store with versioned schema + match CRUD"`

---

## Task 3: Riot routing map (TDD)

**Files:** Create `src/RiftReview.Core/Riot/RiotRouting.cs`; Test `RiotRoutingTests.cs`.

- [ ] **Step 1: Failing test**

```csharp
using RiftReview.Core.Riot;
using Xunit;

public class RiotRoutingTests
{
    [Theory]
    [InlineData("na1", "americas")]
    [InlineData("br1", "americas")]
    [InlineData("euw1", "europe")]
    [InlineData("kr", "asia")]
    [InlineData("jp1", "asia")]
    public void RegionalFor_maps_platform_to_regional(string platform, string expected)
        => Assert.Equal(expected, RiotRouting.RegionalFor(platform));

    [Fact]
    public void RegionalFor_is_case_insensitive()
        => Assert.Equal("americas", RiotRouting.RegionalFor("NA1"));

    [Fact]
    public void RegionalFor_unknown_throws()
        => Assert.Throws<ArgumentException>(() => RiotRouting.RegionalFor("zz9"));
}
```

- [ ] **Step 2: Run red** → FAIL.

- [ ] **Step 3: Implement** (confirm the full platform set against Riot docs at build time)

```csharp
namespace RiftReview.Core.Riot;

public static class RiotRouting
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["na1"]="americas", ["br1"]="americas", ["la1"]="americas", ["la2"]="americas", ["oc1"]="americas",
        ["euw1"]="europe", ["eun1"]="europe", ["tr1"]="europe", ["ru"]="europe",
        ["kr"]="asia", ["jp1"]="asia",
    };

    public static string RegionalFor(string platform) =>
        Map.TryGetValue(platform, out var r) ? r
        : throw new ArgumentException($"Unknown platform '{platform}'", nameof(platform));

    public static string PlatformHost(string platform) => $"https://{platform.ToLowerInvariant()}.api.riotgames.com";
    public static string RegionalHost(string platform) => $"https://{RegionalFor(platform)}.api.riotgames.com";
}
```

- [ ] **Step 4: Run green** → PASS. **Step 5: Commit** — `feat(core): platform→regional routing map`.

---

## Task 4: Rate limiter with injected clock (TDD)

**Files:** Create `Riot/ISystemClock.cs`, `Riot/RiotRateLimiter.cs`; Test `FakeClock.cs`, `RiotRateLimiterTests.cs`.

Design: serializes Riot calls (concurrency 1 — fine for a personal tool), enforces a 1s/20 and 120s/100 sliding window, and honors `Retry-After`. A delay delegate is injected so tests advance a fake clock instead of sleeping.

- [ ] **Step 1: Helpers + failing test**

```csharp
using RiftReview.Core.Riot;
public sealed class FakeClock : ISystemClock
{ public DateTimeOffset UtcNow { get; set; } = new(2026,1,1,0,0,0,TimeSpan.Zero);
  public void Advance(TimeSpan d) => UtcNow += d; }
```

```csharp
using RiftReview.Core.Riot;
using Xunit;

public class RiotRateLimiterTests
{
    [Fact]
    public async Task Allows_first_20_without_delay_then_delays_21st()
    {
        var clock = new FakeClock();
        var totalDelay = TimeSpan.Zero;
        Func<TimeSpan, CancellationToken, Task> delay = (d, _) => { totalDelay += d; clock.Advance(d); return Task.CompletedTask; };
        var rl = new RiotRateLimiter(clock, delay);

        for (int i = 0; i < 20; i++) await rl.WaitForSlotAsync();
        Assert.Equal(TimeSpan.Zero, totalDelay);
        await rl.WaitForSlotAsync(); // 21st within the same second must wait
        Assert.True(totalDelay > TimeSpan.Zero);
    }

    [Fact]
    public async Task Honors_retry_after()
    {
        var clock = new FakeClock();
        var totalDelay = TimeSpan.Zero;
        Func<TimeSpan, CancellationToken, Task> delay = (d, _) => { totalDelay += d; clock.Advance(d); return Task.CompletedTask; };
        var rl = new RiotRateLimiter(clock, delay);
        rl.NotifyRetryAfter(TimeSpan.FromSeconds(5));
        await rl.WaitForSlotAsync();
        Assert.True(totalDelay >= TimeSpan.FromSeconds(5));
    }
}
```

- [ ] **Step 2: Run red** → FAIL.

- [ ] **Step 3: Implement**

```csharp
namespace RiftReview.Core.Riot;

public interface ISystemClock { DateTimeOffset UtcNow { get; } }
public sealed class SystemClock : ISystemClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }

public sealed class RiotRateLimiter
{
    private readonly ISystemClock _clock;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Queue<DateTimeOffset> _short = new();
    private readonly Queue<DateTimeOffset> _long = new();
    private DateTimeOffset _retryUntil = DateTimeOffset.MinValue;
    private static readonly TimeSpan ShortWindow = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan LongWindow = TimeSpan.FromSeconds(120);
    private const int ShortMax = 20, LongMax = 100;

    public RiotRateLimiter(ISystemClock clock, Func<TimeSpan, CancellationToken, Task>? delay = null)
    { _clock = clock; _delay = delay ?? ((d, ct) => Task.Delay(d, ct)); }

    public void NotifyRetryAfter(TimeSpan retryAfter) { var u = _clock.UtcNow + retryAfter; if (u > _retryUntil) _retryUntil = u; }

    public async Task WaitForSlotAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            while (true)
            {
                var now = _clock.UtcNow;
                if (now < _retryUntil) { await _delay(_retryUntil - now, ct); continue; }
                Trim(_short, now, ShortWindow); Trim(_long, now, LongWindow);
                if (_short.Count >= ShortMax) { await _delay(FreeIn(_short, now, ShortWindow), ct); continue; }
                if (_long.Count >= LongMax) { await _delay(FreeIn(_long, now, LongWindow), ct); continue; }
                _short.Enqueue(now); _long.Enqueue(now);
                return;
            }
        }
        finally { _gate.Release(); }
    }

    private static void Trim(Queue<DateTimeOffset> q, DateTimeOffset now, TimeSpan w)
    { while (q.Count > 0 && now - q.Peek() >= w) q.Dequeue(); }
    private static TimeSpan FreeIn(Queue<DateTimeOffset> q, DateTimeOffset now, TimeSpan w)
    { var wait = (q.Peek() + w) - now; return wait > TimeSpan.Zero ? wait : TimeSpan.FromMilliseconds(1); }
}
```

- [ ] **Step 4: Run green** → PASS. **Step 5: Commit** — `feat(core): sliding-window rate limiter with injected clock`.

---

## Task 5: Riot DTOs + API client (TDD against StubHttpMessageHandler)

> **VERIFY GATE:** confirm endpoint paths and field names (build-time gate #1) before trusting these DTOs. Adjust `[JsonPropertyName]` if names differ. Field renames do NOT change later extraction logic.

**Files:** Create `Riot/Dtos/*.cs`, `Riot/RiotApiException.cs`, `Riot/RiotApiClient.cs`; Test `StubHttpMessageHandler.cs`, `RiotApiClientTests.cs`.

- [ ] **Step 1: DTOs** (`AccountDtos.cs`, `MatchDtos.cs`, `TimelineDtos.cs`, `SummonerDtos.cs`)

```csharp
using System.Text.Json.Serialization;
namespace RiftReview.Core.Riot.Dtos;

// ACCOUNT-V1
public sealed record AccountDto(string Puuid, string GameName, string TagLine);

// MATCH-V5 match detail (subset we use)
public sealed record MatchDto(MatchMetadata Metadata, MatchInfo Info);
public sealed record MatchMetadata(string MatchId, List<string> Participants);
public sealed record MatchInfo(long QueueId, long GameCreation, long GameDuration, string GameVersion,
    List<ParticipantDto> Participants);
public sealed record ParticipantDto(
    string Puuid, int ParticipantId, int ChampionId, int TeamId, string TeamPosition, bool Win,
    int Kills, int Deaths, int Assists, int TotalMinionsKilled, int NeutralMinionsKilled);

// MATCH-V5 timeline (subset)
public sealed record TimelineDto(TimelineMetadata Metadata, TimelineInfo Info);
public sealed record TimelineMetadata(string MatchId, List<string> Participants);
public sealed record TimelineInfo(long FrameInterval, List<FrameDto> Frames);
public sealed record FrameDto(long Timestamp,
    Dictionary<string, ParticipantFrameDto> ParticipantFrames, List<EventDto> Events);
public sealed record ParticipantFrameDto(int ParticipantId, int TotalGold, int MinionsKilled, int JungleMinionsKilled);
public sealed record EventDto(string Type, long Timestamp, int? KillerId, int? VictimId);

// SUMMONER-V4 (optional)
public sealed record SummonerDto(string Puuid, long SummonerLevel, int ProfileIconId);
```

`RiotApiException.cs`:

```csharp
namespace RiftReview.Core.Riot;
public sealed class RiotApiException(int statusCode, string message) : Exception(message)
{ public int StatusCode { get; } = statusCode;
  public bool IsKeyProblem => StatusCode is 401 or 403;
  public bool IsRateLimited => StatusCode == 429;
  public bool IsNotFound => StatusCode == 404; }
```

- [ ] **Step 2: StubHttpMessageHandler + failing test**

```csharp
using System.Net;
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = new();
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    { Requests.Add(request); return Task.FromResult(_responder(request)); }
    public static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK)
        => new(code) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
}
```

```csharp
using System.Net;
using RiftReview.Core.Riot;
using Xunit;

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
```

- [ ] **Step 3: Run red** → FAIL.

- [ ] **Step 4: Implement `RiotApiClient`**

```csharp
using System.Net;
using System.Text.Json;
using RiftReview.Core.Riot.Dtos;

namespace RiftReview.Core.Riot;

public sealed class RiotApiClient
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _http;
    private readonly RiotRateLimiter _rl;
    private readonly string _apiKey;
    private readonly string _platform;
    private readonly string _regionalHost;
    private readonly string _platformHost;

    public RiotApiClient(HttpClient http, RiotRateLimiter rl, string apiKey, string platform)
    {
        _http = http; _rl = rl; _apiKey = apiKey; _platform = platform;
        _regionalHost = RiotRouting.RegionalHost(platform);
        _platformHost = RiotRouting.PlatformHost(platform);
    }

    public Task<AccountDto> ResolvePuuidAsync(string gameName, string tagLine, CancellationToken ct = default)
        => GetAsync<AccountDto>($"{_regionalHost}/riot/account/v1/accounts/by-riot-id/{Uri.EscapeDataString(gameName)}/{Uri.EscapeDataString(tagLine)}", ct);

    public Task<SummonerDto> GetSummonerByPuuidAsync(string puuid, CancellationToken ct = default)
        => GetAsync<SummonerDto>($"{_platformHost}/lol/summoner/v4/summoners/by-puuid/{puuid}", ct);

    public Task<List<string>> GetMatchIdsAsync(string puuid, int start, int count, CancellationToken ct = default)
        => GetAsync<List<string>>($"{_regionalHost}/lol/match/v5/matches/by-puuid/{puuid}/ids?start={start}&count={count}", ct);

    public Task<MatchDto> GetMatchAsync(string matchId, CancellationToken ct = default)
        => GetAsync<MatchDto>($"{_regionalHost}/lol/match/v5/matches/{matchId}", ct);

    public Task<TimelineDto> GetMatchTimelineAsync(string matchId, CancellationToken ct = default)
        => GetAsync<TimelineDto>($"{_regionalHost}/lol/match/v5/matches/{matchId}/timeline", ct);

    public string RawJsonLast { get; private set; } = "";

    public async Task<string> GetRawAsync(string url, CancellationToken ct = default)
    {
        await _rl.WaitForSlotAsync(ct);
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("X-Riot-Token", _apiKey);
        using var resp = await _http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (resp.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var ra = resp.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(
                double.TryParse(resp.Headers.TryGetValues("Retry-After", out var v) ? v.FirstOrDefault() : null, out var s) ? s : 1);
            _rl.NotifyRetryAfter(ra);
            throw new RiotApiException(429, $"Rate limited; retry after {ra.TotalSeconds:0}s");
        }
        if (!resp.IsSuccessStatusCode)
            throw new RiotApiException((int)resp.StatusCode, $"Riot API {(int)resp.StatusCode} for {url}: {body}");
        return body;
    }

    private async Task<T> GetAsync<T>(string url, CancellationToken ct)
    {
        var body = await GetRawAsync(url, ct);
        return JsonSerializer.Deserialize<T>(body, Json)
               ?? throw new RiotApiException(0, $"Empty/invalid JSON from {url}");
    }
}
```

Note: the sync layer (Task 8) calls `GetRawAsync` to keep the raw JSON blobs for storage AND deserializes for extraction, so we never double-fetch.

- [ ] **Step 5: Run green** → PASS (3 tests). **Step 6: Commit** — `feat(core): Riot API client with routing split + error mapping`.

---

## Task 6: Synthetic fixtures + match scalar extraction (TDD)

> **VERIFY GATE:** the fixtures must be **schema-accurate** — confirm the timeline/match JSON shape (gate #1) before finalizing. Use hand-built data with known values so the math is deterministic.

**Files:** Create `tests/.../Fixtures/sample_match.json`, `Fixtures/sample_timeline.json`, `Fixtures/FixtureLoader.cs`; `src/.../Analysis/MatchExtractor.cs`, `Analysis/AnalysisModels.cs`; Test `MatchExtractorTests.cs`.

Design the fixtures so "me" = puuid `"ME"` at `participantId 3` (MIDDLE, team 100), lane opponent = `participantId 8` (MIDDLE, team 200). Frames at 60s intervals. Hand-pick golds so:
- at 600 000 ms (frame 10) my CS = 75;
- at 900 000 ms (frame 15) my gold − opp gold = +412;
- I die at 840 000 ms (14:00) and 1 260 000 ms (21:00).

Mark the fixture file top-level with a comment field `"_note": "synthetic, schema-accurate, not real match data"`.

`AnalysisModels.cs`:

```csharp
namespace RiftReview.Core.Analysis;

public readonly record struct ChartPoint(double Minute, double Value);

public sealed record MatchSummary(
    int QueueId, long GameStartUtc, int DurationS, string Patch,
    int MyChampionId, string MyTeamPosition, bool Win,
    int Kills, int Deaths, int Assists, int Cs,
    int MyParticipantId, int? OpponentParticipantId, int? OpponentChampionId);

public sealed record DeepDive(
    IReadOnlyList<ChartPoint> GoldDiffVsLane,
    IReadOnlyList<ChartPoint> GoldDiffVsTeam,
    IReadOnlyList<ChartPoint> CsPerMinute,
    IReadOnlyList<double> DeathMinutes,
    bool HasLaneOpponent);
```

- [ ] **Step 1: FixtureLoader + failing test**

```csharp
public static class FixtureLoader
{
    public static string Read(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}
```

Add to test csproj so fixtures copy to output:
```xml
<ItemGroup><None Update="Fixtures\*.json"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></None></ItemGroup>
```

```csharp
using System.Text.Json;
using RiftReview.Core.Analysis;
using RiftReview.Core.Riot.Dtos;
using Xunit;

public class MatchExtractorTests
{
    private static MatchDto Match() => JsonSerializer.Deserialize<MatchDto>(
        FixtureLoader.Read("sample_match.json"), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    [Fact]
    public void Summarize_finds_me_and_lane_opponent()
    {
        var s = MatchExtractor.Summarize(Match(), myPuuid: "ME");
        Assert.Equal(3, s.MyParticipantId);
        Assert.Equal("MIDDLE", s.MyTeamPosition);
        Assert.Equal(8, s.OpponentParticipantId);
        Assert.True(s.Win);
        Assert.Equal(210, s.Cs); // totalMinionsKilled + neutralMinionsKilled from fixture
    }
}
```

- [ ] **Step 2: Run red** → FAIL.

- [ ] **Step 3: Implement `MatchExtractor`**

```csharp
using RiftReview.Core.Riot.Dtos;

namespace RiftReview.Core.Analysis;

public static class MatchExtractor
{
    public static MatchSummary Summarize(MatchDto match, string myPuuid)
    {
        var me = match.Info.Participants.FirstOrDefault(p => p.Puuid == myPuuid)
            ?? throw new InvalidOperationException($"puuid {myPuuid} not in match {match.Metadata.MatchId}");
        var opp = match.Info.Participants.FirstOrDefault(
            p => p.TeamId != me.TeamId && p.TeamPosition == me.TeamPosition && !string.IsNullOrEmpty(me.TeamPosition));

        var durationS = NormalizeDuration(match.Info.GameDuration);
        return new MatchSummary(
            (int)match.Info.QueueId, match.Info.GameCreation / 1000, durationS,
            PatchFromVersion(match.Info.GameVersion), me.ChampionId, me.TeamPosition, me.Win,
            me.Kills, me.Deaths, me.Assists, me.TotalMinionsKilled + me.NeutralMinionsKilled,
            me.ParticipantId, opp?.ParticipantId, opp?.ChampionId);
    }

    // gameDuration is seconds on modern patches; guard against legacy ms (>100000 => ms).
    private static int NormalizeDuration(long gd) => (int)(gd > 100_000 ? gd / 1000 : gd);

    private static string PatchFromVersion(string gameVersion)
    { var parts = gameVersion.Split('.'); return parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : gameVersion; }
}
```

- [ ] **Step 4: Run green** → PASS. **Step 5: Commit** — `feat(core): match-detail extraction + synthetic fixtures`.

---

## Task 7: Timeline extraction — series, scalars, deaths (TDD)

**Files:** Create `src/.../Analysis/TimelineExtractor.cs`; Test `TimelineExtractorTests.cs`.

- [ ] **Step 1: Failing tests** (assert the values baked into `sample_timeline.json`)

```csharp
using System.Text.Json;
using RiftReview.Core.Analysis;
using RiftReview.Core.Riot.Dtos;
using Xunit;

public class TimelineExtractorTests
{
    private static TimelineDto Tl() => JsonSerializer.Deserialize<TimelineDto>(
        FixtureLoader.Read("sample_timeline.json"), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    [Fact]
    public void DeepDive_builds_two_gold_lines_and_deaths()
    {
        var dd = TimelineExtractor.BuildDeepDive(Tl(), myParticipantId: 3, opponentParticipantId: 8);
        Assert.True(dd.HasLaneOpponent);
        Assert.Equal(412, dd.GoldDiffVsLane.Single(p => p.Minute == 15).Value);
        Assert.Equal(new[] { 14.0, 21.0 }, dd.DeathMinutes.ToArray());
        Assert.All(dd.CsPerMinute, p => Assert.True(p.Value >= 0));
    }

    [Fact]
    public void CsAt10_and_GoldDiffAt15_pick_nearest_frames()
    {
        Assert.Equal(75, TimelineExtractor.CsAtMinute(Tl(), participantId: 3, minute: 10));
        Assert.Equal(412, TimelineExtractor.GoldDiffAtMinute(Tl(), 3, 8, minute: 15));
    }

    [Fact]
    public void No_opponent_returns_team_line_only()
    {
        var dd = TimelineExtractor.BuildDeepDive(Tl(), myParticipantId: 3, opponentParticipantId: null);
        Assert.False(dd.HasLaneOpponent);
        Assert.Empty(dd.GoldDiffVsLane);
        Assert.NotEmpty(dd.GoldDiffVsTeam);
    }
}
```

- [ ] **Step 2: Run red** → FAIL.

- [ ] **Step 3: Implement `TimelineExtractor`**

```csharp
using RiftReview.Core.Riot.Dtos;

namespace RiftReview.Core.Analysis;

public static class TimelineExtractor
{
    private static int TeamOf(int pid) => pid <= 5 ? 100 : 200;

    public static DeepDive BuildDeepDive(TimelineDto tl, int myParticipantId, int? opponentParticipantId)
    {
        var laneLine = new List<ChartPoint>();
        var teamLine = new List<ChartPoint>();
        var csLine = new List<ChartPoint>();
        var myTeam = TeamOf(myParticipantId);

        foreach (var f in tl.Info.Frames)
        {
            double minute = f.Timestamp / 60000.0;
            if (!f.ParticipantFrames.TryGetValue(myParticipantId.ToString(), out var mine)) continue;

            long myTeamGold = 0, enemyTeamGold = 0;
            foreach (var (k, pf) in f.ParticipantFrames)
            { if (TeamOf(pf.ParticipantId) == myTeam) myTeamGold += pf.TotalGold; else enemyTeamGold += pf.TotalGold; }
            teamLine.Add(new ChartPoint(minute, myTeamGold - enemyTeamGold));

            if (opponentParticipantId is int oid &&
                f.ParticipantFrames.TryGetValue(oid.ToString(), out var opp))
                laneLine.Add(new ChartPoint(minute, mine.TotalGold - opp.TotalGold));

            int cs = mine.MinionsKilled + mine.JungleMinionsKilled;
            csLine.Add(new ChartPoint(minute, minute > 0 ? cs / minute : 0)); // running-average pace
        }

        var deaths = tl.Info.Frames
            .SelectMany(f => f.Events)
            .Where(e => e.Type == "CHAMPION_KILL" && e.VictimId == myParticipantId)
            .Select(e => Math.Round(e.Timestamp / 60000.0, 2))
            .OrderBy(m => m).ToList();

        return new DeepDive(laneLine, teamLine, csLine, deaths, opponentParticipantId is not null && laneLine.Count > 0);
    }

    public static int? CsAtMinute(TimelineDto tl, int participantId, int minute)
    {
        var f = NearestFrame(tl, minute);
        if (f is null || !f.ParticipantFrames.TryGetValue(participantId.ToString(), out var pf)) return null;
        return pf.MinionsKilled + pf.JungleMinionsKilled;
    }

    public static int? GoldDiffAtMinute(TimelineDto tl, int myParticipantId, int? opponentParticipantId, int minute)
    {
        if (opponentParticipantId is null) return null;
        var f = NearestFrame(tl, minute);
        if (f is null || !f.ParticipantFrames.TryGetValue(myParticipantId.ToString(), out var mine)
            || !f.ParticipantFrames.TryGetValue(opponentParticipantId.Value.ToString(), out var opp)) return null;
        return mine.TotalGold - opp.TotalGold;
    }

    private static FrameDto? NearestFrame(TimelineDto tl, int minute)
    {
        long target = minute * 60000L;
        return tl.Info.Frames.Count == 0 ? null
            : tl.Info.Frames.OrderBy(f => Math.Abs(f.Timestamp - target)).First();
    }
}
```

- [ ] **Step 4: Run green** → PASS (3 tests). **Step 5: Commit** — `feat(core): timeline extraction (gold lines, CS pace, deaths, scalars)`.

---

## Task 8: Sync service (TDD with stub client + temp DB)

**Files:** Create `src/.../Sync/SyncService.cs`, `Sync/SyncProgress.cs`; Test `SyncServiceTests.cs`.

Because `RiotApiClient` is a concrete class, introduce a thin interface `IRiotApiClient` it implements, so the sync test can inject a fake. Define `IRiotApiClient` with **exactly** the members `SyncService` + `EnsurePuuidAsync` use:

```csharp
namespace RiftReview.Core.Riot;
public interface IRiotApiClient
{
    Task<Dtos.AccountDto> ResolvePuuidAsync(string gameName, string tagLine, CancellationToken ct = default);
    Task<List<string>> GetMatchIdsAsync(string puuid, int start, int count, CancellationToken ct = default);
    Task<(Dtos.MatchDto Dto, string Raw)> GetMatchWithRawAsync(string id, CancellationToken ct = default);
    Task<(Dtos.TimelineDto Dto, string Raw)> GetTimelineWithRawAsync(string id, CancellationToken ct = default);
}
```

Add `: IRiotApiClient` to `RiotApiClient`. The sync stores the raw JSON (for the blob columns) and the deserialized object (for extraction) from a single fetch — add these two helpers to `RiotApiClient`:

```csharp
public async Task<(MatchDto Dto, string Raw)> GetMatchWithRawAsync(string id, CancellationToken ct = default)
{ var raw = await GetRawAsync($"{_regionalHost}/lol/match/v5/matches/{id}", ct);
  return (System.Text.Json.JsonSerializer.Deserialize<MatchDto>(raw, Json)!, raw); }
public async Task<(TimelineDto Dto, string Raw)> GetTimelineWithRawAsync(string id, CancellationToken ct = default)
{ var raw = await GetRawAsync($"{_regionalHost}/lol/match/v5/matches/{id}/timeline", ct);
  return (System.Text.Json.JsonSerializer.Deserialize<TimelineDto>(raw, Json)!, raw); }
```

`SyncProgress.cs`:

```csharp
namespace RiftReview.Core.Sync;
public sealed record SyncProgress(int Fetched, int Total, string? Message);
public sealed record SyncResult(int NewMatches, int Skipped, string? Error);
```

- [ ] **Step 1: Failing test** (fake client returns 2 ids, one already stored → fetches only the new one)

```csharp
using RiftReview.Core.Data;
using RiftReview.Core.Riot;
using RiftReview.Core.Riot.Dtos;
using RiftReview.Core.Sync;
using Xunit;

public class SyncServiceTests
{
    [Fact]
    public async Task Sync_skips_existing_and_inserts_new()
    {
        using var db = RiftReviewDb.Open("Data Source=:memory:;Cache=Shared");
        db.SetMeta("puuid", "ME");
        // Pre-store NA1_1 so it is skipped
        db.UpsertMatch(new MatchRow("NA1_1",420,1,1800,"15.12",103,"MIDDLE",true,1,1,1,1,null,null,null,null,1), "{}", "{}");

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
    }
}
```

(`FakeRiotClient` implements `IRiotApiClient` returning canned data; `TestData` builds minimal valid DTOs. Write both in the test project. Keep them minimal but schema-valid.)

- [ ] **Step 2: Run red** → FAIL.

- [ ] **Step 3: Implement `SyncService`**

```csharp
using RiftReview.Core.Analysis;
using RiftReview.Core.Data;
using RiftReview.Core.Riot;

namespace RiftReview.Core.Sync;

public sealed class SyncService
{
    private readonly RiftReviewDb _db;
    private readonly IRiotApiClient _client;
    public SyncService(RiftReviewDb db, IRiotApiClient client) { _db = db; _client = client; }

    public async Task<SyncResult> SyncAsync(int count, IProgress<SyncProgress>? progress, CancellationToken ct = default)
    {
        try
        {
            var puuid = _db.GetMeta("puuid") ?? throw new InvalidOperationException("No PUUID resolved.");
            var ids = await _client.GetMatchIdsAsync(puuid, 0, count, ct);
            var newIds = ids.Where(id => !_db.HasMatch(id)).ToList();
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
                _db.UpsertMatch(row, matchRaw, tlRaw); // per-match transaction inside Upsert
                progress?.Report(new SyncProgress(++done, newIds.Count, id));
            }
            _db.SetMeta("last_sync_utc", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
            return new SyncResult(newIds.Count, ids.Count - newIds.Count, null);
        }
        catch (RiotApiException ex) when (ex.IsKeyProblem)
        { return new SyncResult(0, 0, "Your Riot API key looks expired or invalid. Set a fresh dev key and try again."); }
        catch (RiotApiException ex)
        { return new SyncResult(0, 0, ex.Message); }
    }
}
```

Add the `EnsurePuuidAsync` helper (resolves Riot ID → puuid via ACCOUNT-V1 and stores it in `meta`) and call it from the App before `SyncAsync` on first run; keep it out of `SyncService` so sync stays unit-testable without account resolution. Signature:

```csharp
public static async Task EnsurePuuidAsync(RiftReviewDb db, IRiotApiClient client, string riotId)
{
    if (db.GetMeta("puuid") is not null) return;
    var parts = riotId.Split('#', 2);
    if (parts.Length != 2) throw new InvalidOperationException("Riot ID must be 'GameName#TAG'.");
    var acc = await client.ResolvePuuidAsync(parts[0], parts[1]);
    db.SetMeta("puuid", acc.Puuid);
    db.SetMeta("riot_id", riotId);
}
```

- [ ] **Step 4: Run green** → PASS. **Step 5: Commit** — `feat(core): incremental sync service with key-expiry handling`.

---

## Task 9: Baseline calculator (TDD)

**Files:** Create `src/.../Analysis/BaselineCalculator.cs`; Test `BaselineCalculatorTests.cs`.

Computes the same-role rolling baseline CS-pace curve = per-minute average of recent same-role games' CS-pace curves. Input: a list of `IReadOnlyList<ChartPoint>` (each game's CS pace). Output: averaged curve, or empty if fewer than `minGames` (default 3).

- [ ] **Step 1: Failing test**

```csharp
using RiftReview.Core.Analysis;
using Xunit;

public class BaselineCalculatorTests
{
    [Fact]
    public void Averages_pace_per_minute_across_games()
    {
        var g1 = new List<ChartPoint> { new(1, 4), new(2, 6) };
        var g2 = new List<ChartPoint> { new(1, 6), new(2, 8) };
        var g3 = new List<ChartPoint> { new(1, 5), new(2, 7) };
        var b = BaselineCalculator.Average(new[] { g1, g2, g3 }, minGames: 3);
        Assert.Equal(5, b.Single(p => p.Minute == 1).Value);
        Assert.Equal(7, b.Single(p => p.Minute == 2).Value);
    }

    [Fact]
    public void Returns_empty_when_too_few_games()
        => Assert.Empty(BaselineCalculator.Average(new[] { new List<ChartPoint> { new(1, 4) } }, minGames: 3));
}
```

- [ ] **Step 2: Run red** → FAIL.

- [ ] **Step 3: Implement**

```csharp
namespace RiftReview.Core.Analysis;

public static class BaselineCalculator
{
    public static IReadOnlyList<ChartPoint> Average(IEnumerable<IReadOnlyList<ChartPoint>> games, int minGames = 3)
    {
        var list = games.ToList();
        if (list.Count < minGames) return Array.Empty<ChartPoint>();
        var byMinute = new Dictionary<double, (double sum, int n)>();
        foreach (var g in list)
            foreach (var p in g)
            { var cur = byMinute.GetValueOrDefault(p.Minute); byMinute[p.Minute] = (cur.sum + p.Value, cur.n + 1); }
        return byMinute.OrderBy(kv => kv.Key)
            .Select(kv => new ChartPoint(kv.Key, kv.Value.sum / kv.Value.n)).ToList();
    }
}
```

- [ ] **Step 4: Run green** → PASS. **Step 5: Commit** — `feat(core): same-role rolling baseline calculator`.

---

## Task 10: Data Dragon client (TDD with stub)

**Files:** Create `src/.../DataDragon/DataDragonClient.cs`; Test `DataDragonClientTests.cs`.

Fetches latest version + champion id→name map; caches `champion.json` + icons under `%LOCALAPPDATA%\RiftReview\ddragon\{ver}\`. For tests, inject the cache dir + HttpClient. Icons can be lazy (download-on-demand); v1 minimal need = championId→name (icons optional, can come later if time-boxed).

- [ ] **Step 1: Failing test**

```csharp
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
}
```

- [ ] **Step 2: Run red** → FAIL.

- [ ] **Step 3: Implement** (confirm Data Dragon URLs at build time — gate #2)

```csharp
using System.Text.Json;

namespace RiftReview.Core.DataDragon;

public sealed class DataDragonClient
{
    private readonly HttpClient _http;
    private readonly string _cacheDir;
    private readonly Dictionary<int, string> _names = new();
    public string Version { get; private set; } = "";

    public DataDragonClient(HttpClient http, string cacheDir) { _http = http; _cacheDir = cacheDir; }

    public async Task EnsureLoadedAsync(CancellationToken ct = default)
    {
        if (_names.Count > 0) return;
        var versions = JsonSerializer.Deserialize<List<string>>(
            await _http.GetStringAsync("https://ddragon.leagueoflegends.com/api/versions.json", ct))!;
        Version = versions[0];
        var champJson = await _http.GetStringAsync(
            $"https://ddragon.leagueoflegends.com/cdn/{Version}/data/en_US/champion.json", ct);
        using var doc = JsonDocument.Parse(champJson);
        foreach (var c in doc.RootElement.GetProperty("data").EnumerateObject())
            if (int.TryParse(c.Value.GetProperty("key").GetString(), out var id))
                _names[id] = c.Value.GetProperty("name").GetString() ?? c.Name;
    }

    public string ChampionName(int championId) => _names.TryGetValue(championId, out var n) ? n : $"Champ {championId}";
    public string ChampionIconUrl(string iconBasename) =>
        $"https://ddragon.leagueoflegends.com/cdn/{Version}/img/champion/{iconBasename}.png";
}
```

- [ ] **Step 4: Run green** → PASS. **Step 5: Commit** — `feat(core): Data Dragon champion-name resolver`.

---

## Task 11: Configuration + options + DI host (App)

**Files:** Create `src/RiftReview.App/Configuration/` binding, `appsettings.json`, edit `App.xaml.cs`. (`RiotOptions` lives in Core.)

`src/RiftReview.Core/Configuration/RiotOptions.cs`:

```csharp
namespace RiftReview.Core.Configuration;
public sealed class RiotOptions
{
    public string ApiKey { get; set; } = "";
    public string RiotId { get; set; } = "";   // GameName#TAG
    public string Platform { get; set; } = "na1";
}
```

`src/RiftReview.App/appsettings.json` (placeholders only — committed):

```json
{
  "Riot": {
    "ApiKey": "SET-VIA-USER-SECRETS",
    "RiotId": "SET-VIA-USER-SECRETS",
    "Platform": "na1"
  }
}
```

- [ ] **Step 1: Host wiring in `App.xaml.cs`** (confirm WPF-UI startup against VideoShelf — gate #3)

```csharp
using System.IO;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RiftReview.Core.Configuration;
using RiftReview.Core.Data;
using RiftReview.Core.DataDragon;
using RiftReview.Core.Riot;

namespace RiftReview.App;

public partial class App : Application
{
    private IHost? _host;
    public static IServiceProvider Services => ((App)Current)._host!.Services;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        bool seedDemo = e.Args.Contains("--seed-demo");

        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RiftReview");
        Directory.CreateDirectory(appData);
        var dbPath = seedDemo ? Path.Combine(appData, "demo.db") : Path.Combine(appData, "riftreview.db");
        if (seedDemo && File.Exists(dbPath)) File.Delete(dbPath);

        _host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((ctx, cfg) =>
            {
                cfg.SetBasePath(AppContext.BaseDirectory).AddJsonFile("appsettings.json", optional: false);
                cfg.AddUserSecrets<App>(optional: true);
            })
            .ConfigureServices((ctx, s) =>
            {
                s.Configure<RiotOptions>(ctx.Configuration.GetSection("Riot"));
                s.AddSingleton<ISystemClock, SystemClock>();
                s.AddSingleton(sp => new RiotRateLimiter(sp.GetRequiredService<ISystemClock>()));
                s.AddHttpClient();
                s.AddSingleton(sp => RiftReviewDb.Open($"Data Source={dbPath}"));
                s.AddSingleton(sp =>
                {
                    var o = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RiotOptions>>().Value;
                    var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
                    return new RiotApiClient(http, sp.GetRequiredService<RiotRateLimiter>(), o.ApiKey, o.Platform);
                });
                s.AddSingleton(sp => new DataDragonClient(
                    sp.GetRequiredService<IHttpClientFactory>().CreateClient(), Path.Combine(appData, "ddragon")));
                s.AddSingleton<ViewModels.MainViewModel>();
                s.AddSingleton<Views.MainWindow>();
            })
            .Build();

        if (seedDemo) Demo.DemoSeeder.Seed(Services.GetRequiredService<RiftReviewDb>());

        // Apply WPF-UI theme + Hextech Gold accent BEFORE first render (see VideoShelf App.xaml.cs pattern).
        ApplyTheme();

        var win = Services.GetRequiredService<Views.MainWindow>();
        win.Show();
    }

    private static void ApplyTheme()
    {
        // VERIFY against WPF-UI 4.3: ApplicationThemeManager.Apply(ApplicationTheme.Dark);
        // ApplicationAccentColorManager.Apply(System.Windows.Media.Color.FromRgb(0xC8,0xAA,0x6E));
    }

    protected override void OnExit(ExitEventArgs e) { _host?.Dispose(); base.OnExit(e); }
}
```

Remove the default `StartupUri` from `App.xaml`. Confirm `AddUserSecrets<App>` resolves the `UserSecretsId`.

- [ ] **Step 2: Build** → `dotnet build`. (No new unit test; this is wiring. The seed-demo screenshot gate in Task 15 verifies it runs.)
- [ ] **Step 3: Commit** — `feat(app): host, DI, config binding, --seed-demo plumbing`.

---

## Task 12: Hand-rolled LineChart control + geometry tests

**Files:** Create `src/RiftReview.App/Controls/LineChart.cs`; Test `tests/RiftReview.App.Tests/LineChartTests.cs`.

Split the pure math (data→pixel scaling) from the WPF drawing so it is unit-testable without a UI thread.

- [ ] **Step 1: Failing geometry test**

```csharp
using RiftReview.App.Controls;
using Xunit;

public class LineChartTests
{
    [Fact]
    public void Scaler_maps_data_bounds_to_pixel_rect()
    {
        var sc = new ChartScaler(minX: 0, maxX: 10, minY: -100, maxY: 100, width: 200, height: 100, pad: 0);
        Assert.Equal(0, sc.X(0), 3);
        Assert.Equal(200, sc.X(10), 3);
        Assert.Equal(100, sc.Y(-100), 3); // bottom
        Assert.Equal(0, sc.Y(100), 3);    // top
        Assert.Equal(50, sc.Y(0), 3);     // zero line mid
    }
}
```

- [ ] **Step 2: Run red** → FAIL.

- [ ] **Step 3: Implement scaler + control**

```csharp
namespace RiftReview.App.Controls;

public sealed class ChartScaler
{
    private readonly double _minX, _maxX, _minY, _maxY, _w, _h, _pad;
    public ChartScaler(double minX, double maxX, double minY, double maxY, double width, double height, double pad)
    { _minX=minX; _maxX=maxX; _minY=minY; _maxY=maxY; _w=width; _h=height; _pad=pad; }
    public double X(double x) => _pad + (_maxX <= _minX ? 0 : (x - _minX) / (_maxX - _minX)) * (_w - 2*_pad);
    public double Y(double y) => _pad + (_maxY <= _minY ? 0 : (1 - (y - _minY) / (_maxY - _minY))) * (_h - 2*_pad);
}
```

Then the WPF control (renders on a `Canvas`; dependency properties for series + markers + a dashed baseline). Provide a `ChartSeries(IReadOnlyList<ChartPoint> Points, Brush Stroke, bool Dashed)` model and redraw on `SizeChanged`/property change. Full drawing code is straightforward Polyline/Line construction; build it against WPF-UI brushes for theme colors (Hextech Gold for primary, a teal for team, red for death markers). The screenshot gate (Task 15) verifies the visual.

- [ ] **Step 4: Run green** (geometry test) → PASS. **Step 5: Commit** — `feat(app): hand-rolled LineChart control + scaler`.

---

## Task 13: ViewModels (trend strip is unit-tested)

**Files:** Create `ViewModels/MainViewModel.cs`, `MatchListItemViewModel.cs`, `DeepDiveViewModel.cs`, `TrendStripViewModel.cs`; Test `TrendStripViewModelTests.cs`.

`MainViewModel`: `ObservableCollection<MatchListItemViewModel> Matches`, `bool RankedOnly`, `SelectedMatch`, `SyncCommand` (async; calls `EnsurePuuidAsync` then `SyncService.SyncAsync`, then reloads), `StatusMessage`. Uses `[ObservableProperty]`/`[RelayCommand]` from CommunityToolkit.Mvvm.

`TrendStripViewModel`: pure transform of `IReadOnlyList<MatchRow>` → four series (win/loss bools, deaths, cs@10, gold@15) — unit-test this transform.

- [ ] **Step 1: Failing test**

```csharp
using RiftReview.App.ViewModels;
using RiftReview.Core.Data;
using Xunit;

public class TrendStripViewModelTests
{
    [Fact]
    public void Builds_four_series_in_chronological_order()
    {
        var rows = new List<MatchRow>
        {
            Row("NA1_2", start: 200, win: false, deaths: 7, cs10: 60, g15: -100),
            Row("NA1_1", start: 100, win: true,  deaths: 3, cs10: 70, g15: 300),
        };
        var vm = new TrendStripViewModel();
        vm.Load(rows);
        Assert.Equal(new[] { true, false }, vm.Wins.ToArray());   // oldest→newest
        Assert.Equal(new[] { 3, 7 }, vm.Deaths.ToArray());
        Assert.Equal(new[] { 70, 60 }, vm.CsAt10.ToArray());
        Assert.Equal(new[] { 300, -100 }, vm.GoldAt15.ToArray());
    }

    private static MatchRow Row(string id, long start, bool win, int deaths, int cs10, int g15) =>
        new(id, 420, start, 1800, "15.12", 103, "MIDDLE", win, 1, deaths, 1, 100, cs10, g15, 7, 1, start);
}
```

- [ ] **Step 2: Run red** → FAIL. **Step 3: Implement** the VM transform (sort ascending by `GameStartUtc`, project the four lists, treat null cs10/g15 as gaps/0). **Step 4: green** → PASS. **Step 5: Commit** — `feat(app): view models + trend-strip transform`.

---

## Task 14: Views / XAML (match list, trend strip, deep-dive)

**Files:** Create `Views/MainWindow.xaml(.cs)`, `Views/DeepDiveView.xaml(.cs)`, `Views/TrendStripView.xaml(.cs)`, `Themes/Colors.xaml`.

Build the approved layout: left match-list rail (`ListBox` bound to `Matches`, Sync button + Ranked/All toggle in the header) + right pane with the trend strip on top and the deep-dive (header + two `LineChart`s) below. Black-glass palette + Hextech Gold accent in `Themes/Colors.xaml`. Reference VideoShelf's `MainWindow.xaml` for the WPF-UI `FluentWindow` + theme resource wiring.

- [ ] **Step 1:** Implement XAML + bind to VMs. No unit test (covered by the screenshot gate).
- [ ] **Step 2: Build** → `dotnet build`. **Step 3: Commit** — `feat(app): main window, trend strip, deep-dive views`.

---

## Task 15: Demo seeder + offline screenshot verification gate

**Files:** Create `src/RiftReview.App/Demo/DemoSeeder.cs` (embed a copy of the synthetic match+timeline fixtures as resources; insert ~8 fake matches with varied stats so the trend strip + charts have data).

- [ ] **Step 1:** Implement `DemoSeeder.Seed(RiftReviewDb db)` — sets `meta.puuid="ME"`, inserts ~8 matches built from the fixture timeline with jittered scalars (so the trend sparklines vary), all clearly synthetic.

- [ ] **Step 2: Build the app**

Run: `dotnet build src/RiftReview.App`

- [ ] **Step 3: Screenshot verification (Sonnet subagent, TEXT verdict)**

Launch `RiftReview.App.exe --seed-demo`, capture the window, and dispatch a **Sonnet subagent** to inspect the screenshot and return a text verdict (per the standing rule — do NOT load the PNG into the main session). The subagent confirms: window renders in black-glass + gold; match list populated; trend strip shows 4 metric rows; deep-dive shows two gold lines + death markers + the CS curve with dashed baseline. If it reports a render bug (crash / Collapsed / wrong contrast), fix and re-run before proceeding.

- [ ] **Step 4: Commit** — `feat(app): demo seeder + offline chart verification`.

---

## Task 16: Error/empty states + README + first push

**Files:** Edit VMs/Views for empty + error states; create `README.md`.

- [ ] **Step 1:** Empty state ("No matches yet — set your Riot key and press Sync"); error banner bound to `StatusMessage`; key-expired and rate-limited messages already returned by `SyncService`.
- [ ] **Step 2:** `README.md` — what it is, single-user/read-only/non-commercial, the **user-secrets setup block**, and a **manual live-verification checklist** (set secrets → launch → Sync → confirm matches + charts populate → force key-expiry by using a bad key → confirm the friendly message).
- [ ] **Step 3: Build + full test run**

Run: `dotnet build RiftReview.slnx && dotnet test RiftReview.slnx`
Expected: build + all tests PASS.

- [ ] **Step 4: First push — secret safety check (CRITICAL)**

```bash
git add -A
git status                 # review every staged file
git ls-files | grep -iE "secret|appsettings.local|\.env" || echo "OK: no secret-looking files tracked"
# Confirm appsettings.json contains ONLY placeholders, never a real RGAPI- key:
grep -R "RGAPI-" -n . --include=*.json || echo "OK: no real key in tracked files"
git commit -m "feat(app): error/empty states, README, setup + verification checklist"
gh repo create yovanmc/RiftReview --public --source=. --remote=origin --description "Personal single-user League post-game review (C#/.NET 10 WPF + SQLite)"
git push -u origin HEAD
```

If either guard prints a hit instead of "OK", STOP and remove the file from staging before pushing. Treat pushed public history as permanent.

- [ ] **Step 5:** Confirm the repo is public, the push succeeded, and CI (if any) is green.

---

## Done — v1 acceptance

- [ ] All Core tests pass (DB, routing, rate limiter, API client, extraction, baseline, sync, Data Dragon).
- [ ] App launches with `--seed-demo` and the Sonnet subagent verdict confirms the charts render correctly.
- [ ] `git grep RGAPI-` finds nothing; `.gitignore` excludes secrets; repo pushed public.
- [ ] README has the user-secrets block + the **manual live-pull checklist Yovan must run** (the one thing not verifiable here).

---

## Self-review notes (plan vs spec)

- Spec §3 data layer → Tasks 2 (matches/match_detail/meta, versioned migrations). ✓
- Spec §4 Riot integration (routing split, rate limiter, error handling) → Tasks 3, 4, 5. ✓
- Spec §6 Data Dragon → Task 10. ✓
- Spec §7 sync (incremental, per-match tx, key-expiry) → Task 8. ✓
- Spec §8 analysis (two gold lines, CS pace, deaths, cs@10/gold@15, baseline) → Tasks 6, 7, 9. ✓
- Spec §9 UI (layout, theme, hand-rolled charts) → Tasks 12, 14. ✓
- Spec §10 secrets/config (user-secrets, placeholders, gitignore) → Tasks 11, 16. ✓
- Spec §11 repo (public push, secret check) → Task 16. ✓
- Spec §12 testing (offline fixtures, --seed-demo screenshot gate, live-pull flagged) → Tasks 6–9, 15, 16. ✓
- Open verify gates centralized at top; none block offline progress. ✓
```
