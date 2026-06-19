# RiftReview M2 — Per-Champ Improvement Trends — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a per-champion "Trends" page that trends key performance metrics over a trailing N-game rolling window and renders each as a trajectory row with an improving/steady/declining verdict.

**Architecture:** Three new derived scalars (kill participation, damage share, pre-15 deaths) are precomputed into the `matches` table (schema v2→v3), backfilled once from the already-stored raw blobs (zero Riot calls) and populated on future syncs. A pure `ChampTrendCalculator` does the rolling-window + verdict math; a `TrendsViewModel` + `TrendsView` render layout C (trajectory rows + row-click drill chart). Mirrors the existing `ChampPoolCalculator` / `ChampPoolView` patterns.

**Tech Stack:** C#/.NET 10, WPF + WPF-UI 4.3, Microsoft.Data.Sqlite, CommunityToolkit.Mvvm, xUnit.

**Design spec:** `docs/superpowers/specs/2026-06-19-riftreview-m2-design.md`.

---

## Build-time verification gates (confirm BEFORE trusting representative code)

1. **Champion-damage field name.** Before extending `ParticipantDto`, confirm the exact MATCH-V5 participant field for champion damage by inspecting a REAL stored blob. Run (PowerShell, against the owner's real DB):
   `dotnet run`-free check is fine via the test fixtures, but the authoritative check is a real blob. Acceptable confirmation: grep the field name in a stored `match_json`. Expected: `totalDamageDealtToChampions`. If the real blob uses a different name, use that name in the `[JsonPropertyName]`/property. **Do not assume.**
2. **Timeline kill-event shape.** `EventDto` is already `(string Type, long Timestamp, int? KillerId, int? VictimId)` and `TimelineExtractor.BuildDeepDive` already filters `e.Type == "CHAMPION_KILL" && e.VictimId == myParticipantId`. Reuse that exact pattern for pre-15 deaths.
3. **SQLite ALTER TABLE ADD COLUMN** runs inside the existing `RunVersionedMigrations` (`PRAGMA user_version` gated) and flows through `SELECT *` in `ReadRow` (which reads by `GetOrdinal(name)`), so new columns are picked up automatically once `ReadRow` and `UpsertMatch` reference them.
4. **WPF-UI NavigationView** 3→4 menu items (Review/Champions/Trends + Settings footer) — same API proven in M1 (`NavigationViewItem` + `TargetPageType`).

## Existing code touch-points (read before starting)

- `src/RiftReview.Core/Riot/Dtos/MatchDtos.cs` — `ParticipantDto` (11 positional fields today).
- `src/RiftReview.Core/Riot/Dtos/TimelineDtos.cs` — `EventDto`, `FrameDto`.
- `src/RiftReview.Core/Analysis/MatchExtractor.cs` — `Summarize(MatchDto, string myPuuid)` → `MatchSummary`.
- `src/RiftReview.Core/Analysis/TimelineExtractor.cs` — death-event parsing pattern.
- `src/RiftReview.Core/Analysis/AnalysisModels.cs` — `MatchSummary`.
- `src/RiftReview.Core/Data/MatchRow.cs` / `Data/RiftReviewDb.cs` — row record, migration runner, `UpsertMatch`, `ReadRow`, `GetMatchJson`, `GetTimelineJson`.
- `src/RiftReview.Core/Sync/SyncService.cs` — match loop building `MatchRow`.
- `src/RiftReview.Core/Configuration/SettingsStore.cs` — meta-backed settings.
- `src/RiftReview.Core/Analysis/ChampPoolCalculator.cs` + `ChampPoolModels.cs` — pure-calculator pattern to mirror.
- `src/RiftReview.App/Controls/Sparkline.cs` — gold polyline control (int? today).
- `src/RiftReview.App/Views/ChampPoolView.xaml(.cs)` + `ViewModels/ChampPoolViewModel.cs` — view/VM pattern.
- `src/RiftReview.App/AppShell.xaml(.cs)`, `App.xaml.cs`, `Views/SettingsView.xaml(.cs)`, `ViewModels/SettingsViewModel.cs`.
- `src/RiftReview.App/Demo/DemoSeeder.cs`.

App test classes have NO namespace (top-level). Core tests `using` the namespaces. Commit author is the repo default (`yovanmc`); append `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>` to every commit. Plain `git commit` — never `--author`.

---

## Task 1: Champion-damage on `ParticipantDto` + KP/damage-share in `MatchExtractor`

**Files:**
- Modify: `src/RiftReview.Core/Riot/Dtos/MatchDtos.cs`
- Modify: `src/RiftReview.Core/Analysis/AnalysisModels.cs` (extend `MatchSummary`)
- Modify: `src/RiftReview.Core/Analysis/MatchExtractor.cs`
- Test: `tests/RiftReview.Core.Tests/MatchExtractorTests.cs` (add tests; create if missing)

- [ ] **Step 0: Gate** — confirm the champion-damage field name in a real stored `match_json` (see Build-time gate 1). Proceed with `TotalDamageDealtToChampions` unless the blob says otherwise.

- [ ] **Step 1: Write the failing test**

```csharp
using RiftReview.Core.Analysis;
using RiftReview.Core.Riot.Dtos;
using Xunit;

public class MatchExtractorDerivedTests
{
    private static ParticipantDto P(int pid, int team, int k, int a, int dmg, string puuid) =>
        new(puuid, pid, 100 + pid, team, "MIDDLE", team == 100, k, 2, a, 100, 0, dmg);

    private static MatchDto Match()
    {
        var parts = new List<ParticipantDto>
        {
            P(1,100, 5,3, 1000, "ME"),   // me: kills 5, assists 3, dmg 1000
            P(2,100, 3,1, 500,  "B"),
            P(3,100, 2,0, 500,  "C"),    // team100 kills total = 5+3+2 = 10; dmg = 1000+500+500 = 2000
            P(6,200, 4,4, 800,  "X"),
            P(7,200, 1,1, 700,  "Y"),
        };
        return new MatchDto(new MatchMetadata("NA1_1", parts.Select(p => p.Puuid).ToList()),
            new MatchInfo(420, 1_700_000_000_000, 1800, "15.12.1", parts));
    }

    [Fact]
    public void Computes_kill_participation_and_damage_share()
    {
        var s = MatchExtractor.Summarize(Match(), "ME");
        // KP = (5 + 3) / teamKills(10) = 0.8
        Assert.Equal(0.8, s.KillParticipation, 3);
        // damage share = 1000 / teamDmg(2000) = 0.5
        Assert.Equal(0.5, s.DamageShare, 3);
    }

    [Fact]
    public void Guards_divide_by_zero_when_team_has_no_kills_or_damage()
    {
        var parts = new List<ParticipantDto>
        {
            new("ME", 1, 101, 100, "MIDDLE", true, 0, 0, 0, 0, 0, 0),
            new("B",  2, 102, 100, "MIDDLE", true, 0, 0, 0, 0, 0, 0),
        };
        var m = new MatchDto(new MatchMetadata("NA1_2", new List<string> { "ME", "B" }),
            new MatchInfo(420, 1, 1800, "15.12.1", parts));
        var s = MatchExtractor.Summarize(m, "ME");
        Assert.Equal(0.0, s.KillParticipation, 3);
        Assert.Equal(0.0, s.DamageShare, 3);
    }
}
```

- [ ] **Step 2: Run to verify failure** — `dotnet test --filter MatchExtractorDerivedTests`; Expected: FAIL (compile — `TotalDamageDealtToChampions`/`KillParticipation`/`DamageShare` don't exist).

- [ ] **Step 3: Extend `ParticipantDto`** (append an optional positional field so existing 11-arg construction sites still compile):

```csharp
public sealed record ParticipantDto(
    string Puuid, int ParticipantId, int ChampionId, int TeamId, string TeamPosition, bool Win,
    int Kills, int Deaths, int Assists, int TotalMinionsKilled, int NeutralMinionsKilled,
    int TotalDamageDealtToChampions = 0);
```

- [ ] **Step 4: Extend `MatchSummary`** (append two fields):

```csharp
public sealed record MatchSummary(
    int QueueId, long GameStartUtc, int DurationS, string Patch,
    int MyChampionId, string MyTeamPosition, bool Win,
    int Kills, int Deaths, int Assists, int Cs,
    int MyParticipantId, int? OpponentParticipantId, int? OpponentChampionId,
    double KillParticipation, double DamageShare);
```

- [ ] **Step 5: Compute them in `MatchExtractor.Summarize`** — after `me` is resolved, before the `return`, add the team aggregates and pass them to the constructor:

```csharp
        var myTeam = match.Info.Participants.Where(p => p.TeamId == me.TeamId).ToList();
        int teamKills = myTeam.Sum(p => p.Kills);
        long teamDmg = myTeam.Sum(p => (long)p.TotalDamageDealtToChampions);
        double kp = teamKills <= 0 ? 0 : (me.Kills + me.Assists) / (double)teamKills;
        double dmgShare = teamDmg <= 0 ? 0 : me.TotalDamageDealtToChampions / (double)teamDmg;
```

Then append `kp, dmgShare` to the `new MatchSummary(...)` call (last two args).

- [ ] **Step 6: Run to verify pass** — `dotnet test --filter MatchExtractorDerivedTests`; Expected: PASS.

- [ ] **Step 7: Full build + test** — `dotnet build RiftReview.slnx`; `dotnet test`; Expected: clean (0 warnings) + green.

- [ ] **Step 8: Commit**

```bash
git add src/RiftReview.Core/Riot/Dtos/MatchDtos.cs src/RiftReview.Core/Analysis/AnalysisModels.cs src/RiftReview.Core/Analysis/MatchExtractor.cs tests/RiftReview.Core.Tests/MatchExtractorTests.cs
git commit -m "feat(core): kill participation + damage share in match extraction

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

(If the test file is named differently, `git add` the actual path you created.)

---

## Task 2: `TimelineExtractor.DeathsBeforeMinute`

**Files:**
- Modify: `src/RiftReview.Core/Analysis/TimelineExtractor.cs`
- Test: `tests/RiftReview.Core.Tests/TimelineExtractorTests.cs` (add a test; create if missing)

- [ ] **Step 1: Write the failing test**

```csharp
using RiftReview.Core.Analysis;
using RiftReview.Core.Riot.Dtos;
using Xunit;

public class TimelineExtractorPre15Tests
{
    [Fact]
    public void Counts_only_my_deaths_before_the_minute_mark()
    {
        var frames = new List<FrameDto>
        {
            new(0, new(), new List<EventDto>
            {
                new("CHAMPION_KILL", 5*60000L, 8, 3),   // me (pid 3) dies at 5:00  -> counts
                new("CHAMPION_KILL", 9*60000L, 8, 7),   // someone else dies        -> no
            }),
            new(60000, new(), new List<EventDto>
            {
                new("CHAMPION_KILL", 14*60000L, 8, 3),  // me dies at 14:00         -> counts
                new("CHAMPION_KILL", 16*60000L, 8, 3),  // me dies at 16:00         -> after 15, no
            }),
        };
        var tl = new TimelineDto(new TimelineMetadata("NA1_1", new()), new TimelineInfo(60000, frames));
        Assert.Equal(2, TimelineExtractor.DeathsBeforeMinute(tl, myParticipantId: 3, minute: 15));
    }
}
```

- [ ] **Step 2: Run to verify failure** — `dotnet test --filter TimelineExtractorPre15Tests`; Expected: FAIL (method missing).

- [ ] **Step 3: Implement** — add to `TimelineExtractor`:

```csharp
    public static int DeathsBeforeMinute(TimelineDto tl, int myParticipantId, int minute)
    {
        long cutoff = minute * 60000L;
        return tl.Info.Frames
            .SelectMany(f => f.Events)
            .Count(e => e.Type == "CHAMPION_KILL" && e.VictimId == myParticipantId && e.Timestamp < cutoff);
    }
```

- [ ] **Step 4: Run to verify pass** — `dotnet test --filter TimelineExtractorPre15Tests`; Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/RiftReview.Core/Analysis/TimelineExtractor.cs tests/RiftReview.Core.Tests/TimelineExtractorTests.cs
git commit -m "feat(core): pre-15 death count from timeline

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 3: `MatchRow` + schema v3 migration + DB plumbing

**Files:**
- Modify: `src/RiftReview.Core/Data/MatchRow.cs`
- Modify: `src/RiftReview.Core/Data/RiftReviewDb.cs`
- Test: `tests/RiftReview.Core.Tests/RiftReviewDbTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
    [Fact]
    public void Schema_is_v3()
    {
        using var db = RiftReviewDb.Open("Data Source=:memory:");
        Assert.Equal(3, db.GetSchemaVersion());
    }

    [Fact]
    public void Derived_metrics_roundtrip()
    {
        using var db = RiftReviewDb.Open("Data Source=:memory:");
        var row = new MatchRow("NA1_1", 420, 1, 1800, "15.12", 103, "MIDDLE", true,
            5, 3, 7, 200, 80, 150, 4, 8, 100,
            KillParticipation: 0.62, DamageShare: 0.27, DeathsPre15: 1);
        db.UpsertMatch(row, "{}", "{}");
        var back = db.GetMatch("NA1_1")!;
        Assert.Equal(0.62, back.KillParticipation!.Value, 3);
        Assert.Equal(0.27, back.DamageShare!.Value, 3);
        Assert.Equal(1, back.DeathsPre15);
    }
```

(The `MatchRow` ctor here passes the first 17 positional args then the three new named args.)

- [ ] **Step 2: Run to verify failure** — `dotnet test --filter RiftReviewDbTests`; Expected: FAIL (v2 != v3; new members missing).

- [ ] **Step 3: Extend `MatchRow`** (append three nullable fields with defaults so existing call sites compile):

```csharp
public sealed record MatchRow(
    string MatchId, int QueueId, long GameStartUtc, int DurationS, string Patch,
    int MyChampionId, string MyTeamPosition, bool Win,
    int Kills, int Deaths, int Assists, int Cs,
    int? CsAt10, int? GoldDiffAt15,
    int? OpponentParticipantId, int? OpponentChampionId, long SyncedAt,
    double? KillParticipation = null, double? DamageShare = null, int? DeathsPre15 = null);
```

- [ ] **Step 4: Migration** — in `RiftReviewDb.RunVersionedMigrations`, bump `LatestSchemaVersion` to 3 and add after the `v < 2` block:

```csharp
        if (v < 3)
        {
            Exec(_conn, @"ALTER TABLE matches ADD COLUMN kill_participation REAL;
ALTER TABLE matches ADD COLUMN damage_share REAL;
ALTER TABLE matches ADD COLUMN deaths_pre15 INTEGER;");
            Exec(_conn, "PRAGMA user_version=3;");
        }
```

Also change `public const int LatestSchemaVersion = 2;` → `= 3;`.

- [ ] **Step 5: `UpsertMatch`** — add the three columns to the INSERT column list, the `VALUES` list, and the `ON CONFLICT DO UPDATE SET`, plus binds:

INSERT columns: append `,kill_participation,damage_share,deaths_pre15`; VALUES: append `,$kp,$ds,$pre15`; UPDATE set: append `,kill_participation=$kp,damage_share=$ds,deaths_pre15=$pre15`. Then binds:

```csharp
            Bind(c, "$kp", (object?)m.KillParticipation ?? DBNull.Value);
            Bind(c, "$ds", (object?)m.DamageShare ?? DBNull.Value);
            Bind(c, "$pre15", (object?)m.DeathsPre15 ?? DBNull.Value);
```

- [ ] **Step 6: `ReadRow`** — append three reads to the `new(...)` (after `synced_at`):

```csharp
        r.GetInt64(r.GetOrdinal("synced_at")),
        GetNullableDouble(r, "kill_participation"),
        GetNullableDouble(r, "damage_share"),
        GetNullableInt(r, "deaths_pre15"));
```

And add the helper next to `GetNullableInt`:

```csharp
    private static double? GetNullableDouble(SqliteDataReader r, string col)
    {
        var o = r.GetOrdinal(col);
        return r.IsDBNull(o) ? null : r.GetDouble(o);
    }
```

- [ ] **Step 7: Run to verify pass** — `dotnet test --filter RiftReviewDbTests`; Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/RiftReview.Core/Data/MatchRow.cs src/RiftReview.Core/Data/RiftReviewDb.cs tests/RiftReview.Core.Tests/RiftReviewDbTests.cs
git commit -m "feat(core): schema v3 — derived metric columns (KP, damage share, pre-15 deaths)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 4: Derived-metrics backfill (local, idempotent, no API)

**Files:**
- Modify: `src/RiftReview.Core/Data/RiftReviewDb.cs` (query + update APIs)
- Create: `src/RiftReview.Core/Sync/DerivedMetricsBackfill.cs`
- Test: `tests/RiftReview.Core.Tests/DerivedMetricsBackfillTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System.Text.Json;
using RiftReview.Core.Data;
using RiftReview.Core.Riot.Dtos;
using RiftReview.Core.Sync;
using Xunit;

public class DerivedMetricsBackfillTests
{
    [Fact]
    public void Backfills_rows_missing_derived_metrics_from_blobs()
    {
        using var db = RiftReviewDb.Open("Data Source=:memory:");
        db.SetMeta("puuid", "ME");

        // A row stored WITHOUT derived metrics (nulls), but with real-shaped blobs.
        var parts = new List<ParticipantDto>
        {
            new("ME", 3, 103, 100, "MIDDLE", true, 5, 2, 3, 200, 10, 1000),
            new("B",  1, 110, 100, "TOP",    true, 3, 1, 1, 150, 0,  600),
            new("X",  8, 145, 200, "MIDDLE", false,4, 4, 4, 180, 0,  900),
        };
        var match = new MatchDto(new MatchMetadata("NA1_1", parts.Select(p => p.Puuid).ToList()),
            new MatchInfo(420, 1_700_000_000_000, 1800, "15.12.1", parts));
        var tl = new TimelineDto(new TimelineMetadata("NA1_1", new()),
            new TimelineInfo(60000, new List<FrameDto>
            {
                new(0, new(), new List<EventDto> { new("CHAMPION_KILL", 6*60000L, 8, 3) }), // 1 pre-15 death
            }));
        var json = new JsonSerializerOptions();
        var row = new MatchRow("NA1_1", 420, 1, 1800, "15.12.1", 103, "MIDDLE", true,
            5, 2, 3, 210, 80, 150, 8, 145, 100);   // derived metrics left null
        db.UpsertMatch(row, JsonSerializer.Serialize(match, json), JsonSerializer.Serialize(tl, json));

        int filled = DerivedMetricsBackfill.Run(db);

        Assert.Equal(1, filled);
        var back = db.GetMatch("NA1_1")!;
        Assert.Equal((5 + 3) / 8.0, back.KillParticipation!.Value, 3);  // teamKills = 5+3 = 8
        Assert.Equal(1000 / 1600.0, back.DamageShare!.Value, 3);        // teamDmg = 1000+600 = 1600
        Assert.Equal(1, back.DeathsPre15);
        Assert.Equal(0, DerivedMetricsBackfill.Run(db));                // idempotent: nothing left
    }
}
```

- [ ] **Step 2: Run to verify failure** — `dotnet test --filter DerivedMetricsBackfillTests`; Expected: FAIL (APIs missing).

- [ ] **Step 3: Add DB APIs** to `RiftReviewDb`:

```csharp
    public IReadOnlyList<string> MatchIdsMissingDerivedMetrics()
    {
        using var c = _conn.CreateCommand();
        c.CommandText = "SELECT match_id FROM matches WHERE kill_participation IS NULL OR damage_share IS NULL OR deaths_pre15 IS NULL";
        var list = new List<string>();
        using var r = c.ExecuteReader();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    public void UpdateDerivedMetrics(string matchId, double killParticipation, double damageShare, int deathsPre15)
    {
        using var c = _conn.CreateCommand();
        c.CommandText = "UPDATE matches SET kill_participation=$kp, damage_share=$ds, deaths_pre15=$p WHERE match_id=$id";
        Bind(c, "$kp", killParticipation); Bind(c, "$ds", damageShare); Bind(c, "$p", deathsPre15); Bind(c, "$id", matchId);
        c.ExecuteNonQuery();
    }
```

- [ ] **Step 4: Create `DerivedMetricsBackfill.cs`**

```csharp
using System.Text.Json;
using RiftReview.Core.Analysis;
using RiftReview.Core.Data;
using RiftReview.Core.Riot.Dtos;

namespace RiftReview.Core.Sync;

// One-time, idempotent, local recompute of derived scalars (KP, damage share, pre-15 deaths)
// for pre-M2 rows. Reads the immutable stored blobs only; never re-fetches or mutates raw source.
public static class DerivedMetricsBackfill
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static int Run(RiftReviewDb db, IProgress<(int done, int total)>? progress = null)
    {
        var puuid = db.GetMeta("puuid");
        if (puuid is null) return 0;
        var ids = db.MatchIdsMissingDerivedMetrics();
        int done = 0;
        foreach (var id in ids)
        {
            var matchJson = db.GetMatchJson(id);
            var tlJson = db.GetTimelineJson(id);
            if (matchJson is null || tlJson is null) { progress?.Report((++done, ids.Count)); continue; }
            try
            {
                var match = JsonSerializer.Deserialize<MatchDto>(matchJson, Json)!;
                var tl = JsonSerializer.Deserialize<TimelineDto>(tlJson, Json)!;
                if (!match.Info.Participants.Any(p => p.Puuid == puuid)) { progress?.Report((++done, ids.Count)); continue; }
                var s = MatchExtractor.Summarize(match, puuid);
                int pre15 = TimelineExtractor.DeathsBeforeMinute(tl, s.MyParticipantId, 15);
                db.UpdateDerivedMetrics(id, s.KillParticipation, s.DamageShare, pre15);
            }
            catch { /* a single unparseable blob never aborts the backfill */ }
            progress?.Report((++done, ids.Count));
        }
        return db.MatchIdsMissingDerivedMetrics().Count == 0 ? ids.Count : ids.Count - db.MatchIdsMissingDerivedMetrics().Count;
    }
}
```

Note: the return is "rows filled this run"; the idempotent re-run returns 0 because no rows remain missing.

- [ ] **Step 5: Run to verify pass** — `dotnet test --filter DerivedMetricsBackfillTests`; Expected: PASS.

- [ ] **Step 6: Build + test + commit**

```bash
dotnet build RiftReview.slnx
git add src/RiftReview.Core/Data/RiftReviewDb.cs src/RiftReview.Core/Sync/DerivedMetricsBackfill.cs tests/RiftReview.Core.Tests/DerivedMetricsBackfillTests.cs
git commit -m "feat(core): one-time local backfill of derived metrics from stored blobs

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 5: `SyncService` populates the new scalars on new matches

**Files:**
- Modify: `src/RiftReview.Core/Sync/SyncService.cs`
- Test: `tests/RiftReview.Core.Tests/SyncServiceTests.cs`

- [ ] **Step 1: Write the failing test** (build a dedicated fake match with two team-100 members so the team aggregates are unambiguous):

```csharp
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
```

- [ ] **Step 2: Run to verify failure** — `dotnet test --filter Sync_stores_derived_metrics`; Expected: FAIL (row derived metrics are null).

- [ ] **Step 3: Implement** — in `SyncService.SyncAsync`, inside the loop, after `MatchExtractor.Summarize`, compute pre-15 and include all three in the `MatchRow`:

```csharp
                var s = MatchExtractor.Summarize(match, puuid);
                var cs10 = TimelineExtractor.CsAtMinute(timeline, s.MyParticipantId, 10);
                var g15 = TimelineExtractor.GoldDiffAtMinute(timeline, s.MyParticipantId, s.OpponentParticipantId, 15);
                int pre15 = TimelineExtractor.DeathsBeforeMinute(timeline, s.MyParticipantId, 15);
                var row = new MatchRow(id, s.QueueId, s.GameStartUtc, s.DurationS, s.Patch,
                    s.MyChampionId, s.MyTeamPosition, s.Win, s.Kills, s.Deaths, s.Assists, s.Cs,
                    cs10, g15, s.OpponentParticipantId, s.OpponentChampionId, DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    s.KillParticipation, s.DamageShare, pre15);
```

- [ ] **Step 4: Run to verify pass** — `dotnet test --filter Sync_stores_derived_metrics`; Expected: PASS. Then `dotnet test` (full) — confirm the existing sync tests still pass.

- [ ] **Step 5: Commit**

```bash
git add src/RiftReview.Core/Sync/SyncService.cs tests/RiftReview.Core.Tests/SyncServiceTests.cs
git commit -m "feat(core): sync populates derived metrics on new matches

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 6: `SettingsStore.TrendWindow`

**Files:**
- Modify: `src/RiftReview.Core/Configuration/SettingsStore.cs`
- Test: `tests/RiftReview.Core.Tests/SettingsStoreTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
    [Fact]
    public void Trend_window_defaults_to_10_and_clamps()
    {
        using var db = RiftReviewDb.Open("Data Source=:memory:");
        var s = new SettingsStore(db);
        Assert.Equal(10, s.TrendWindow);
        s.TrendWindow = 3;   Assert.Equal(SettingsStore.MinTrendWindow, new SettingsStore(db).TrendWindow);
        s.TrendWindow = 999; Assert.Equal(SettingsStore.MaxTrendWindow, new SettingsStore(db).TrendWindow);
        s.TrendWindow = 14;  Assert.Equal(14, new SettingsStore(db).TrendWindow);
    }
```

- [ ] **Step 2: Run to verify failure** — `dotnet test --filter Trend_window_defaults_to_10_and_clamps`; Expected: FAIL.

- [ ] **Step 3: Implement** — add to `SettingsStore`:

```csharp
    public const int MinTrendWindow = 5, MaxTrendWindow = 30, DefaultTrendWindow = 10;

    public int TrendWindow
    {
        get => int.TryParse(_db.GetMeta("settings.trend_window"), out var v) ? Math.Clamp(v, MinTrendWindow, MaxTrendWindow) : DefaultTrendWindow;
        set => _db.SetMeta("settings.trend_window", Math.Clamp(value, MinTrendWindow, MaxTrendWindow).ToString());
    }
```

- [ ] **Step 4: Run to verify pass** — `dotnet test --filter Trend_window_defaults_to_10_and_clamps`; Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/RiftReview.Core/Configuration/SettingsStore.cs tests/RiftReview.Core.Tests/SettingsStoreTests.cs
git commit -m "feat(core): trend-window setting (default 10, clamp 5-30)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 7: `TrendModels` + `ChampTrendCalculator` (pure)

**Files:**
- Create: `src/RiftReview.Core/Analysis/TrendModels.cs`
- Create: `src/RiftReview.Core/Analysis/ChampTrendCalculator.cs`
- Test: `tests/RiftReview.Core.Tests/ChampTrendCalculatorTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using RiftReview.Core.Analysis;
using RiftReview.Core.Data;
using Xunit;

public class ChampTrendCalculatorTests
{
    // helper: build a ranked K'Sante row with a given win + cs10 + deaths, ordered by gameStart
    private static MatchRow Row(long start, bool win, int cs10, int deaths) =>
        new("NA1_" + start, 420, start, 1800, "15.12", 103, "MIDDLE", win,
            5, deaths, 5, 200, cs10, 100, 8, 145, 100, 0.6, 0.25, 1);

    [Fact]
    public void Improving_cs10_when_recent_block_beats_prior_block()
    {
        // 20 games (N=10): prior 10 average cs10 = 60, recent 10 average = 75 -> improving
        var games = new List<MatchRow>();
        for (int i = 0; i < 10; i++) games.Add(Row(i, true, 60, 3));        // oldest (prior block)
        for (int i = 10; i < 20; i++) games.Add(Row(i, true, 75, 3));       // newest (current block)
        games.Reverse();                                                    // calculator expects newest-first
        var t = ChampTrendCalculator.Build(games, n: 10);
        var cs = t.Metrics.Single(m => m.Key == "cs10");
        Assert.Equal(TrendVerdict.Improving, cs.Verdict);
        Assert.Equal(75, cs.Current!.Value, 1);
        Assert.Equal(60, cs.Prior!.Value, 1);
    }

    [Fact]
    public void Deaths_down_reads_as_improving()
    {
        var games = new List<MatchRow>();
        for (int i = 0; i < 10; i++) games.Add(Row(i, true, 70, 6));   // prior: 6 deaths
        for (int i = 10; i < 20; i++) games.Add(Row(i, true, 70, 3));  // recent: 3 deaths (better)
        games.Reverse();
        var t = ChampTrendCalculator.Build(games, n: 10);
        Assert.Equal(TrendVerdict.Improving, t.Metrics.Single(m => m.Key == "deaths").Verdict);
    }

    [Fact]
    public void Building_when_fewer_than_two_windows()
    {
        var games = new List<MatchRow>();
        for (int i = 0; i < 12; i++) games.Add(Row(i, true, 70, 3)); // 12 < 2N(20)
        games.Reverse();
        var t = ChampTrendCalculator.Build(games, n: 10);
        Assert.Equal(TrendVerdict.Building, t.Metrics.Single(m => m.Key == "cs10").Verdict);
    }

    [Fact]
    public void Steady_within_deadband()
    {
        var games = new List<MatchRow>();
        for (int i = 0; i < 10; i++) games.Add(Row(i, true, 70, 3));
        for (int i = 10; i < 20; i++) games.Add(Row(i, true, 71, 3)); // +1 cs < floor(2) -> steady
        games.Reverse();
        var t = ChampTrendCalculator.Build(games, n: 10);
        Assert.Equal(TrendVerdict.Steady, t.Metrics.Single(m => m.Key == "cs10").Verdict);
    }

    [Fact]
    public void Eligible_champions_need_two_windows()
    {
        var rows = new List<MatchRow>();
        for (int i = 0; i < 25; i++) rows.Add(Row(i, true, 70, 3));              // champ 103: 25 games
        for (int i = 25; i < 30; i++) rows.Add(new MatchRow("NA1_" + i, 420, i, 1800, "15.12", 7, "MIDDLE", true, 1,1,1,100, 60,0,8,99,100)); // champ 7: 5 games
        var eligible = ChampTrendCalculator.EligibleChampions(rows, n: 10);
        Assert.Contains(103, eligible);
        Assert.DoesNotContain(7, eligible);
    }
}
```

- [ ] **Step 2: Run to verify failure** — `dotnet test --filter ChampTrendCalculatorTests`; Expected: FAIL (types missing).

- [ ] **Step 3: Create `TrendModels.cs`**

```csharp
namespace RiftReview.Core.Analysis;

public enum TrendVerdict { Improving, Steady, Declining, Building }

public sealed record MetricTrend(
    string Key, string DisplayName, string Unit, int Direction,
    double? Current, double? Prior, double ImprovementDelta,
    TrendVerdict Verdict, IReadOnlyList<double?> RollingSeries);

public sealed record ChampTrend(int ChampionId, int Games, IReadOnlyList<MetricTrend> Metrics);
```

- [ ] **Step 4: Create `ChampTrendCalculator.cs`**

```csharp
using RiftReview.Core.Data;

namespace RiftReview.Core.Analysis;

public static class ChampTrendCalculator
{
    private sealed record Def(string Key, string Name, string Unit, int Dir, double Floor, Func<MatchRow, double?> Sel);

    // Floors are the dead-band half-width in each metric's own units.
    private static readonly Def[] Defs =
    {
        new("winRate", "Win rate",            "%", +1, 0.05, m => m.Win ? 1.0 : 0.0),
        new("cs10",    "CS @ 10",             "",  +1, 2.0,  m => m.CsAt10),
        new("gold15",  "Gold @ 15 (vs lane)", "g", +1, 75.0, m => m.GoldDiffAt15),
        new("kda",     "KDA",                 "",  +1, 0.3,  m => (m.Kills + m.Assists) / (double)Math.Max(1, m.Deaths)),
        new("deaths",  "Deaths / game",       "",  -1, 0.4,  m => m.Deaths),
        new("pre15",   "Pre-15 deaths",       "",  -1, 0.3,  m => m.DeathsPre15.HasValue ? m.DeathsPre15.Value : (double?)null),
        new("kp",      "Kill participation",  "%", +1, 0.03, m => m.KillParticipation),
        new("dmg",     "Damage share",        "%", +1, 0.02, m => m.DamageShare),
    };

    public static IReadOnlyList<int> EligibleChampions(IReadOnlyList<MatchRow> rankedMatches, int n) =>
        rankedMatches.GroupBy(m => m.MyChampionId)
            .Where(g => g.Count() >= 2 * n)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key).ToList();

    public static ChampTrend Build(IReadOnlyList<MatchRow> champGames, int n)
    {
        var games = champGames.OrderBy(m => m.GameStartUtc).ToList(); // oldest -> newest
        int k = games.Count;
        var metrics = new List<MetricTrend>(Defs.Length);
        foreach (var d in Defs)
        {
            var vals = games.Select(d.Sel).ToList();
            var rolling = Rolling(vals, n);
            double? current = Block(vals, k - n, n);
            double? prior   = Block(vals, k - 2 * n, n);
            double delta = (current.HasValue && prior.HasValue) ? d.Dir * (current.Value - prior.Value) : 0.0;
            metrics.Add(new MetricTrend(d.Key, d.Name, d.Unit, d.Dir, current, prior, delta,
                Classify(current, prior, k, n, delta, d.Floor), rolling));
        }
        return new ChampTrend(k > 0 ? games[^1].MyChampionId : 0, k, metrics);
    }

    private static TrendVerdict Classify(double? cur, double? prior, int k, int n, double delta, double floor)
    {
        if (cur is null || prior is null || k < 2 * n) return TrendVerdict.Building;
        if (delta > floor) return TrendVerdict.Improving;
        if (delta < -floor) return TrendVerdict.Declining;
        return TrendVerdict.Steady;
    }

    // Average of non-null values in [start, start+count); null if window starts before 0 or holds no values.
    private static double? Block(IReadOnlyList<double?> vals, int start, int count)
    {
        if (start < 0) return null;
        double sum = 0; int n = 0;
        for (int i = start; i < start + count && i < vals.Count; i++)
            if (vals[i].HasValue) { sum += vals[i]!.Value; n++; }
        return n == 0 ? null : sum / n;
    }

    private static IReadOnlyList<double?> Rolling(IReadOnlyList<double?> vals, int n)
    {
        var outp = new List<double?>(vals.Count);
        for (int i = 0; i < vals.Count; i++)
        {
            double sum = 0; int c = 0;
            for (int j = Math.Max(0, i - n + 1); j <= i; j++)
                if (vals[j].HasValue) { sum += vals[j]!.Value; c++; }
            outp.Add(c == 0 ? (double?)null : sum / c);
        }
        return outp;
    }
}
```

- [ ] **Step 5: Run to verify pass** — `dotnet test --filter ChampTrendCalculatorTests`; Expected: PASS.

- [ ] **Step 6: Build + test + commit**

```bash
dotnet build RiftReview.slnx
git add src/RiftReview.Core/Analysis/TrendModels.cs src/RiftReview.Core/Analysis/ChampTrendCalculator.cs tests/RiftReview.Core.Tests/ChampTrendCalculatorTests.cs
git commit -m "feat(core): ChampTrendCalculator — rolling-window per-champ metric trends + verdicts

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 8: `TrendsViewModel`

**Files:**
- Create: `src/RiftReview.App/ViewModels/TrendsViewModel.cs`
- Create: `src/RiftReview.App/ViewModels/MetricTrendViewModel.cs`
- Test: `tests/RiftReview.App.Tests/TrendsViewModelTests.cs`

`MetricTrendViewModel` is a thin display wrapper (formats current value + delta by unit, maps verdict→label/brush-key, exposes the rolling series as `double?[]` for the Sparkline).

- [ ] **Step 1: Write the failing test**

```csharp
using System.Linq;
using RiftReview.App.ViewModels;
using RiftReview.Core.Configuration;
using RiftReview.Core.Data;
using RiftReview.Core.DataDragon;
using Xunit;

public class TrendsViewModelTests
{
    private static MatchRow Row(long start, int champ, bool win, int cs10) =>
        new("NA1_" + start, 420, start, 1800, "15.12", champ, "MIDDLE", win,
            5, 3, 5, 200, cs10, 100, 8, 145, 100, 0.6, 0.25, 1);

    [Fact]
    public void Lists_eligible_champ_and_builds_metric_rows()
    {
        using var db = RiftReviewDb.Open("Data Source=:memory:");
        db.SetMeta("puuid", "ME");
        for (int i = 0; i < 10; i++) db.UpsertMatch(Row(i, 103, true, 60), "{}", "{}");
        for (int i = 10; i < 20; i++) db.UpsertMatch(Row(i, 103, true, 75), "{}", "{}");

        var vm = new TrendsViewModel(db, new DataDragonClient(new System.Net.Http.HttpClient(), System.IO.Path.GetTempPath()), new SettingsStore(db));
        vm.Load();

        Assert.Contains(vm.Champions, c => c.ChampionId == 103);
        Assert.NotEmpty(vm.Metrics);
        Assert.Contains(vm.Metrics, m => m.Key == "cs10" && m.Verdict == "Improving");
    }
}
```

- [ ] **Step 2: Run to verify failure** — `dotnet test --filter TrendsViewModelTests`; Expected: FAIL.

- [ ] **Step 3: Implement `MetricTrendViewModel.cs`**

```csharp
using System;
using System.Collections.Generic;
using RiftReview.Core.Analysis;

namespace RiftReview.App.ViewModels;

public sealed class MetricTrendViewModel
{
    public MetricTrendViewModel(MetricTrend t)
    {
        Key = t.Key;
        DisplayName = t.DisplayName;
        Current = Format(t.Current, t.Unit);
        Verdict = t.Verdict switch
        {
            TrendVerdict.Improving => "Improving",
            TrendVerdict.Declining => "Declining",
            TrendVerdict.Building => "Building",
            _ => "Steady",
        };
        IsGood = t.Verdict == TrendVerdict.Improving;
        IsBad = t.Verdict == TrendVerdict.Declining;
        Delta = t.Verdict == TrendVerdict.Building || !t.Current.HasValue || !t.Prior.HasValue
            ? "" : FormatDelta(t.ImprovementDelta, t.Direction, t.Unit);
        Series = t.RollingSeries;
    }

    public string Key { get; }
    public string DisplayName { get; }
    public string Current { get; }
    public string Verdict { get; }
    public string Delta { get; }
    public bool IsGood { get; }
    public bool IsBad { get; }
    public IReadOnlyList<double?> Series { get; }

    private static string Format(double? v, string unit) => v is null ? "—"
        : unit == "%" ? Math.Round(v.Value * 100) + "%"
        : unit == "g" ? (v.Value >= 0 ? "+" : "") + Math.Round(v.Value) + "g"
        : Math.Round(v.Value, 1).ToString();

    // ImprovementDelta is already sign-adjusted (positive = better). Show the raw metric change.
    private static string FormatDelta(double improvementDelta, int dir, string unit)
    {
        double raw = improvementDelta * dir;                 // back to metric units, signed
        string sign = raw > 0 ? "+" : raw < 0 ? "" : "";
        return unit == "%" ? sign + Math.Round(raw * 100) + " pts"
            : unit == "g" ? sign + Math.Round(raw) + "g"
            : sign + Math.Round(raw, 1);
    }
}
```

- [ ] **Step 4: Implement `TrendsViewModel.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using RiftReview.Core.Analysis;
using RiftReview.Core.Configuration;
using RiftReview.Core.Data;
using RiftReview.Core.DataDragon;

namespace RiftReview.App.ViewModels;

public sealed partial class TrendsViewModel : ObservableObject
{
    private readonly RiftReviewDb _db;
    private readonly DataDragonClient _ddragon;
    private readonly SettingsStore _settings;

    public TrendsViewModel(RiftReviewDb db, DataDragonClient ddragon, SettingsStore settings)
    {
        _db = db; _ddragon = ddragon; _settings = settings;
    }

    public ObservableCollection<ChampChoice> Champions { get; } = new();
    public ObservableCollection<MetricTrendViewModel> Metrics { get; } = new();

    [ObservableProperty] private ChampChoice? _selectedChampion;
    [ObservableProperty] private MetricTrendViewModel? _selectedMetric;   // drill-down target
    [ObservableProperty] private string _lpHeadline = "";
    [ObservableProperty] private bool _hasLp;
    [ObservableProperty] private bool _isEmpty;

    public sealed record ChampChoice(int ChampionId, string Name);

    // NOTE: the View calls InitializeWithBackfillAsync() (added in Task 11) from its Loaded handler;
    // that runs the one-time backfill, ensures Data Dragon names, then calls Load(). The Task 8 test
    // calls Load() directly (champ names fall back to placeholders when Data Dragon isn't loaded).
    public void Load()
    {
        int n = _settings.TrendWindow;
        var ranked = _db.AllMatches(rankedOnly: true);
        var eligible = ChampTrendCalculator.EligibleChampions(ranked, n);

        Champions.Clear();
        foreach (var id in eligible) Champions.Add(new ChampChoice(id, _ddragon.ChampionName(id)));
        IsEmpty = Champions.Count == 0;

        LoadLp();

        SelectedChampion = Champions.FirstOrDefault();   // triggers OnSelectedChampionChanged
        if (SelectedChampion is null) Metrics.Clear();
    }

    partial void OnSelectedChampionChanged(ChampChoice? value)
    {
        Metrics.Clear();
        if (value is null) return;
        int n = _settings.TrendWindow;
        var games = _db.AllMatches(rankedOnly: true).Where(m => m.MyChampionId == value.ChampionId).ToList();
        var trend = ChampTrendCalculator.Build(games, n);
        foreach (var m in trend.Metrics) Metrics.Add(new MetricTrendViewModel(m));
        SelectedMetric = Metrics.FirstOrDefault();
    }

    private void LoadLp()
    {
        var snaps = _db.GetLpSnapshots();
        var solo = snaps.Where(s => s.QueueType == "RANKED_SOLO_5x5").ToList();
        if (solo.Count == 0) { HasLp = false; LpHeadline = ""; return; }
        var latest = solo[^1];
        HasLp = true;
        LpHeadline = $"{Cap(latest.Tier)} {latest.Division} · {latest.LeaguePoints} LP";
    }

    private static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..].ToLower();
}
```

- [ ] **Step 5: Run to verify pass** — `dotnet test --filter TrendsViewModelTests`; Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/RiftReview.App/ViewModels/TrendsViewModel.cs src/RiftReview.App/ViewModels/MetricTrendViewModel.cs tests/RiftReview.App.Tests/TrendsViewModelTests.cs
git commit -m "feat(app): TrendsViewModel — eligible champs, metric rows, LP headline

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 9: `Sparkline` double-series support

**Files:**
- Modify: `src/RiftReview.App/Controls/Sparkline.cs`

The row sparklines plot `double?` rolling series. Add a `Values` DP and prefer it in `OnRender`; keep the existing `Points` (int?) DP so M1's Champions page is untouched.

- [ ] **Step 1: Add a `Values` DP and render-from-double**

```csharp
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<double?>), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<double?>? Values
    {
        get => (IReadOnlyList<double?>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }
```

In `OnRender`, replace the first line that builds `pts` with a source that prefers `Values`:

```csharp
        var pts = (Values is not null
                ? Values.Where(v => v.HasValue).Select(v => v!.Value)
                : Points?.Where(v => v.HasValue).Select(v => (double)v!.Value))
            ?.ToList();
        if (pts is null || pts.Count < 2) return;
```

(The rest of `OnRender` is unchanged.)

- [ ] **Step 2: Build** — `dotnet build RiftReview.slnx`; Expected: clean. (No behavior change for M1's `Points` usage; visual confirmed in Task 12.)

- [ ] **Step 3: Commit**

```bash
git add src/RiftReview.App/Controls/Sparkline.cs
git commit -m "feat(app): Sparkline supports a double? value series

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 10: Settings — trend-window control

**Files:**
- Modify: `src/RiftReview.App/ViewModels/SettingsViewModel.cs`
- Modify: `src/RiftReview.App/Views/SettingsView.xaml`
- Test: `tests/RiftReview.App.Tests/SettingsViewModelTests.cs`

- [ ] **Step 1: Extend the failing test** — add to `SettingsViewModelTests`:

```csharp
    [Fact]
    public void Trend_window_persists_to_store()
    {
        using var db = RiftReviewDb.Open("Data Source=:memory:");
        var store = new SettingsStore(db);
        var vm = new SettingsViewModel(store);
        Assert.Equal(10, vm.TrendWindow);
        vm.TrendWindow = 14;
        Assert.Equal(14, new SettingsStore(db).TrendWindow);
    }
```

- [ ] **Step 2: Run to verify failure** — `dotnet test --filter Trend_window_persists_to_store`; Expected: FAIL.

- [ ] **Step 3: Extend `SettingsViewModel`** — add an observable property + passthrough bounds + persistence (mirror `MatchDepth`):

```csharp
    [ObservableProperty] private int _trendWindow;
    public int MinTrendWindow => SettingsStore.MinTrendWindow;
    public int MaxTrendWindow => SettingsStore.MaxTrendWindow;

    partial void OnTrendWindowChanged(int value)
    {
        _store.TrendWindow = value;
        if (_store.TrendWindow != value) { _trendWindow = _store.TrendWindow; OnPropertyChanged(nameof(TrendWindow)); }
    }
```

And in the ctor, after the existing seeds: `_trendWindow = store.TrendWindow;`

- [ ] **Step 4: Add the control to `SettingsView.xaml`** — a labelled slider mirroring the match-depth one, bound `Value="{Binding TrendWindow, Mode=TwoWay}"`, `Minimum="{Binding MinTrendWindow}"`, `Maximum="{Binding MaxTrendWindow}"`, with a live value `TextBlock Text="{Binding TrendWindow}"` and a helper line ("Games per rolling window used for trend verdicts."), styled with the same brushes (`PanelBgBrush`/`AccentBrush`/`TextPrimaryBrush`/`TextMutedBrush`/`HairlineBrush`).

- [ ] **Step 5: Build + test** — `dotnet build RiftReview.slnx`; `dotnet test`; Expected: clean + green.

- [ ] **Step 6: Commit**

```bash
git add src/RiftReview.App/ViewModels/SettingsViewModel.cs src/RiftReview.App/Views/SettingsView.xaml tests/RiftReview.App.Tests/SettingsViewModelTests.cs
git commit -m "feat(app): Settings — trend window control

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 11: `TrendsView` (layout C) + nav + DI + startup backfill

**Files:**
- Create: `src/RiftReview.App/Views/TrendsView.xaml` (+ `.xaml.cs`)
- Modify: `src/RiftReview.App/AppShell.xaml` (+ `.xaml.cs`)
- Modify: `src/RiftReview.App/App.xaml.cs`
- Modify: `src/RiftReview.App/ViewModels/TrendsViewModel.cs` (backfill "preparing" state)

- [ ] **Step 1: Backfill state on the VM** — add to `TrendsViewModel`:

```csharp
    [ObservableProperty] private bool _isPreparing;
    [ObservableProperty] private string _prepareStatus = "";
```

Add a method that runs the one-time backfill off the UI thread, then loads:

```csharp
    public async Task InitializeWithBackfillAsync()
    {
        try { await _ddragon.EnsureLoadedAsync(); } catch { }
        if (_db.MatchIdsMissingDerivedMetrics().Count > 0)
        {
            IsPreparing = true;
            PrepareStatus = "Preparing trends data…";
            await Task.Run(() => RiftReview.Core.Sync.DerivedMetricsBackfill.Run(_db,
                new Progress<(int done, int total)>(p => PrepareStatus = $"Preparing trends data… {p.done}/{p.total}")));
            IsPreparing = false;
        }
        Load();
    }
```

(The view calls `InitializeWithBackfillAsync` from `Loaded`. `Progress<T>` marshals to the UI thread since it's constructed on it.)

- [ ] **Step 2: `TrendsView.xaml`** — layout C on a `PanelBgBrush` surface inside a `ScrollViewer`:
  - Header: gold "Improvement trends" title; account LP strip (`Visibility` bound to `HasLp`) showing `LpHeadline`.
  - "Preparing" overlay/text bound to `IsPreparing` (`PrepareStatus`).
  - Champ chips: `ItemsControl` over `Champions` in a `WrapPanel`; each a selectable button setting `SelectedChampion` (use a `ListBox` with `SelectionMode=Single`, `ItemsSource={Binding Champions}`, `SelectedItem={Binding SelectedChampion, Mode=TwoWay}`, styled as gold pill chips via `ItemContainerStyle`).
  - Trajectory rows: `ItemsControl` over `Metrics`; each row a `Grid`/`DockPanel`: `DisplayName` + `Current` (left), `controls:Sparkline Values="{Binding Series}"` (center, Height ~28), verdict label + `Delta` (right). Verdict color via a `BoolToBrushConverter` on `IsGood` (win-green) / a second on `IsBad` (loss-red), default muted — reuse `WinBrush`/`TextMutedBrush`; add a loss brush key if missing.
  - Empty state (`IsEmpty`): "Play more ranked games on a champ (≥ 2× the trend window) to see trends."
  - Selected-metric drill panel (bound to `SelectedMetric`): a larger `Sparkline Values="{Binding SelectedMetric.Series}"` (Height ~120) under the rows, with the metric name + current; clicking a row sets `SelectedMetric` (bind row click via `ListBox`/`Button`).

  Use the same XAML namespaces as `ChampPoolView.xaml` (`ui`, `conv`, `controls`).

- [ ] **Step 3: `TrendsView.xaml.cs`** — DI ctor (mirror `ChampPoolView`):

```csharp
using System.Windows.Controls;
using RiftReview.App.ViewModels;

namespace RiftReview.App.Views;

public partial class TrendsView : UserControl
{
    public TrendsView(TrendsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Loaded += async (_, _) => await vm.InitializeWithBackfillAsync();
    }
}
```

- [ ] **Step 4: Nav** — in `AppShell.xaml`, add a `NavigationViewItem Content="Trends" TargetPageType="{x:Type views:TrendsView}"` (with a `ui:SymbolIcon Symbol="DataTrending24"` or another valid WPF-UI symbol — verify the symbol name compiles; fall back to `ChartMultiple24`) between Champions and the Settings footer item. In `AppShell.xaml.cs`, extend the `--page` switch with `"trends" => typeof(TrendsView),`.

- [ ] **Step 5: DI** — in `App.xaml.cs` `ConfigureServices`, register beside the other pages:

```csharp
                s.AddTransient<ViewModels.TrendsViewModel>();
                s.AddTransient<TrendsView>();
```

- [ ] **Step 6: Build + smoke** — `dotnet build RiftReview.slnx -c Debug`; Expected: clean. (Full visual verification in Task 12.)

- [ ] **Step 7: Commit**

```bash
git add src/RiftReview.App/Views/TrendsView.xaml src/RiftReview.App/Views/TrendsView.xaml.cs src/RiftReview.App/AppShell.xaml src/RiftReview.App/AppShell.xaml.cs src/RiftReview.App/App.xaml.cs src/RiftReview.App/ViewModels/TrendsViewModel.cs
git commit -m "feat(app): Trends page (layout C) + nav + startup backfill

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 12: Demo seeder — a trendable champ

**Files:**
- Modify: `src/RiftReview.App/Demo/DemoSeeder.cs`

The Trends page needs a champ with ≥ 2N (≈20) ranked games whose metrics drift over time, and non-zero champion damage so damage share isn't flat. Extend the seed so champ 103 (Ahri) has 24 games with an improving CS@10 and damage values.

- [ ] **Step 1: Give demo participants champion damage** — in `BuildGame`, set `TotalDamageDealtToChampions` on the participants (use the optional 12th arg). For "ME" (pid 3) scale damage with the game index so damage share trends; give teammates/enemies fixed damage. Example: change the `ME` participant construction to include a damage figure like `9000 + i * 150` and others `8000`.

- [ ] **Step 2: Concentrate 24 games on champ 103 with a CS@10 ramp** — change the `plan` array so champ 103 appears ≥24 times, and make `BuildGame` raise the "ME" CS accumulation with `i` so trailing-window CS@10 improves across the series (oldest games lower CS, newest higher). Keep the other one-off champs.

```csharp
        int[] plan =
        {
            103,103,103,103,103,103,103,103,103,103,103,103,
            103,103,103,103,103,103,103,103,103,103,103,103,
            157,157,157,157,157,157,157,157, 7, 238, 99, 142
        };
```

(Adjust `BuildGame`'s CS-per-minute factor to depend on `i` so newer games — lower `i`, since seeding goes newest-first via `baseCreation - i*day` — have higher CS@10; verify the direction so the demo reads as "improving".)

- [ ] **Step 3: Build + smoke under demo** — `dotnet build RiftReview.slnx -c Debug`; optionally run `RiftReview.App.exe --seed-demo --page trends` to eyeball: Trends lists Ahri, rows render with sparklines + verdicts.

- [ ] **Step 4: Commit**

```bash
git add src/RiftReview.App/Demo/DemoSeeder.cs
git commit -m "feat(app): demo seeder seeds a trendable champ (24 games, damage values)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 13: Screenshot verification gate (real data + demo)

**Files:** none (verification only).

- [ ] **Step 1: Build Debug** — `dotnet build RiftReview.slnx -c Debug`; Expected: clean.

- [ ] **Step 2: Verify on the real DB** — the owner's `%LOCALAPPDATA%\RiftReview\riftreview.db` holds 631 games (408 ranked). First launch triggers the one-time derived-metrics backfill. **Inside a Sonnet subagent** (PrintWindow flag 2; HW-accel-off registry workaround set+restored; return a TEXT verdict + PNG paths — do NOT load PNGs into the main context), launch `RiftReview.App.exe --page trends` and verify:
  - The "Preparing trends data… N/631" state appears then completes (backfill runs once).
  - Nav rail shows Review / Champions / Trends / Settings; switching works.
  - **Trends:** champ chips list real mains (K'Sante etc.); selecting one renders the 8 trajectory rows with smoothed gold sparklines, sensible current values, and improving/steady/declining/building verdicts (colors: improving green, declining red); the account LP strip shows the current standing; clicking a row updates the larger drill chart.
  - No crash / empty-state-with-data / error banner.
  - Re-launch once more: no "preparing" state (backfill already done — idempotent).

- [ ] **Step 3: Verify under `--seed-demo`** — relaunch with `--seed-demo --page trends`; confirm Ahri lists with rendered trajectory rows. Same subagent text-verdict method.

- [ ] **Step 4: Settings check** — `--page settings`: confirm both the match-depth and the new trend-window controls render and are editable.

- [ ] **Step 5: Holistic review + commit (if any fixes)** — run a final spec-coverage pass; fix any defects in their own commits. No code change → no commit.

---

## Acceptance criteria (from spec §13)

- `dotnet build RiftReview.slnx` clean (0 warnings); `dotnet test` all green.
- Schema at v3; a v2 DB upgrades in place without data loss; derived metrics backfilled for existing rows (idempotent, zero Riot calls).
- Sync populates KP / damage share / pre-15 deaths on new matches.
- Trends page lists champs with ≥2N ranked games; per metric shows the trailing-N value, a smoothed rolling sparkline, the correct (good-direction-aware) verdict, and the delta; a row click drives the larger chart.
- Account LP strip shows current standing or hides when no snapshots.
- Trend window N is editable in Settings and re-computes the view.
- No secrets added; `appsettings.json` placeholders only.

## Hand-back (cannot verify here — owner runs locally)

- Sanity-check the backfilled derived metrics on real data (KP / damage share plausible; pre-15 deaths reasonable).
- Confirm verdicts + sparklines look right for a known main (e.g. K'Sante) across the 408-ranked dataset.
- LP-strip delta becomes meaningful only as `lp_snapshots` accumulate over real calendar time (current snapshots cluster on backfill days).
