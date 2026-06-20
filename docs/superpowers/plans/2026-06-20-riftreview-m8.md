# RiftReview M8 — Timeline causality (implementation plan)

> **Written for Sonnet execution.** Code blocks are complete and verified against the current
> source (read 2026-06-20). For edits to *existing* App files (ViewModel/View/control/seeder),
> READ the current file first and apply the change in the file's own style/namespaces.
> **If something doesn't match what's described here, STOP and report rather than guess.**

## Goal

Move the deep-dive from *descriptive* ("here's your gold graph") to *causal* ("where/why the game
turned"). Three research-dossier items (13–15), all timeline-only, **no external data**:

- **Item 13 — "Where the game turned":** the single largest swing in the **team** gold-diff.
- **Item 14 — Game-state-at-death:** team gold-diff at each of my deaths (diagnostic, not just descriptive).
- **Item 15 — Back-timing / item-spike lag:** recalls inferred from `ITEM_PURCHASED` clusters + the
  lag between the turning-point swing and the preceding recall.

Everything is computed **on demand** from the stored `timeline_json` blob (like M7's Vision &
objectives) — **no DB schema migration**. The `EventDto` gains 2 nullable fields (additive, like M7).

## Locked design decisions (data-honesty guardrails)

- **Swing = team gold-diff**, not lane. "Where the game turned" is a macro/game-state concept; the
  lane (1v1) diff is the micro view. Positive team diff = my team ahead (confirmed sign convention).
- **Swing window = 3 frames (~3 min)**, rolling; tie-break earliest. A single-minute delta is noisy;
  3 minutes captures a teamfight→objective cascade. Configurable via a private const.
- **No decisive swing** (flat game, or <2 frames, or |Δ| below epsilon) → `Swing == null` → UI shows
  "No decisive swing this game." Never fabricate an inflection that isn't there.
- **Back-timing uses NO item metadata.** We do **not** pull Data Dragon / item names / gold values
  (this honors the M6 decision to drop external build data to protect the local-only identity).
  A "back" = a cluster of my `ITEM_PURCHASED` events at nearly the same timestamp (you buy instantly
  on a base visit). We report each back's **minute** and **item count** only — recall *cadence*,
  which is the honest, coaching-useful signal. No "item spike = legendary completion" (that needs
  external item tiers).
- **Turning-point lag** = minutes from the swing's start back to the most recent recall at-or-before
  it ("your power swing began N min after your last shopping trip"). `null` if no swing or no
  preceding back.
- **Surgical:** `BuildDeepDive` is heavily tested (asserts exact gold values) — **do not modify it.**
  Add a new `BuildCausality` and a new private team-series helper used only by it. The helper uses the
  identical formula/skip-rule as `BuildDeepDive`, so the swing marker aligns with the displayed chart.

---

## Task 1 — Core DTO + analysis models

### 1a. Extend `EventDto` (additive, backward-compatible)

File: `src/RiftReview.Core/Riot/Dtos/TimelineDtos.cs`. Append two nullable params to the **end** of
the `EventDto` record (so all existing positional/named constructions keep compiling, and JSON
deserialization is by name anyway):

```csharp
public sealed record EventDto(
    string Type, long Timestamp, int? KillerId, int? VictimId,
    int? CreatorId = null,
    string? WardType = null,
    int? KillerTeamId = null,
    string? MonsterType = null,
    string? MonsterSubType = null,
    string? BuildingType = null,
    string? TowerType = null,
    string? LaneType = null,
    int? TeamId = null,
    List<int>? AssistingParticipantIds = null,
    int? ParticipantId = null,    // ITEM_PURCHASED / ITEM_SOLD use participantId (NOT killerId)
    int? ItemId = null);          // ITEM_PURCHASED itemId (not rendered; reserved for future)
```

Riot match-v5 `ITEM_PURCHASED` shape: `{ "type":"ITEM_PURCHASED", "timestamp":…, "participantId":…, "itemId":… }`.
JSON deserialization is case-insensitive (the test/loader already sets `PropertyNameCaseInsensitive = true`),
so `participantId`/`itemId` map automatically. Existing stored blobs simply have these as null.

### 1b. Add causality result models

File: `src/RiftReview.Core/Analysis/AnalysisModels.cs`. Append:

```csharp
/// One inflection window on the TEAM gold-diff curve. Favorable = my team's lead grew.
public sealed record SwingPoint(
    double StartMinute, double EndMinute,
    double StartGold, double EndGold,
    double Delta, bool Favorable);

/// Team gold-diff (signed; + = my team ahead) at the minute I died.
public sealed record DeathContext(double Minute, double GoldDiff);

/// A recall, inferred from a cluster of my ITEM_PURCHASED events. ItemCount = items bought that trip.
public sealed record BackEvent(double Minute, int ItemCount);

public sealed record CausalityResult(
    SwingPoint? Swing,                       // null = no decisive swing
    IReadOnlyList<DeathContext> Deaths,
    IReadOnlyList<BackEvent> Backs,
    double? TurningPointLagMinutes);         // min from preceding back to swing start; null if N/A
```

**Verify 1:** `dotnet build RiftReview.slnx -v minimal` succeeds (no other code references these yet).

---

## Task 2 — Core algorithm: `BuildCausality`

File: `src/RiftReview.Core/Analysis/TimelineExtractor.cs`. Add the following **inside** the
`TimelineExtractor` class (place after `BuildVisionObjectives`). **Do not touch `BuildDeepDive`.**

```csharp
    // ---- M8: timeline causality (swing / death-context / back-timing) ----
    private const int  SwingWindowFrames = 3;     // ~3 min (frames are 1-min); rolling window width
    private const double SwingEpsilonGold = 1.0;  // |Δ| below this => no decisive swing
    private const long BackClusterGapMs = 10_000; // purchases > 10s apart => a separate recall

    public static CausalityResult BuildCausality(TimelineDto tl, int myParticipantId)
    {
        var team = TeamGoldDiffSeries(tl, myParticipantId);   // signed: + = my team ahead

        // Item 13: largest-magnitude rolling-window swing on the team gold-diff curve.
        SwingPoint? swing = null;
        if (team.Count >= 2)
        {
            int w = Math.Min(SwingWindowFrames, team.Count - 1);
            double bestAbs = -1;
            for (int i = 0; i + w < team.Count; i++)
            {
                double delta = team[i + w].Value - team[i].Value;
                if (Math.Abs(delta) > bestAbs)
                {
                    bestAbs = Math.Abs(delta);
                    swing = new SwingPoint(
                        team[i].Minute, team[i + w].Minute,
                        team[i].Value, team[i + w].Value,
                        delta, delta > 0);
                }
            }
            if (swing is not null && Math.Abs(swing.Delta) < SwingEpsilonGold) swing = null;
        }

        // Item 14: team gold-diff at each of my deaths.
        var deaths = tl.Info.Frames
            .SelectMany(f => f.Events)
            .Where(e => e.Type == "CHAMPION_KILL" && e.VictimId == myParticipantId)
            .Select(e => e.Timestamp / 60000.0)
            .OrderBy(m => m)
            .Select(m => new DeathContext(Math.Round(m, 2), NearestValue(team, m)))
            .ToList();

        // Item 15: recalls = clusters of my ITEM_PURCHASED events.
        var myPurchases = tl.Info.Frames
            .SelectMany(f => f.Events)
            .Where(e => e.Type == "ITEM_PURCHASED" && e.ParticipantId == myParticipantId)
            .OrderBy(e => e.Timestamp)
            .ToList();

        var backs = new List<BackEvent>();
        long clusterStart = 0, prev = 0;
        int count = 0;
        void Flush() { if (count > 0) backs.Add(new BackEvent(Math.Round(clusterStart / 60000.0, 2), count)); }
        foreach (var e in myPurchases)
        {
            if (count == 0 || e.Timestamp - prev > BackClusterGapMs) { Flush(); clusterStart = e.Timestamp; count = 0; }
            count++;
            prev = e.Timestamp;
        }
        Flush();

        // Turning-point lag: from the most recent recall at-or-before the swing start.
        double? lag = null;
        if (swing is not null && backs.Count > 0)
        {
            var preceding = backs.Where(b => b.Minute <= swing.StartMinute)
                                 .OrderByDescending(b => b.Minute)
                                 .FirstOrDefault();
            if (preceding is not null) lag = Math.Round(swing.StartMinute - preceding.Minute, 2);
        }

        return new CausalityResult(swing, deaths, backs, lag);
    }

    // Team gold-diff curve (mirrors BuildDeepDive's team logic + its skip-rule, so the swing
    // aligns with the displayed chart). Kept separate so the tested BuildDeepDive stays untouched.
    private static List<ChartPoint> TeamGoldDiffSeries(TimelineDto tl, int myParticipantId)
    {
        var line = new List<ChartPoint>();
        int myTeam = TeamOf(myParticipantId);
        foreach (var f in tl.Info.Frames)
        {
            if (!f.ParticipantFrames.ContainsKey(myParticipantId.ToString())) continue; // same guard as BuildDeepDive
            long myTeamGold = 0, enemyTeamGold = 0;
            foreach (var pf in f.ParticipantFrames.Values)
            {
                if (TeamOf(pf.ParticipantId) == myTeam) myTeamGold += pf.TotalGold;
                else enemyTeamGold += pf.TotalGold;
            }
            line.Add(new ChartPoint(f.Timestamp / 60000.0, myTeamGold - enemyTeamGold));
        }
        return line;
    }

    private static double NearestValue(List<ChartPoint> series, double minute)
    {
        if (series.Count == 0) return 0;
        var best = series[0];
        double bestDist = Math.Abs(best.Minute - minute);
        foreach (var p in series)
        {
            double d = Math.Abs(p.Minute - minute);
            if (d < bestDist) { bestDist = d; best = p; }
        }
        return best.Value;
    }
```

**Verify 2:** `dotnet build RiftReview.slnx -v minimal` succeeds.

---

## Task 3 — Core tests

### 3a. Add fixture `tests/RiftReview.Core.Tests/Fixtures/causality_timeline.json`

First READ an existing fixture (e.g. `Fixtures/sample_timeline.json`) to confirm the exact JSON
casing/shape the loader expects, then create this file. It models me = participant **1** (team 100),
enemy = participant **6** (team 200), so team gold-diff = `p1.totalGold − p6.totalGold`. Designed so:
the largest 3-min swing is minutes **5→8** (diff 500→2500, **Δ +2000, favorable**); deaths at min **2**
(diff 200) and min **7** (diff 1700); recalls at min **1** (×3), min **4** (×2), min **8** (×1);
turning-point lag = `5 − 4 = 1`.

```json
{
  "metadata": { "matchId": "TEST_M8", "participants": ["p1","p2","p3","p4","p5","p6","p7","p8","p9","p10"] },
  "info": {
    "frameInterval": 60000,
    "frames": [
      { "timestamp": 0,      "participantFrames": { "1": {"participantId":1,"totalGold":500, "minionsKilled":0, "jungleMinionsKilled":0}, "6": {"participantId":6,"totalGold":500, "minionsKilled":0, "jungleMinionsKilled":0} }, "events": [] },
      { "timestamp": 60000,  "participantFrames": { "1": {"participantId":1,"totalGold":800, "minionsKilled":8, "jungleMinionsKilled":0}, "6": {"participantId":6,"totalGold":700, "minionsKilled":7, "jungleMinionsKilled":0} }, "events": [
          {"type":"ITEM_PURCHASED","timestamp":60000,"participantId":1,"itemId":1001},
          {"type":"ITEM_PURCHASED","timestamp":61000,"participantId":1,"itemId":1004},
          {"type":"ITEM_PURCHASED","timestamp":62000,"participantId":1,"itemId":2003} ] },
      { "timestamp": 120000, "participantFrames": { "1": {"participantId":1,"totalGold":1100,"minionsKilled":16,"jungleMinionsKilled":0}, "6": {"participantId":6,"totalGold":900, "minionsKilled":14,"jungleMinionsKilled":0} }, "events": [
          {"type":"CHAMPION_KILL","timestamp":120000,"killerId":6,"victimId":1} ] },
      { "timestamp": 180000, "participantFrames": { "1": {"participantId":1,"totalGold":1400,"minionsKilled":24,"jungleMinionsKilled":0}, "6": {"participantId":6,"totalGold":1100,"minionsKilled":21,"jungleMinionsKilled":0} }, "events": [] },
      { "timestamp": 240000, "participantFrames": { "1": {"participantId":1,"totalGold":1700,"minionsKilled":32,"jungleMinionsKilled":0}, "6": {"participantId":6,"totalGold":1300,"minionsKilled":28,"jungleMinionsKilled":0} }, "events": [
          {"type":"ITEM_PURCHASED","timestamp":240000,"participantId":1,"itemId":3020},
          {"type":"ITEM_PURCHASED","timestamp":241000,"participantId":1,"itemId":1026} ] },
      { "timestamp": 300000, "participantFrames": { "1": {"participantId":1,"totalGold":2000,"minionsKilled":40,"jungleMinionsKilled":0}, "6": {"participantId":6,"totalGold":1500,"minionsKilled":35,"jungleMinionsKilled":0} }, "events": [] },
      { "timestamp": 360000, "participantFrames": { "1": {"participantId":1,"totalGold":2500,"minionsKilled":46,"jungleMinionsKilled":0}, "6": {"participantId":6,"totalGold":1500,"minionsKilled":40,"jungleMinionsKilled":0} }, "events": [] },
      { "timestamp": 420000, "participantFrames": { "1": {"participantId":1,"totalGold":3200,"minionsKilled":52,"jungleMinionsKilled":0}, "6": {"participantId":6,"totalGold":1500,"minionsKilled":44,"jungleMinionsKilled":0} }, "events": [
          {"type":"CHAMPION_KILL","timestamp":420000,"killerId":6,"victimId":1} ] },
      { "timestamp": 480000, "participantFrames": { "1": {"participantId":1,"totalGold":4000,"minionsKilled":58,"jungleMinionsKilled":0}, "6": {"participantId":6,"totalGold":1500,"minionsKilled":48,"jungleMinionsKilled":0} }, "events": [
          {"type":"ITEM_PURCHASED","timestamp":480000,"participantId":1,"itemId":3089} ] },
      { "timestamp": 540000, "participantFrames": { "1": {"participantId":1,"totalGold":4200,"minionsKilled":64,"jungleMinionsKilled":0}, "6": {"participantId":6,"totalGold":1700,"minionsKilled":52,"jungleMinionsKilled":0} }, "events": [] },
      { "timestamp": 600000, "participantFrames": { "1": {"participantId":1,"totalGold":4400,"minionsKilled":70,"jungleMinionsKilled":0}, "6": {"participantId":6,"totalGold":1900,"minionsKilled":56,"jungleMinionsKilled":0} }, "events": [] }
    ]
  }
}
```

Team diffs by minute: 0,100,200,300,400,500,1000,1700,2500,2500,2500. Rolling 3-frame deltas: the
max is `[min5..min8] = 2500−500 = +2000`. (Sanity: `[6→9]=1500`, `[3→6]=700` — all smaller.)

### 3b. Add `tests/RiftReview.Core.Tests/TimelineExtractorCausalityTests.cs`

READ `TimelineExtractorTests.cs` first to match the fixture-loader idiom (`FixtureLoader.Read(...)`,
`JsonSerializerOptions { PropertyNameCaseInsensitive = true }`) and assertion style. Then:

```csharp
using System.Text.Json;
using RiftReview.Core.Analysis;
using RiftReview.Core.Riot.Dtos;
using Xunit;

namespace RiftReview.Core.Tests;

public class TimelineExtractorCausalityTests
{
    // Mirror the loader idiom used by TimelineExtractorTests (adjust if that file differs).
    private static TimelineDto Tl() => JsonSerializer.Deserialize<TimelineDto>(
        FixtureLoader.Read("causality_timeline.json"),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    private static readonly JsonSerializerOptions Ci = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public void Swing_finds_largest_3min_window_and_sign()
    {
        var c = TimelineExtractor.BuildCausality(Tl(), myParticipantId: 1);
        Assert.NotNull(c.Swing);
        Assert.Equal(5, c.Swing!.StartMinute);
        Assert.Equal(8, c.Swing.EndMinute);
        Assert.Equal(500,  c.Swing.StartGold);
        Assert.Equal(2500, c.Swing.EndGold);
        Assert.Equal(2000, c.Swing.Delta);
        Assert.True(c.Swing.Favorable);
    }

    [Fact]
    public void Death_context_reports_team_gold_diff_at_each_death()
    {
        var c = TimelineExtractor.BuildCausality(Tl(), myParticipantId: 1);
        Assert.Equal(2, c.Deaths.Count);
        Assert.Equal(2, c.Deaths[0].Minute);
        Assert.Equal(200, c.Deaths[0].GoldDiff);
        Assert.Equal(7, c.Deaths[1].Minute);
        Assert.Equal(1700, c.Deaths[1].GoldDiff);
    }

    [Fact]
    public void Backs_cluster_purchases_by_gap()
    {
        var c = TimelineExtractor.BuildCausality(Tl(), myParticipantId: 1);
        Assert.Equal(3, c.Backs.Count);
        Assert.Equal((1.0, 3), (c.Backs[0].Minute, c.Backs[0].ItemCount));
        Assert.Equal((4.0, 2), (c.Backs[1].Minute, c.Backs[1].ItemCount));
        Assert.Equal((8.0, 1), (c.Backs[2].Minute, c.Backs[2].ItemCount));
    }

    [Fact]
    public void Turning_point_lag_is_from_preceding_back()
    {
        var c = TimelineExtractor.BuildCausality(Tl(), myParticipantId: 1);
        Assert.Equal(1.0, c.TurningPointLagMinutes); // swing start 5 − back at 4
    }

    [Fact]
    public void Flat_game_has_no_decisive_swing()
    {
        var tl = Build(new[] { (0, 1000, 1000), (1, 1000, 1000), (2, 1000, 1000), (3, 1000, 1000) });
        var c = TimelineExtractor.BuildCausality(tl, myParticipantId: 1);
        Assert.Null(c.Swing);
    }

    [Fact]
    public void Single_frame_has_no_swing()
    {
        var tl = Build(new[] { (0, 1000, 800) });
        var c = TimelineExtractor.BuildCausality(tl, myParticipantId: 1);
        Assert.Null(c.Swing);
    }

    [Fact]
    public void No_purchases_yields_no_backs_and_null_lag()
    {
        var tl = Build(new[] { (0, 500, 500), (1, 900, 600), (2, 1400, 700), (3, 2000, 700) });
        var c = TimelineExtractor.BuildCausality(tl, myParticipantId: 1);
        Assert.Empty(c.Backs);
        Assert.Null(c.TurningPointLagMinutes);
    }

    // Build a minimal timeline: each tuple = (minute, myGold p1/team100, enemyGold p6/team200).
    private static TimelineDto Build((int min, int mine, int enemy)[] pts)
    {
        var frames = pts.Select(p => new FrameDto(
            p.min * 60000L,
            new Dictionary<string, ParticipantFrameDto>
            {
                ["1"] = new(1, p.mine, 0, 0),
                ["6"] = new(6, p.enemy, 0, 0),
            },
            new List<EventDto>())).ToList();
        return new TimelineDto(new TimelineMetadata("T", new()), new TimelineInfo(60000, frames));
    }
}
```

**Verify 3:** `dotnet test RiftReview.slnx -v minimal` — all green; Core gains 7 tests
(102 → 109). If `FixtureLoader` has a different name/signature in `TimelineExtractorTests.cs`,
match it exactly (STOP and report if no fixture loader exists).

---

## Task 4 — Demo seeder: emit recalls so back-timing renders

File: `src/RiftReview.App/Demo/DemoSeeder.cs`. The seeder's `BuildGame(...)` currently emits
`CHAMPION_KILL` events but **no `ITEM_PURCHASED`**, so back-timing would render empty in the demo.

READ `BuildGame` first to find: (a) the player's participantId variable, (b) how frames/events are
constructed and where the per-minute `Events` lists live, (c) game length. Then add a small set of
synthetic recalls for the player at a few base-trip timestamps, each a cluster of 1–3 purchases a
second apart, e.g. backs near minutes 2 (×1), 8 (×3), 14 (×2), 20 (×2). Concretely, for the player's
participantId `pid`, append events like:

```csharp
// inside BuildGame, after death events are added — add synthetic recalls (back-timing demo data).
void AddBack(List<EventDto> evs, int minute, int items)
{
    long t0 = minute * 60000L;
    for (int k = 0; k < items; k++)
        evs.Add(new EventDto("ITEM_PURCHASED", t0 + k * 1000L, null, null, ParticipantId: pid, ItemId: 1001 + k));
}
```

Add the recalls to the frame `Events` list(s) matching those minutes (the extractor flattens all
frames' events, so exact frame placement only needs to be plausible — put each back's events in the
frame whose minute matches). Pick back minutes that fall **within the seeded game length** and at
least one **before** a clear gold swing so the turning-point-lag line renders. Keep it light — this
is demo dressing, not a simulation.

**Verify 4:** `dotnet build RiftReview.slnx -v minimal` succeeds. (Behavioral check happens in the
screenshot step.)

---

## Task 5 — ViewModel wiring + formatters

File: `src/RiftReview.App/ViewModels/DeepDiveViewModel.cs`. Follow the **M7 Vision & objectives**
pattern exactly (READ how `Vision`/`Objectives` are declared and populated in `Load`). Add:

### 5a. Display VM records (App-side; near the existing `ObjectiveRowVm`)

```csharp
public sealed record DeathContextVm(string Minute, string Gold, bool Ahead);
public sealed record BackVm(string Text);
```

### 5b. Observable properties (with the other `[ObservableProperty]` fields)

```csharp
[ObservableProperty] private bool _hasCausality;
[ObservableProperty] private bool _hasSwing;
[ObservableProperty] private string _swingText = "";
[ObservableProperty] private bool _swingFavorable;
[ObservableProperty] private IReadOnlyList<DeathContextVm> _deathContexts = Array.Empty<DeathContextVm>();
[ObservableProperty] private string _backSummary = "";
[ObservableProperty] private IReadOnlyList<BackVm> _backs = Array.Empty<BackVm>();
[ObservableProperty] private bool _hasLag;
[ObservableProperty] private string _turningPointLag = "";
```

### 5c. Populate in `Load` (right after the `BuildVisionObjectives` block)

```csharp
var causality = TimelineExtractor.BuildCausality(tl, summary.MyParticipantId);

if (causality.Swing is { } sw)
{
    HasSwing = true;
    SwingFavorable = sw.Favorable;
    SwingText = $"{FormatGold(sw.Delta)} {(sw.Favorable ? "in your favor" : "against you")} · "
              + $"{Clock(sw.StartMinute)} → {Clock(sw.EndMinute)}";
}
else { HasSwing = false; SwingText = "No decisive swing this game."; }

DeathContexts = causality.Deaths
    .Select(d => new DeathContextVm(Clock(d.Minute), FormatGold(d.GoldDiff), d.GoldDiff >= 0))
    .ToList();

Backs = causality.Backs
    .Select(b => new BackVm($"{Clock(b.Minute)}{(b.ItemCount > 1 ? $" ×{b.ItemCount}" : "")}"))
    .ToList();
BackSummary = causality.Backs.Count switch { 0 => "", 1 => "1 back", var n => $"{n} backs" };

HasLag = causality.TurningPointLagMinutes is not null;
TurningPointLag = causality.TurningPointLagMinutes is double lag
    ? $"Power swing began {Clock(lag)} after a back"
    : "";

HasCausality = HasSwing || DeathContexts.Count > 0 || Backs.Count > 0;
```

### 5d. Formatters (private static helpers in the same class)

```csharp
// 14.0 -> "14:00", 14.5 -> "14:30"
private static string Clock(double minute)
{
    int total = (int)Math.Round(minute * 60);
    return $"{total / 60}:{total % 60:00}";
}

// +1850 -> "+1,850g", -1200 -> "−1,200g" (U+2212 minus), 0 -> "0g"
private static string FormatGold(double g)
{
    string sign = g > 0 ? "+" : g < 0 ? "−" : "";
    return $"{sign}{Math.Abs(g):#,0}g";
}
```

**Verify 5:** `dotnet build RiftReview.slnx -v minimal` succeeds.

### 5e. (Optional, low-cost) App formatter tests

If `RiftReview.App.Tests` already tests pure helpers, expose `Clock`/`FormatGold` as `internal static`
(+ `[assembly: InternalsVisibleTo("RiftReview.App.Tests")]` if not already present) and add ~3 asserts
(`Clock(14.5)=="14:30"`, `FormatGold(1850)=="+1,850g"`, `FormatGold(-1200)=="−1,200g"`). If the
App.Tests project has no precedent for reaching into the VM, **skip this** rather than add plumbing —
the Core tests are the gate.

---

## Task 6 — View: "Swing & causality" card

File: `src/RiftReview.App/Views/DeepDiveView.xaml`. READ the file and the **M7 Vision & objectives**
`Border` card first (copy its brushes, corner radius, padding, margins, header `TextBlock` style, and
the converter it uses for show/hide). Insert a new card **directly under the header, above the Vision
card**, so it's the first thing captured by PrintWindow (M7 lesson: top-of-deep-dive content captures
without scrolling). Reflow the `Grid` rows accordingly (new row for this card; shift Vision/gold/cs
down by one). Bind to the Task 5 properties. Use whatever `BoolToVisibility` converter the project
already has (the Vision card's empty-states establish the pattern); if none exists, add the standard
one. Structure (adapt brush/style resource keys to the real ones in the file):

```xml
<Border Style="{StaticResource DeepDiveCardStyle}"
        Visibility="{Binding HasCausality, Converter={StaticResource BoolToVisibility}}">
  <StackPanel>
    <TextBlock Text="Swing &amp; causality" Style="{StaticResource DeepDiveSectionHeader}"/>

    <!-- Item 13: where the game turned -->
    <StackPanel Orientation="Horizontal" Margin="0,4,0,0">
      <TextBlock Text="Where the game turned: " Opacity="0.7"/>
      <TextBlock Text="{Binding SwingText}"/>
    </StackPanel>

    <!-- Item 14: deaths in context -->
    <ItemsControl ItemsSource="{Binding DeathContexts}" Margin="0,6,0,0">
      <ItemsControl.ItemTemplate>
        <DataTemplate>
          <StackPanel Orientation="Horizontal" Margin="0,1,0,1">
            <TextBlock Text="Died " Opacity="0.7"/>
            <TextBlock Text="{Binding Minute}"/>
            <TextBlock Text=" — "/>
            <TextBlock Text="{Binding Gold}"/>
          </StackPanel>
        </DataTemplate>
      </ItemsControl.ItemTemplate>
    </ItemsControl>

    <!-- Item 15: back-timing -->
    <StackPanel Orientation="Horizontal" Margin="0,6,0,0">
      <TextBlock Text="Backs: " Opacity="0.7"/>
      <TextBlock Text="{Binding BackSummary}"/>
    </StackPanel>
    <ItemsControl ItemsSource="{Binding Backs}">
      <ItemsControl.ItemsPanel>
        <ItemsPanelTemplate><WrapPanel/></ItemsPanelTemplate>
      </ItemsControl.ItemsPanel>
      <ItemsControl.ItemTemplate>
        <DataTemplate><TextBlock Text="{Binding Text}" Margin="0,0,10,0" Opacity="0.85"/></DataTemplate>
      </ItemsControl.ItemTemplate>
    </ItemsControl>
    <TextBlock Text="{Binding TurningPointLag}" Margin="0,4,0,0" Opacity="0.7"
               Visibility="{Binding HasLag, Converter={StaticResource BoolToVisibility}}"/>
  </StackPanel>
</Border>
```

Optionally color the death-gold / swing text by ahead/behind (bind `Ahead`/`SwingFavorable` through a
bool→brush converter to the Hextech Gold `#C8AA6E` vs a muted red) **only if** the project already has
such a converter; otherwise leave default foreground (don't add styling plumbing for M8).

**Verify 6:** `dotnet build RiftReview.slnx -v minimal` succeeds.

---

## Task 7 — (Secondary, risk-gated) swing band on the gold chart

Item 13's wording is "marker on the gold-diff chart." The Task 6 card already satisfies the
acceptance criteria; this task adds a visual band on the chart as a bonus.

READ `src/RiftReview.App/Controls/LineChart.cs` (the hand-rolled `OnRender` control with `Series` and
`DeathMarkers`). If its X→pixel mapping is clear (it must be, since `DeathMarkers` already maps minutes
to X), add two nullable dependency properties `SwingStartMinute`/`SwingEndMinute` (double?) and, when
both are set, draw a translucent vertical band (e.g. `#22C8AA6E`) between their X positions **behind**
the series. Bind them from the gold `LineChart` in `DeepDiveView.xaml` to new VM properties
(`SwingStartMinute`/`SwingEndMinute`, populated in Task 5c from `sw.StartMinute`/`sw.EndMinute`).

**If the control's render/scale math is not obviously safe to extend, STOP and report** — do not risk
the existing chart rendering. The text card is the primary deliverable; the band is optional.

**Verify 7:** `dotnet build RiftReview.slnx -v minimal` succeeds; existing App tests still green.

---

## Final verification (controller-driven, after all tasks)

1. `dotnet test RiftReview.slnx -v minimal` — **all green.** Expect Core 102 → ~109 (+7 causality),
   App 24 unchanged (or +~3 if Task 5e was done). Report the exact counts.
2. **Screenshot gate.** Clone the `.m7shots` harness to `.m8shots` (copy `run_capture.ps1`, keep the
   DisableHWAcceleration set/restore, the `.m2shots/Capturer/out/Capturer.exe` PrintWindow capturer,
   and the UIAutomation `SelectionItemPattern.Select()` on the first ReviewView match). Build the
   Debug exe, launch `--seed-demo --page review`, select the first match to open the deep-dive, capture
   `.m8shots/deepdive.png`. Then dispatch a **Sonnet subagent** to Read the PNG and return a TEXT
   verdict against these acceptance criteria (never load the PNG into the controller):
   - A "Swing & causality" card is visible near the top of the deep-dive.
   - "Where the game turned" shows a signed gold value + a minute range (or "No decisive swing").
   - Deaths-in-context rows show minute + signed gold.
   - Back-timing shows a back count + a list of recall minutes (some with ×N).
   - Nothing is empty/overlapping/clipped; theme (black-glass + Hextech Gold) intact.
3. `.m?shots/*.png` is already gitignored; commit the `.m8shots` **scripts** only.

## Done = 
All tasks merged via PR (foreground `gh pr checks --watch` if CI exists; this repo has **no CI**, so
the gate is local `dotnet test` + the screenshot subagent verdict). `ROADMAP.md` M8 row flipped to
✅ Merged with PR # and a one-line summary; decision-log entry added (swing definition, back-timing =
no-external-data cadence, EventDto +2 fields, no migration).
