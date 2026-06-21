# M10 — "By game phase" (item 20) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **Written for Sonnet execution; if something doesn't match the code you find, STOP and report rather than guess.** All paths absolute. Build quietly: `dotnet build RiftReview.slnx -v minimal`.

**Goal:** Add a "By game phase" card to the per-match deep-dive that splits the game into Early/Mid/Late and shows, per phase, four decomposed metrics (gold-diff Δ, CS/min, deaths, kill participation) each with a signed delta vs the player's own same-role average — never a composite, never a fabricated number.

**Architecture:** Two new pure Core units (`TimelineExtractor.BuildPhaseBreakdown` + `PhaseBaselineCalculator`) computed on demand from the stored `timeline_json` blob (no schema change). The deep-dive view-model's existing 20-game same-role baseline loop is extended to also collect per-phase baselines (zero extra timeline I/O). A new XAML card renders Layout A. The demo seeder gains player-credited team kills so KP isn't empty.

**Tech Stack:** C#/.NET 10, WPF (+ CommunityToolkit.Mvvm `[ObservableProperty]`), xUnit. Build: `dotnet build RiftReview.slnx -v minimal`. Test: `dotnet test RiftReview.slnx`.

**Spec:** `docs/superpowers/specs/2026-06-20-riftreview-m10-phase-breakdown-design.md`

---

## Verified ground truth (do not re-derive)

- `MatchSummary` (in `AnalysisModels.cs`) already has `MyParticipantId` (int), `MyTeamId` (int), `OpponentParticipantId` (int?). **No record change needed.**
- `TimelineExtractor` has private `TeamOf(int)` and `TeamOfNullable(int?)` (1–5 → 100, 6–10 → 200; null/≤0 → null). `ParticipantFrames` is keyed by **string pid** (`pid.ToString()`).
- `DeepDive.GoldDiffVsTeam` is a per-minute `IReadOnlyList<ChartPoint>` (signed, + = my team ahead). `ChartPoint` = `readonly record struct(double Minute, double Value)`.
- `ParticipantFrameDto(int ParticipantId, int TotalGold, int MinionsKilled, int JungleMinionsKilled)`. `EventDto(string Type, long Timestamp, int? KillerId, int? VictimId, …, List<int>? AssistingParticipantIds = null, int? ParticipantId = null, int? ItemId = null)`. `FrameDto(long Timestamp, Dictionary<string,ParticipantFrameDto> ParticipantFrames, List<EventDto> Events)`. `TimelineDto(TimelineMetadata Metadata, TimelineInfo Info)`, `TimelineInfo(long FrameInterval, List<FrameDto> Frames, …)`.
- `DeepDiveViewModel.Load` is **fully synchronous** — assign new data exactly like `CsBaseline` (a `[ObservableProperty]`). The VM already `using`s `System.Windows.Media` (it builds frozen `SolidColorBrush`es).
- xUnit, `snake_case` test method names, inline `TimelineDto` builders keyed by string pid (see `TimelineExtractorCausalityTests`).
- Demo seeder: `src/RiftReview.App/Demo/DemoSeeder.cs`; player pid = **3**, enemy mid pid = **8**; per-game `frameList[minute].Events.Add(...)`; games are 25–35 min. Currently the only `CHAMPION_KILL` is killer=8/victim=3 — **no player-credited kills exist**.

---

## Task 1: Add `PhaseStat` and `PhaseBaseline` records

**Files:**
- Modify: `C:\Agent Projects\RiftReview\src\RiftReview.Core\Analysis\AnalysisModels.cs`

- [ ] **Step 1: Append the two records** to the end of `AnalysisModels.cs` (after `CausalityResult`):

```csharp

/// One game phase for one match. All metrics exact (no baseline applied here).
/// KillParticipation is null when the player's team scored no kills in the phase (never fabricate).
public sealed record PhaseStat(
    string Label,            // "Early" | "Mid" | "Late"
    double StartMinute,      // 0, 10, 20
    double EndMinute,        // capped at game end (partial phases allowed)
    double GoldDiffDelta,    // team gold-diff change across the phase (+ = my team gained)
    double CsPerMinute,      // CS gained in the phase / phase duration (minutes)
    int Deaths,              // my deaths in the phase
    int DeathsWhileBehind,   // subset of Deaths where team gold-diff < 0 at the death
    int Kills,               // my kills in the phase
    int Assists,             // my assists in the phase
    int TeamKills,           // my team's kills in the phase (KP denominator)
    double? KillParticipation); // (Kills+Assists)/TeamKills, null if TeamKills == 0

/// Per-phase own same-role baseline. Each metric is independently null when fewer than
/// `minGames` prior games contributed a value for it (never fabricate).
public sealed record PhaseBaseline(
    string Label,
    double? GoldDiffDelta,
    double? CsPerMinute,
    double? Deaths,
    double? KillParticipation);
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build RiftReview.slnx -v minimal`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/RiftReview.Core/Analysis/AnalysisModels.cs
git commit -m "feat(core): add PhaseStat and PhaseBaseline records for M10

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 2: `TimelineExtractor.BuildPhaseBreakdown` (pure, TDD)

**Files:**
- Modify: `C:\Agent Projects\RiftReview\src\RiftReview.Core\Analysis\TimelineExtractor.cs`
- Test: `C:\Agent Projects\RiftReview\tests\RiftReview.Core.Tests\TimelineExtractorPhaseTests.cs` (create)

- [ ] **Step 1: Write the failing tests**

Create `tests\RiftReview.Core.Tests\TimelineExtractorPhaseTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using RiftReview.Core.Analysis;
using RiftReview.Core.Riot.Dtos;
using Xunit;

namespace RiftReview.Core.Tests;

public class TimelineExtractorPhaseTests
{
    // Build a minimal timeline: a frame per minute 0..lastMinute. csAt(minute) = my cumulative CS.
    // events: (minute, type, killerId, victimId, assists) appended to that minute's frame.
    private static TimelineDto Tl(int lastMinute, Func<int, int> csAt,
        params (int min, string type, int? killer, int? victim, int[] assists)[] events)
    {
        var frames = new List<FrameDto>();
        for (int m = 0; m <= lastMinute; m++)
        {
            var pf = new Dictionary<string, ParticipantFrameDto>
            {
                ["3"] = new(3, 0, csAt(m), 0),   // "me" = pid 3, gold unused (series passed in)
                ["8"] = new(8, 0, 0, 0),
            };
            var evts = events.Where(e => e.min == m)
                .Select(e => new EventDto(e.type, m * 60000L, e.killer, e.victim,
                    AssistingParticipantIds: e.assists.Length > 0 ? e.assists.ToList() : null))
                .ToList();
            frames.Add(new FrameDto(m * 60000L, pf, evts));
        }
        return new TimelineDto(new TimelineMetadata("T", new()), new TimelineInfo(60000, frames));
    }

    private static List<ChartPoint> Series(params (double min, double val)[] pts) =>
        pts.Select(p => new ChartPoint(p.min, p.val)).ToList();

    [Fact]
    public void Emits_only_phases_the_game_reached()
    {
        var ps24 = TimelineExtractor.BuildPhaseBreakdown(
            Tl(24, m => 0), 3, 100, Series((0, 0), (24, 0)));
        Assert.Equal(new[] { "Early", "Mid", "Late" }, ps24.Select(p => p.Label));
        Assert.Equal(24, ps24.Single(p => p.Label == "Late").EndMinute);

        var ps14 = TimelineExtractor.BuildPhaseBreakdown(
            Tl(14, m => 0), 3, 100, Series((0, 0), (14, 0)));
        Assert.Equal(new[] { "Early", "Mid" }, ps14.Select(p => p.Label));
        Assert.Equal(14, ps14.Single(p => p.Label == "Mid").EndMinute); // partial Mid
    }

    [Fact]
    public void Gold_delta_is_team_series_endpoint_difference()
    {
        var series = Series((0, 0), (10, 500), (20, -300), (24, -800));
        var ps = TimelineExtractor.BuildPhaseBreakdown(Tl(24, m => 0), 3, 100, series);
        Assert.Equal(500, ps.Single(p => p.Label == "Early").GoldDiffDelta);
        Assert.Equal(-800, ps.Single(p => p.Label == "Mid").GoldDiffDelta);   // -300 - 500
        Assert.Equal(-500, ps.Single(p => p.Label == "Late").GoldDiffDelta);  // -800 - (-300)
    }

    [Fact]
    public void Cs_per_minute_is_cumulative_delta_over_duration()
    {
        int Cs(int m) => m <= 10 ? 8 * m : m <= 20 ? 80 + 7 * (m - 10) : 150 + 5 * (m - 20);
        var ps = TimelineExtractor.BuildPhaseBreakdown(
            Tl(24, Cs), 3, 100, Series((0, 0), (24, 0)));
        Assert.Equal(8.0, ps.Single(p => p.Label == "Early").CsPerMinute, 3);
        Assert.Equal(7.0, ps.Single(p => p.Label == "Mid").CsPerMinute, 3);
        Assert.Equal(5.0, ps.Single(p => p.Label == "Late").CsPerMinute, 3); // (170-150)/4
    }

    [Fact]
    public void Death_on_phase_boundary_counts_in_the_later_phase()
    {
        var ps = TimelineExtractor.BuildPhaseBreakdown(
            Tl(12, m => 0, (10, "CHAMPION_KILL", 8, 3, new int[0])),
            3, 100, Series((0, 0), (12, 0)));
        Assert.Equal(0, ps.Single(p => p.Label == "Early").Deaths);
        Assert.Equal(1, ps.Single(p => p.Label == "Mid").Deaths);
    }

    [Fact]
    public void Kill_participation_uses_enemy_deaths_as_denominator()
    {
        var ps = TimelineExtractor.BuildPhaseBreakdown(
            Tl(9, m => 0,
                (4, "CHAMPION_KILL", 3, 8, new int[0]),      // my kill (enemy 8 dies)
                (6, "CHAMPION_KILL", 1, 7, new[] { 3 }),     // my assist (enemy 7 dies)
                (8, "CHAMPION_KILL", 2, 9, new int[0]),      // teammate kill, not me
                (5, "CHAMPION_KILL", 8, 3, new int[0])),     // I die — not a team kill
            3, 100, Series((0, 0), (9, 0)));
        var early = ps.Single(p => p.Label == "Early");
        Assert.Equal(3, early.TeamKills);     // enemies 8,7,9 died
        Assert.Equal(1, early.Kills);
        Assert.Equal(1, early.Assists);
        Assert.Equal(2.0 / 3.0, early.KillParticipation!.Value, 3);
        Assert.Equal(1, early.Deaths);
    }

    [Fact]
    public void Kill_participation_is_null_when_team_scored_no_kills()
    {
        var ps = TimelineExtractor.BuildPhaseBreakdown(
            Tl(9, m => 0, (5, "CHAMPION_KILL", 8, 3, new int[0])), // only my death
            3, 100, Series((0, 0), (9, 0)));
        Assert.Null(ps.Single(p => p.Label == "Early").KillParticipation);
        Assert.Equal(0, ps.Single(p => p.Label == "Early").TeamKills);
    }

    [Fact]
    public void Deaths_while_behind_uses_team_gold_sign_at_death()
    {
        var ps = TimelineExtractor.BuildPhaseBreakdown(
            Tl(9, m => 0,
                (5, "CHAMPION_KILL", 8, 3, new int[0]),   // behind
                (8, "CHAMPION_KILL", 8, 3, new int[0])),  // ahead
            3, 100, Series((0, 0), (5, -200), (8, 300)));
        var early = ps.Single(p => p.Label == "Early");
        Assert.Equal(2, early.Deaths);
        Assert.Equal(1, early.DeathsWhileBehind);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test RiftReview.slnx --filter "FullyQualifiedName~TimelineExtractorPhaseTests"`
Expected: FAIL — `BuildPhaseBreakdown` does not exist (compile error).

- [ ] **Step 3: Implement `BuildPhaseBreakdown` + `ValueAtOrBefore`**

In `TimelineExtractor.cs`, add this public method (place it after `BuildCausality`) and the private helper (place it near the existing `NearestValue` helper):

```csharp
    // ---- M10: per-phase breakdown (item 20 "timeline mini-score") ----
    public static IReadOnlyList<PhaseStat> BuildPhaseBreakdown(
        TimelineDto tl, int myParticipantId, int myTeamId,
        IReadOnlyList<ChartPoint> teamGoldDiffSeries)
    {
        var frames = tl.Info.Frames;
        if (frames.Count == 0) return Array.Empty<PhaseStat>();
        double gameEnd = frames[^1].Timestamp / 60000.0;

        // Cumulative CS for my pid at each frame minute (string-keyed participant frames).
        var csCumulative = new List<ChartPoint>();
        foreach (var f in frames)
        {
            if (!f.ParticipantFrames.TryGetValue(myParticipantId.ToString(), out var mine)) continue;
            csCumulative.Add(new ChartPoint(f.Timestamp / 60000.0,
                mine.MinionsKilled + mine.JungleMinionsKilled));
        }

        var kills = frames.SelectMany(f => f.Events)
            .Where(e => e.Type == "CHAMPION_KILL")
            .Select(e => (Minute: e.Timestamp / 60000.0, e.KillerId, e.VictimId, e.AssistingParticipantIds))
            .ToList();

        var defs = new (string Label, double Start, double End)[]
        {
            ("Early", 0, 10),
            ("Mid", 10, 20),
            ("Late", 20, double.PositiveInfinity),
        };

        var result = new List<PhaseStat>();
        foreach (var (label, start, end) in defs)
        {
            if (gameEnd <= start) continue;                 // phase not reached
            double effEnd = Math.Min(end, gameEnd);
            double duration = effEnd - start;
            if (duration <= 0) continue;
            bool isLate = double.IsPositiveInfinity(end);

            double goldDelta = ValueAtOrBefore(teamGoldDiffSeries, effEnd)
                             - ValueAtOrBefore(teamGoldDiffSeries, start);
            double csPerMin = (ValueAtOrBefore(csCumulative, effEnd)
                             - ValueAtOrBefore(csCumulative, start)) / duration;

            bool InPhase(double m) => isLate ? (m >= start && m <= gameEnd) : (m >= start && m < end);

            int deaths = 0, deathsBehind = 0, myKills = 0, myAssists = 0, teamKills = 0;
            foreach (var k in kills)
            {
                if (!InPhase(k.Minute)) continue;
                if (k.VictimId == myParticipantId)
                {
                    deaths++;
                    if (ValueAtOrBefore(teamGoldDiffSeries, k.Minute) < 0) deathsBehind++;
                }
                if (TeamOfNullable(k.VictimId) is int vt && vt != myTeamId) teamKills++; // enemy died
                if (k.KillerId == myParticipantId) myKills++;
                if (k.AssistingParticipantIds is { } a && a.Contains(myParticipantId)) myAssists++;
            }

            double? kp = teamKills > 0 ? (myKills + myAssists) / (double)teamKills : null;
            result.Add(new PhaseStat(label, start, effEnd, goldDelta, csPerMin,
                deaths, deathsBehind, myKills, myAssists, teamKills, kp));
        }
        return result;
    }

    // Value of the series point with the largest Minute <= t (0 if the series is empty;
    // the first point's value if t precedes the series). Series is ascending by Minute.
    private static double ValueAtOrBefore(IReadOnlyList<ChartPoint> series, double t)
    {
        if (series.Count == 0) return 0;
        double val = series[0].Value;
        foreach (var p in series)
        {
            if (p.Minute <= t) val = p.Value;
            else break;
        }
        return val;
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test RiftReview.slnx --filter "FullyQualifiedName~TimelineExtractorPhaseTests"`
Expected: PASS (7 tests).

- [ ] **Step 5: Commit**

```bash
git add src/RiftReview.Core/Analysis/TimelineExtractor.cs tests/RiftReview.Core.Tests/TimelineExtractorPhaseTests.cs
git commit -m "feat(core): BuildPhaseBreakdown — per-phase decomposed metrics

Early/Mid/Late gold-diff delta, CS/min, deaths(+behind), kill participation.
KP null when team scored no kills; phases emitted only if reached.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 3: `PhaseBaselineCalculator` (pure, TDD)

**Files:**
- Create: `C:\Agent Projects\RiftReview\src\RiftReview.Core\Analysis\PhaseBaselineCalculator.cs`
- Test: `C:\Agent Projects\RiftReview\tests\RiftReview.Core.Tests\PhaseBaselineCalculatorTests.cs` (create)

- [ ] **Step 1: Write the failing tests**

Create `tests\RiftReview.Core.Tests\PhaseBaselineCalculatorTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using RiftReview.Core.Analysis;
using Xunit;

namespace RiftReview.Core.Tests;

public class PhaseBaselineCalculatorTests
{
    private static PhaseStat S(string label, double gold, double cs, int deaths, double? kp) =>
        new(label, 0, 10, gold, cs, deaths, 0, 0, 0, kp is null ? 0 : 1, kp);

    [Fact]
    public void Averages_each_metric_across_games_that_reached_the_phase()
    {
        var games = new List<IReadOnlyList<PhaseStat>>
        {
            new[] { S("Early", 100, 8, 1, 0.5) },
            new[] { S("Early", 200, 6, 3, 0.7) },
            new[] { S("Early", 300, 7, 2, 0.6) },
        };
        var b = PhaseBaselineCalculator.Average(games);
        var early = b.Single(x => x.Label == "Early");
        Assert.Equal(200, early.GoldDiffDelta);
        Assert.Equal(7, early.CsPerMinute);
        Assert.Equal(2, early.Deaths);
        Assert.Equal(0.6, early.KillParticipation!.Value, 3);
    }

    [Fact]
    public void Metric_with_too_few_samples_is_null_while_siblings_populate()
    {
        var games = new List<IReadOnlyList<PhaseStat>>
        {
            new[] { S("Early", 100, 8, 1, 0.5) },
            new[] { S("Early", 200, 6, 3, null) }, // KP missing
            new[] { S("Early", 300, 7, 2, null) }, // KP missing
        };
        var early = PhaseBaselineCalculator.Average(games).Single(x => x.Label == "Early");
        Assert.NotNull(early.GoldDiffDelta);                 // 3 samples
        Assert.Null(early.KillParticipation);                // only 1 sample (<3)
    }

    [Fact]
    public void Phase_reached_by_fewer_than_min_games_is_all_null()
    {
        var games = new List<IReadOnlyList<PhaseStat>>
        {
            new[] { S("Early", 100, 8, 1, 0.5), S("Late", 50, 5, 0, 0.4) },
            new[] { S("Early", 200, 6, 3, 0.7), S("Late", 60, 4, 1, 0.5) },
            new[] { S("Early", 300, 7, 2, 0.6) }, // no Late
        };
        var bl = PhaseBaselineCalculator.Average(games);
        Assert.NotNull(bl.Single(x => x.Label == "Early").GoldDiffDelta);   // 3
        var late = bl.Single(x => x.Label == "Late");                        // only 2
        Assert.Null(late.GoldDiffDelta);
        Assert.Null(late.CsPerMinute);
        Assert.Null(late.Deaths);
        Assert.Null(late.KillParticipation);
    }

    [Fact]
    public void Always_returns_the_three_phase_labels()
    {
        var b = PhaseBaselineCalculator.Average(new List<IReadOnlyList<PhaseStat>>());
        Assert.Equal(new[] { "Early", "Mid", "Late" }, b.Select(x => x.Label));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test RiftReview.slnx --filter "FullyQualifiedName~PhaseBaselineCalculatorTests"`
Expected: FAIL — `PhaseBaselineCalculator` does not exist.

- [ ] **Step 3: Implement `PhaseBaselineCalculator`**

Create `src\RiftReview.Core\Analysis\PhaseBaselineCalculator.cs`:

```csharp
namespace RiftReview.Core.Analysis;

/// Averages per-phase metrics across the player's prior same-role games, mirroring
/// BaselineCalculator. Each metric is null unless >= minGames games supplied a value for it.
public static class PhaseBaselineCalculator
{
    private static readonly string[] Labels = { "Early", "Mid", "Late" };

    public static IReadOnlyList<PhaseBaseline> Average(
        IEnumerable<IReadOnlyList<PhaseStat>> priorGames, int minGames = 3)
    {
        var games = priorGames.ToList();
        var result = new List<PhaseBaseline>();
        foreach (var label in Labels)
        {
            var stats = games
                .Select(g => g.FirstOrDefault(p => p.Label == label))
                .Where(p => p is not null)
                .Select(p => p!)
                .ToList();

            result.Add(new PhaseBaseline(
                label,
                Avg(stats.Select(s => (double?)s.GoldDiffDelta), minGames),
                Avg(stats.Select(s => (double?)s.CsPerMinute), minGames),
                Avg(stats.Select(s => (double?)s.Deaths), minGames),
                Avg(stats.Select(s => s.KillParticipation), minGames)));
        }
        return result;
    }

    private static double? Avg(IEnumerable<double?> values, int minGames)
    {
        var present = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        return present.Count < minGames ? null : present.Average();
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test RiftReview.slnx --filter "FullyQualifiedName~PhaseBaselineCalculatorTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/RiftReview.Core/Analysis/PhaseBaselineCalculator.cs tests/RiftReview.Core.Tests/PhaseBaselineCalculatorTests.cs
git commit -m "feat(core): PhaseBaselineCalculator — per-phase own same-role averages

Per-metric min-3 guard (null when too few samples); never fabricates.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 4: Wire the deep-dive view-model

**Files:**
- Modify: `C:\Agent Projects\RiftReview\src\RiftReview.App\ViewModels\DeepDiveViewModel.cs`

This piggybacks the existing 20-game baseline loop (zero extra timeline I/O) and adds the row VMs. **The CS-baseline output must stay byte-for-byte identical** — only an extra collection is added to the loop.

- [ ] **Step 1: Add the badge brushes + observable properties**

In the static frozen-brush block (next to `BaselineBrush` etc.), add:

```csharp
    private static readonly Brush GoodBadgeBrush    = Frozen(0x5F, 0xBF, 0x6F); // delta in your favour
    private static readonly Brush BadBadgeBrush      = Frozen(0xE0, 0x57, 0x4C); // delta against you
    private static readonly Brush NeutralBadgeBrush  = Frozen(0x8A, 0x8A, 0x8A); // no baseline yet
```

In the observable-property block (next to the M8 causality properties), add:

```csharp
    // By game phase (M10)
    [ObservableProperty] private bool _hasPhaseBreakdown;
    [ObservableProperty] private IReadOnlyList<PhaseRowVm> _phaseRows = Array.Empty<PhaseRowVm>();
```

- [ ] **Step 2: Replace `BuildBaseline` with a dual-output loop**

Replace the entire existing `BuildBaseline` method body with this `BuildBaselines` method (same loop, now also collecting phase stats):

```csharp
    private (IReadOnlyList<ChartPoint> Cs, IReadOnlyList<PhaseBaseline> Phases) BuildBaselines(
        MatchRow selected, string puuid)
    {
        var curves = new List<IReadOnlyList<ChartPoint>>();
        var phaseGames = new List<IReadOnlyList<PhaseStat>>();
        foreach (var m in _db.RecentMatches(rankedOnly: false, limit: 20)
                 .Where(m => m.MyTeamPosition == selected.MyTeamPosition && m.MatchId != selected.MatchId))
        {
            var mj = _db.GetMatchJson(m.MatchId);
            var tj = _db.GetTimelineJson(m.MatchId);
            if (mj is null || tj is null) continue;
            try
            {
                var mm = JsonSerializer.Deserialize<MatchDto>(mj, Json)!;
                var ms = MatchExtractor.Summarize(mm, puuid);
                var mtl = JsonSerializer.Deserialize<TimelineDto>(tj, Json)!;
                var mdd = TimelineExtractor.BuildDeepDive(mtl, ms.MyParticipantId, ms.OpponentParticipantId);
                curves.Add(mdd.CsPerMinute);
                phaseGames.Add(TimelineExtractor.BuildPhaseBreakdown(
                    mtl, ms.MyParticipantId, ms.MyTeamId, mdd.GoldDiffVsTeam));
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            { /* skip a match whose stored blobs can't be parsed/summarized */ }
        }
        return (BaselineCalculator.Average(curves), PhaseBaselineCalculator.Average(phaseGames));
    }
```

- [ ] **Step 3: Update `Load` to consume both and build rows**

In `Load`, find:

```csharp
            var csBaseline = BuildBaseline(selected, puuid);
            CsBaseline = csBaseline;
```

Replace with:

```csharp
            var (csBaseline, phaseBaseline) = BuildBaselines(selected, puuid);
            CsBaseline = csBaseline;

            var phaseStats = TimelineExtractor.BuildPhaseBreakdown(
                tl, summary.MyParticipantId, summary.MyTeamId, dd.GoldDiffVsTeam);
            PhaseRows = BuildPhaseRows(phaseStats, phaseBaseline);
            HasPhaseBreakdown = PhaseRows.Count > 0;
```

- [ ] **Step 4: Add the row-mapping helpers** (place near the bottom of the class, alongside `FormatGold`/`Clock`):

```csharp
    private static IReadOnlyList<PhaseRowVm> BuildPhaseRows(
        IReadOnlyList<PhaseStat> stats, IReadOnlyList<PhaseBaseline> baseline)
    {
        var rows = new List<PhaseRowVm>();
        foreach (var s in stats)
        {
            var b = baseline.FirstOrDefault(x => x.Label == s.Label);
            var (gTxt, gBrush) = Badge(s.GoldDiffDelta, b?.GoldDiffDelta, higherIsGood: true, SignedInt);
            var (cTxt, cBrush) = Badge(s.CsPerMinute, b?.CsPerMinute, higherIsGood: true, Signed1);
            var (dTxt, dBrush) = Badge(s.Deaths, b?.Deaths, higherIsGood: false, Signed1);

            string kp = s.KillParticipation is double k ? k.ToString("P0") : "—";
            string kpTxt = ""; Brush kpBrush = NeutralBadgeBrush;
            if (s.KillParticipation is double kk)
                (kpTxt, kpBrush) = Badge(kk * 100,
                    b?.KillParticipation is double bk ? bk * 100 : (double?)null,
                    higherIsGood: true, v => SignedInt(v) + " pts");

            string deaths = s.Deaths + (s.DeathsWhileBehind > 0 ? $" ({s.DeathsWhileBehind} behind)" : "");

            rows.Add(new PhaseRowVm(
                s.Label, RangeLabel(s.Label),
                SignedInt(s.GoldDiffDelta), gTxt, gBrush,
                s.CsPerMinute.ToString("0.0"), cTxt, cBrush,
                deaths, dTxt, dBrush,
                kp, kpTxt, kpBrush));
        }
        return rows;
    }

    private static string RangeLabel(string label) =>
        label switch { "Early" => "0–10", "Mid" => "10–20", _ => "20+" };

    private static (string text, Brush brush) Badge(
        double cur, double? baseline, bool higherIsGood, Func<double, string> fmt)
    {
        if (baseline is not double bv) return ("", NeutralBadgeBrush);
        double d = cur - bv;
        bool good = higherIsGood ? d >= 0 : d <= 0;
        return (fmt(d), good ? GoodBadgeBrush : BadBadgeBrush);
    }

    private static string SignedInt(double v) => (v >= 0 ? "+" : "") + Math.Round(v).ToString("0");
    private static string Signed1(double v) => (v >= 0 ? "+" : "") + v.ToString("0.0");
```

- [ ] **Step 5: Reset the new properties in `Clear`**

In the `Clear()` method, add:

```csharp
        HasPhaseBreakdown = false;
        PhaseRows = Array.Empty<PhaseRowVm>();
```

- [ ] **Step 6: Add the `PhaseRowVm` record**

Next to the other VM records at the end of the file (`ObjectiveRowVm`, `DeathContextVm`, `BackVm`), add:

```csharp
public sealed record PhaseRowVm(
    string Label, string Range,
    string GoldDelta,  string GoldBadge,   Brush GoldBadgeBrush,
    string CsPerMin,   string CsBadge,     Brush CsBadgeBrush,
    string Deaths,     string DeathsBadge, Brush DeathsBadgeBrush,
    string Kp,         string KpBadge,     Brush KpBadgeBrush);
```

(If `Brush` is unresolved in the record's file scope, add `using System.Windows.Media;` at the top — the class already uses it.)

- [ ] **Step 7: Build to verify it compiles**

Run: `dotnet build RiftReview.slnx -v minimal`
Expected: Build succeeded.

- [ ] **Step 8: Run the full suite (no regressions in existing App/Core tests)**

Run: `dotnet test RiftReview.slnx`
Expected: PASS, count = previous 155 + 11 new (Core) = ~166.

- [ ] **Step 9: Commit**

```bash
git add src/RiftReview.App/ViewModels/DeepDiveViewModel.cs
git commit -m "feat(app): deep-dive phase breakdown rows + own-baseline deltas

Piggybacks the existing 20-game same-role loop (zero extra timeline I/O);
CS-baseline output unchanged. Synchronous assignment like CsBaseline.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 5: Add the "By game phase" card to the deep-dive view

**Files:**
- Modify: `C:\Agent Projects\RiftReview\src\RiftReview.App\Views\DeepDiveView.xaml`

- [ ] **Step 1: Add a 6th row to the data Grid**

Find the `Grid.RowDefinitions` block (5 rows) and change it to 6:

```xml
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="*"/>
                <RowDefinition Height="*"/>
            </Grid.RowDefinitions>
```

- [ ] **Step 2: Shift the two chart cards down by one row**

On the "Gold diff chart" `Border`, change `Grid.Row="3"` to `Grid.Row="4"`.
On the "CS/min chart" `Border` (currently `Grid.Row="4"`), change it to `Grid.Row="5"`.

- [ ] **Step 3: Insert the new card at `Grid.Row="3"`** (between the Vision card at Row 2 and the gold chart now at Row 4):

```xml
            <!-- By game phase (M10) -->
            <Border Grid.Row="3"
                    Background="{StaticResource CardBgBrush}"
                    BorderBrush="{StaticResource HairlineBrush}"
                    BorderThickness="0,0,0,1"
                    Margin="0,0,0,2"
                    Padding="12,8"
                    Visibility="{Binding HasPhaseBreakdown, Converter={StaticResource BoolToVis}}">
                <StackPanel>
                    <Grid Margin="0,0,0,6">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*"/>
                            <ColumnDefinition Width="Auto"/>
                        </Grid.ColumnDefinitions>
                        <TextBlock Grid.Column="0" Text="By game phase"
                                   Foreground="{StaticResource AccentBrush}"
                                   FontSize="13" FontWeight="SemiBold"/>
                        <TextBlock Grid.Column="1" Text="vs your same-role average · raw where no baseline"
                                   Foreground="{StaticResource TextMutedBrush}" FontSize="10"
                                   VerticalAlignment="Center"/>
                    </Grid>

                    <Grid Margin="0,0,0,2">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="64"/>
                            <ColumnDefinition Width="*"/>
                            <ColumnDefinition Width="*"/>
                            <ColumnDefinition Width="*"/>
                            <ColumnDefinition Width="*"/>
                        </Grid.ColumnDefinitions>
                        <TextBlock Grid.Column="0" Text="Phase"    Foreground="{StaticResource TextMutedBrush}" FontSize="11"/>
                        <TextBlock Grid.Column="1" Text="Gold Δ"   Foreground="{StaticResource TextMutedBrush}" FontSize="11"/>
                        <TextBlock Grid.Column="2" Text="CS/min"   Foreground="{StaticResource TextMutedBrush}" FontSize="11"/>
                        <TextBlock Grid.Column="3" Text="Deaths"   Foreground="{StaticResource TextMutedBrush}" FontSize="11"/>
                        <TextBlock Grid.Column="4" Text="KP"       Foreground="{StaticResource TextMutedBrush}" FontSize="11"/>
                    </Grid>

                    <ItemsControl ItemsSource="{Binding PhaseRows}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Border BorderBrush="{StaticResource HairlineBrush}" BorderThickness="0,1,0,0" Padding="0,4">
                                    <Grid>
                                        <Grid.ColumnDefinitions>
                                            <ColumnDefinition Width="64"/>
                                            <ColumnDefinition Width="*"/>
                                            <ColumnDefinition Width="*"/>
                                            <ColumnDefinition Width="*"/>
                                            <ColumnDefinition Width="*"/>
                                        </Grid.ColumnDefinitions>
                                        <StackPanel Grid.Column="0">
                                            <TextBlock Text="{Binding Label}" Foreground="{StaticResource TextPrimaryBrush}" FontSize="12"/>
                                            <TextBlock Text="{Binding Range}" Foreground="{StaticResource TextMutedBrush}" FontSize="10"/>
                                        </StackPanel>
                                        <StackPanel Grid.Column="1">
                                            <TextBlock Text="{Binding GoldDelta}" Foreground="{StaticResource TextPrimaryBrush}" FontSize="14"/>
                                            <TextBlock Text="{Binding GoldBadge}" Foreground="{Binding GoldBadgeBrush}" FontSize="10"/>
                                        </StackPanel>
                                        <StackPanel Grid.Column="2">
                                            <TextBlock Text="{Binding CsPerMin}" Foreground="{StaticResource TextPrimaryBrush}" FontSize="14"/>
                                            <TextBlock Text="{Binding CsBadge}" Foreground="{Binding CsBadgeBrush}" FontSize="10"/>
                                        </StackPanel>
                                        <StackPanel Grid.Column="3">
                                            <TextBlock Text="{Binding Deaths}" Foreground="{StaticResource TextPrimaryBrush}" FontSize="14"/>
                                            <TextBlock Text="{Binding DeathsBadge}" Foreground="{Binding DeathsBadgeBrush}" FontSize="10"/>
                                        </StackPanel>
                                        <StackPanel Grid.Column="4">
                                            <TextBlock Text="{Binding Kp}" Foreground="{StaticResource TextPrimaryBrush}" FontSize="14"/>
                                            <TextBlock Text="{Binding KpBadge}" Foreground="{Binding KpBadgeBrush}" FontSize="10"/>
                                        </StackPanel>
                                    </Grid>
                                </Border>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>

                    <TextBlock Text="No flavor grades — every number is your raw value with its delta vs your own same-role average; blank where you have fewer than 3 prior games."
                               Foreground="{StaticResource TextMutedBrush}" FontSize="10"
                               TextWrapping="Wrap" Margin="0,6,0,0"/>
                </StackPanel>
            </Border>
```

- [ ] **Step 4: Build to verify the XAML compiles**

Run: `dotnet build RiftReview.slnx -v minimal`
Expected: Build succeeded (no XAML parse errors).

- [ ] **Step 5: Commit**

```bash
git add src/RiftReview.App/Views/DeepDiveView.xaml
git commit -m "feat(app): By game phase card in deep-dive (Layout A)

Phase rows x metric columns; raw value over signed delta-vs-your-average
badge; collapses when no phases. Inserted between Vision and gold chart.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 6: Seed player-credited team kills in the demo

Without this, KP renders "—" for every phase in `--seed-demo`.

**Files:**
- Modify: `C:\Agent Projects\RiftReview\src\RiftReview.App\Demo\DemoSeeder.cs`

- [ ] **Step 1: Add the `AddTeamKill` helper + a phase-spread of kills**

Inside `BuildGame`, after the existing death-injection loop (killer=8, victim=3) and before/after the `AddBack(...)` recall calls, add:

```csharp
        // M10: player-credited team kills (enemy victims 6–10, my killers 1–5) spread across
        // phases so kill-participation renders in --seed-demo. Vary by game index i so the
        // per-phase KP baseline is non-constant. Deaths (pid 3 as victim) are untouched.
        void AddTeamKill(int minute, int killerPid, int victimPid, int[] assists)
        {
            if (minute >= frames) return;
            frameList[minute].Events.Add(new EventDto(
                "CHAMPION_KILL", 60_000L * minute, killerPid, victimPid,
                AssistingParticipantIds: assists.Length > 0 ? assists.ToList() : null));
        }

        AddTeamKill(4,  3, 8, new int[0]);          // early: my kill
        AddTeamKill(7,  1, 6, new[] { 3 });         // early: my assist
        if (i % 2 == 0) AddTeamKill(9, 2, 7, new int[0]); // early: team kill (varies KP by game)
        AddTeamKill(12, 3, 9, new int[0]);          // mid: my kill
        AddTeamKill(15, 4, 10, new[] { 3, 5 });     // mid: my assist
        AddTeamKill(18, 5, 6, new int[0]);          // mid: team kill (no me)
        AddTeamKill(22, 3, 8, new[] { 2 });         // late: my kill
        AddTeamKill(26, 1, 7, new[] { 3 });         // late: my assist
        if (i % 2 == 1) AddTeamKill(30, 2, 9, new int[0]); // late: team kill (varies)
```

(If `System.Linq` is not already imported in `DemoSeeder.cs`, add `using System.Linq;` for `.ToList()`.)

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build RiftReview.slnx -v minimal`
Expected: Build succeeded.

- [ ] **Step 3: Run the full suite (demo changes must not break existing tests)**

Run: `dotnet test RiftReview.slnx`
Expected: PASS (same count as Task 4 step 8).

- [ ] **Step 4: Commit**

```bash
git add src/RiftReview.App/Demo/DemoSeeder.cs
git commit -m "test(demo): seed player-credited team kills so KP renders in --seed-demo

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 7: `.m10shots` screenshot harness + visual verification

**Files:**
- Create: `C:\Agent Projects\RiftReview\.m10shots\run_capture.ps1` (copy of the M8 review-page harness)

- [ ] **Step 1: Clone the M8 review-page capture script**

Copy `C:\Agent Projects\RiftReview\.m8shots\run_capture.ps1` to
`C:\Agent Projects\RiftReview\.m10shots\run_capture.ps1`. Keep its mechanics **exactly** (they are
the documented harness): launch the Debug exe with `--seed-demo --page review`; set
`HKCU:\Software\Microsoft\Avalon.Graphics\DisableHWAcceleration = 1` (restore the prior value
afterward); reach the deep-dive via UIAutomation `SelectionItemPattern.Select()` on the **first**
match list item; capture with `C:\Agent Projects\RiftReview\.m2shots\Capturer\out\Capturer.exe`
(PrintWindow `PW_RENDERFULLCONTENT`). Change only the **output PNG path** to
`C:\Agent Projects\RiftReview\.m10shots\phase.png`. If the M8 script has a "tall" variant
(`run_capture_tall.ps1`), copy that too — the new card sits high (above the charts) so the default
capture should frame it, but keep the tall fallback available.

- [ ] **Step 2: Ensure PNGs are gitignored**

Confirm `.gitignore` contains a pattern covering `.m?shots/*.png` (it does, from prior milestones).
If `.m10shots` needs an explicit entry, add `.m10shots/*.png`.

- [ ] **Step 3: Build Debug and run the capture**

```bash
dotnet build RiftReview.slnx -c Debug -v minimal
powershell -ExecutionPolicy Bypass -File "C:\Agent Projects\RiftReview\.m10shots\run_capture.ps1"
```
Expected: `C:\Agent Projects\RiftReview\.m10shots\phase.png` is written.

- [ ] **Step 4: Verify the screenshot via a Sonnet subagent (text verdict only)**

Dispatch a subagent to Read `C:\Agent Projects\RiftReview\.m10shots\phase.png` and confirm against
acceptance criteria: a "By game phase" card is visible between the Vision card and the gold chart;
three phase rows (Early/Mid/Late) each show four numeric columns (Gold Δ, CS/min, Deaths, KP); KP
shows a percentage (not "—") for at least the early/mid rows; delta badges appear under values in
green/red; no letter grades or flavor words anywhere. The subagent returns PASS/FAIL + observations +
the absolute path it viewed. **Do not load the PNG into the controller context.**

- [ ] **Step 5: Commit the harness script (not the PNG)**

```bash
git add .m10shots/run_capture.ps1 .gitignore
git commit -m "test(shots): .m10shots harness for the By game phase card

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 8: Final verification + ROADMAP update

**Files:**
- Modify: `C:\Agent Projects\RiftReview\ROADMAP.md`

- [ ] **Step 1: Run the entire suite once more**

Run: `dotnet test RiftReview.slnx`
Expected: PASS, ~166 tests (Core 142, App 24). Record the exact number.

- [ ] **Step 2: Add the M10 row to the milestone table**

Append to the `## Milestones` table:

```markdown
| 10 | By game phase (timeline mini-score) | ✅ Merged | `docs/superpowers/plans/2026-06-20-riftreview-m10.md` | #_ | Deep-dive "By game phase" card (item 20): Early/Mid/Late × {gold-diff Δ, CS/min, deaths(+behind), kill participation}, each a raw number + signed delta vs own same-role average; never-fabricate (phase not reached → absent; <3 prior games → no badge); KP = new CHAMPION_KILL extraction. Pure BuildPhaseBreakdown + PhaseBaselineCalculator piggyback the existing 20-game loop (zero extra I/O); NO schema change. Suite ___. |
```

- [ ] **Step 3: Add a decision-log entry** under `## Decision log & gotchas` summarizing: item 20 shipped as the "By game phase" card; phase boundaries Early[0,10)/Mid[10,20)/Late[20,end] with Late inclusive of the final minute; gold-Δ reuses `dd.GoldDiffVsTeam` (chart-aligned, no drift); KP denominator = enemy deaths (robust to killerId=0); baseline piggybacks `BuildBaselines` so CS-baseline output is unchanged; demo seeder gained player-credited team kills (KP would render "—" otherwise); never-fabricate Late-baseline case is unit-tested, not forced in the demo.

- [ ] **Step 4: Commit**

```bash
git add ROADMAP.md
git commit -m "docs(roadmap): M10 By game phase shipped

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Self-review (author checklist — completed)

- **Spec coverage:** intervals (Task 2 defs), own-baseline + never-fabricate (Tasks 3–4), 4 metrics incl. KP new extraction (Task 2), Layout A (Task 5), zero-I/O piggyback (Task 4), demo seed (Task 6), screenshot gate (Task 7), no schema change (no DB task). All covered.
- **Placeholders:** none — every code step shows complete code; PR # and final suite count are runtime values filled at merge.
- **Type consistency:** `PhaseStat`/`PhaseBaseline`/`PhaseRowVm` fields match across Tasks 1→3→4→5; `BuildPhaseBreakdown(tl,myPid,myTeamId,series)` signature identical in Task 2 test, Task 4 caller, and Task 4 loop; `BuildBaselines` returns the tuple consumed in `Load`.
- **Risk guard:** Task 4 explicitly preserves the CS-baseline output; if it would change, STOP and report.
```

