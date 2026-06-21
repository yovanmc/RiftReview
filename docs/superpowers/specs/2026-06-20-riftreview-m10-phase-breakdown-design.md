# RiftReview M10 — "By game phase" (item 20, timeline mini-score) — Design

> Status: approved 2026-06-20. Scopes the deferred research-dossier **item 20** ("per-game
> timeline mini-score → label intervals with actionable **deltas**, never op.gg-style flavor
> keywords"). Source of truth for the M10 implementation plan.

## Goal

Add a compact **"By game phase"** card to the per-match deep-dive that decomposes the game into
three fixed phases and, for each phase, shows four individually-numbered metrics with a signed
**delta vs the player's own same-role average**. It is the data-honest answer to op.gg's OP-Score
keywords: every cell is a number, nothing is rolled into a composite, and absent baselines are left
blank rather than fabricated.

## Non-goals (inherited, load-bearing)

- **No composite score.** No single "phase grade", no letter, no flavor word ("Strong laning").
  Every verdict is the number itself. (Enshrined in `## Non-goals` of ROADMAP.md and
  `docs/superpowers/specs/2026-06-20-riftreview-verdict-audit.md`.)
- **Never fabricate.** A phase the game didn't reach is not shown. A per-phase baseline with fewer
  than 3 contributing prior games shows the raw value with **no delta badge** — never a made-up
  default.
- **No new data sources, no schema migration.** Computed on demand from the stored
  `timeline_json` blob, exactly like M7/M8/M9. No Riot calls, no Data Dragon, no rank baseline
  (the M6 seed table is far too sparse to slice per-phase — own-baseline only).

## Decisions (from brainstorming)

| Axis | Decision |
|------|----------|
| Intervals | **Phase-based**: Early `[0,10)`, Mid `[10,20)`, Late `[20, gameEnd]` minutes. |
| Reference frame | **Vs the player's own same-role trailing average**, never-fabricate fallback to raw. |
| Metrics (v1) | **All four**: gold-diff Δ, CS/min, deaths (+ behind context), kill participation. |
| KP | The one **new extraction** — from `CHAMPION_KILL` events; `teamKills == 0` → `null` (shown "—"). |
| Layout | **Option A** — phase rows × metric columns; raw number leads, delta badge underneath. |
| Run mode | Autonomous (plan + build this session). |

## Data model (new records in `RiftReview.Core/Analysis/AnalysisModels.cs`)

```csharp
/// One game phase for one match. All metrics are exact (no baseline applied here).
/// KillParticipation is null when the player's team scored no kills in the phase (never fabricate).
public sealed record PhaseStat(
    string Label,            // "Early" | "Mid" | "Late"
    double StartMinute,      // 0, 10, 20
    double EndMinute,        // capped at game end (partial phases allowed)
    double GoldDiffDelta,    // team gold-diff change across the phase (+ = my team gained)
    double CsPerMinute,      // CS gained in the phase / phase duration (minutes)
    int Deaths,              // my deaths in the phase
    int DeathsWhileBehind,   // subset of Deaths where team gold-diff < 0 at the death
    int Kills,               // my kills in the phase (KP numerator part)
    int Assists,             // my assists in the phase (KP numerator part)
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

`MatchSummary` is **unchanged** — it already exposes `MyParticipantId`, `MyTeamId`,
`OpponentParticipantId`.

## Computation (pure, in `TimelineExtractor` — Core, fully unit-tested)

New method:

```csharp
public static IReadOnlyList<PhaseStat> BuildPhaseBreakdown(
    TimelineDto tl, int myParticipantId, int myTeamId,
    IReadOnlyList<ChartPoint> teamGoldDiffSeries)
```

Reuses the existing private `TeamOf`/`TeamOfNullable` helpers and the string-keyed
`ParticipantFrames` idiom. `teamGoldDiffSeries` is passed in (the caller already computed
`dd.GoldDiffVsTeam`) so the gold-Δ aligns **exactly** with the displayed chart — the same
anti-drift discipline M8 used for `TeamGoldDiffSeries`.

Rules per phase (only emitted if `gameEndMinute > phaseStart`; `gameEndMinute` = last frame's
`Timestamp/60000`):

- **Boundaries.** `effEnd = min(phaseEnd, gameEndMinute)`; phase duration `= effEnd - phaseStart`
  (always > 0 for a reached phase). Membership of an event/death at minute `m`:
  `phaseStart ≤ m < phaseEnd` for Early/Mid; `phaseStart ≤ m ≤ gameEnd` for Late (the final minute
  is inclusive only in Late).
- **GoldDiffDelta** `= valueAtOrBefore(teamGoldDiffSeries, effEnd) − valueAtOrBefore(series, phaseStart)`.
  `valueAtOrBefore` returns the value of the point with the largest `Minute ≤ t` (0 if none).
- **CsPerMinute** `= (cs(effEnd) − cs(phaseStart)) / duration`, where `cs(t)` is cumulative CS
  (`MinionsKilled + JungleMinionsKilled` for my pid) at the frame with the largest minute `≤ t`.
- **Deaths** `=` count of `CHAMPION_KILL` where `VictimId == myParticipantId` in the phase window.
  **DeathsWhileBehind** `=` subset where `valueAtOrBefore(teamGoldDiffSeries, deathMinute) < 0`.
- **Kill participation.** In the phase window over `CHAMPION_KILL` events:
  - `TeamKills` = count where `TeamOfNullable(VictimId)` is the **enemy** team (i.e. `!= myTeamId`,
    non-null) — every enemy death is a kill for my team.
  - `Kills` = count where `KillerId == myParticipantId`.
  - `Assists` = count where `AssistingParticipantIds` contains `myParticipantId`.
  - `KillParticipation = TeamKills > 0 ? (Kills + Assists) / (double)TeamKills : null`.
  (Numerator ≤ denominator by construction: the player is at most one of killer/assist per team kill.)

New pure aggregator `RiftReview.Core/Analysis/PhaseBaselineCalculator.cs`, mirroring
`BaselineCalculator`:

```csharp
public static IReadOnlyList<PhaseBaseline> Average(
    IEnumerable<IReadOnlyList<PhaseStat>> priorGames, int minGames = 3)
```

For each label (Early/Mid/Late), gather the contributing prior games' values **per metric** (a
game contributes only if it reached that phase; KP contributes only when non-null). Each metric is
averaged independently and is `null` unless it has `≥ minGames` samples. Always returns the three
labels (so the App can zip current ↔ baseline by label).

## App integration (`DeepDiveViewModel`, synchronous — no threading trap)

`Load` is fully synchronous; the new data is assigned exactly like `CsBaseline`/`CsSeries`. Two
changes:

1. **Piggyback the existing 20-game loop (zero extra I/O).** `BuildBaseline` already loads the last
   20 same-role games' timelines (`_db.RecentMatches(rankedOnly:false, limit:20)` filtered to
   `selected.MyTeamPosition`), deserializes each, calls `BuildDeepDive`, and collects `CsPerMinute`.
   Refactor it into one method that, in the **same** loop, also collects
   `BuildPhaseBreakdown(mtl, ms.MyParticipantId, ms.MyTeamId, mdd.GoldDiffVsTeam)` per prior game,
   then returns both `BaselineCalculator.Average(csCurves)` (unchanged) and
   `PhaseBaselineCalculator.Average(phaseStatsPerGame)`. **The CS-baseline output must be byte-for-byte
   identical** to today (it feeds the exact-value-tested CS chart) — only an additional collection is
   added.
2. **Current-match phase stats + row VMs.** After `dd` is built, call `BuildPhaseBreakdown(tl,
   summary.MyParticipantId, summary.MyTeamId, dd.GoldDiffVsTeam)`, zip with the phase baseline by
   label, and project to display rows. Expose `bool HasPhaseBreakdown` (true iff ≥1 phase) +
   `IReadOnlyList<PhaseRowVm> PhaseRows`.

New App-layer VM:

```csharp
public sealed record PhaseRowVm(
    string Label, string Range,                       // "Early", "0–10"
    string GoldDelta,  string? GoldDeltaVsAvg, bool GoldGood,   // null badge => no baseline
    string CsPerMin,   string? CsVsAvg,        bool CsGood,
    string Deaths,     string? DeathsVsAvg,    bool DeathsGood, // "good" = fewer than avg
    string Kp,         string? KpVsAvg,        bool KpGood);    // Kp = "—" when KillParticipation null
```

Delta sign→colour mapping: gold-Δ and KP higher = good (success colour); CS higher = good; deaths
higher = bad (danger colour). A `null *VsAvg` renders no badge (never-fabricate). Deltas are
rounded for display (gold integer, CS 1 dp, deaths 1 dp, KP whole-percent points).

## UI (`DeepDiveView.xaml`) — Layout A

A new card inserted **between** the Vision card (`Grid.Row="2"`) and the Gold-diff chart card
(currently `Grid.Row="3"`). Add a 6th `RowDefinition`; the gold-diff and CS charts shift to rows
**4** and **5**. The new card is `Grid.Row="3"`, `Height="Auto"`.

- Matches existing card chrome: `Border` with `CardBgBrush` / `HairlineBrush` /
  `BorderThickness="0,0,0,1"` / `Margin="0,0,0,2"` / `Padding="12,8"`.
- Section header `TextBlock` "By game phase": `AccentBrush`, `FontSize=13`, `FontWeight=SemiBold`.
- A right-aligned caption: "vs your same-role average · raw where no baseline".
- Body = a header row + an `ItemsControl` of `PhaseRows` using a 5-column `Grid` `DataTemplate`
  (Phase | Gold Δ | CS/min | Deaths | KP). Each metric cell stacks the raw value
  (`TextPrimaryBrush`, 14–15px) over a small delta badge (success/danger foreground), the badge
  collapsed when its `*VsAvg` binding is null (use the `BoolToVis` converter against a derived
  `Has*` flag, or a `DataTrigger` on null — match the file's existing idiom; it has **no**
  NullToCollapsed converter).
- Whole card visibility bound to `HasPhaseBreakdown` via `BoolToVis` (same idiom as the Swing card).
- No new chart, no new colour resources beyond the existing brushes + the standard success/danger
  semantic brushes already used elsewhere.

## Demo seeder (`RiftReview.App/Demo/DemoSeeder.cs`) — required or KP renders "—"

The demo currently emits `CHAMPION_KILL` only as **killer=8, victim=3** (the player only ever dies),
so `TeamKills == 0` and KP would be `null` for every phase. Add player-credited team kills:

- A helper `AddTeamKill(int minute, int killerPid, int victimPid, int[] assists)` appending
  `EventDto("CHAMPION_KILL", 60000L*minute, killerPid, victimPid, AssistingParticipantIds: assists)`
  with `victimPid` on the enemy team (6–10) and `killerPid` on my team (1–5).
- Per demo game, distribute ~8–12 team kills across the three phases (e.g. minutes 4/7/9, 12/15/18,
  22/26/30), crediting pid 3 as killer or assist on ~60% of them, varied slightly by game index `i`
  so the baseline column is non-constant. Deaths (pid 3 as victim) are untouched.
- Existing gold/CS frame seeding already populates gold-Δ and CS/min per phase; demo games are
  25–35 min so all three phases render. The never-fabricate Late-baseline case is **not** forced in
  the demo (all games reach Late) — it is covered by unit tests instead.

## Testing

- `tests/RiftReview.Core.Tests/` (xUnit, `snake_case` methods, inline `TimelineDto` builders keyed by
  string pid, as in `TimelineExtractorCausalityTests`):
  - **`BuildPhaseBreakdown`**: phase bucketing at exact boundaries (event at 10.0 → Mid); a partial
    Late phase (game ends 24 min → Late duration 4); gold-Δ equals the team-series endpoint diff;
    CS/min equals cumulative-CS delta ÷ duration; deaths + DeathsWhileBehind counts; KP numerator/
    denominator; **KP null when no enemy deaths in a phase**; a phase the game never reached is absent.
  - **`PhaseBaselineCalculator`**: averages per metric; a metric with <3 samples → null while a
    sibling metric with ≥3 stays populated; a phase reached by <3 games → all-null for that phase;
    KP samples skip null contributors.
- App tests remain at 24 (the VM mapping is exercised via the screenshot gate; add a VM unit test
  only if the harness supports async-free construction — current Core coverage is the gate).
- Target suite ≈ 155 → ~165–170.

## Screenshot gate (`.m10shots/`)

Clone `.m9shots` but target the **review page** (deep-dive is embedded in `ReviewView`): launch the
Debug exe with `--seed-demo --page review`, set `DisableHWAcceleration=1` (restore after), select the
first match via UIAutomation `SelectionItemPattern.Select()`, capture with
`.m2shots/Capturer/out/Capturer.exe` (PrintWindow PW_RENDERFULLCONTENT). The new card sits above the
charts (high in the deep-dive) so the default capture should frame it; use the **tall** variant if it
falls below the fold. A Sonnet subagent views the PNG and returns a text verdict (PASS/FAIL +
observations + paths) — PNGs gitignored (`.m?shots/*.png`), scripts committed.

## Risks / guard-rails for the implementer

- **Do not alter `BuildDeepDive`** (exact-value tested) or change the CS-baseline output of the
  refactored loop — only add the phase collection alongside it. If the refactor would change the CS
  baseline, STOP and report.
- **Float boundaries.** Frames are 1/min with integer-minute timestamps, so boundaries land on exact
  frames; still use `valueAtOrBefore` (not exact-minute lookup) so a missing frame degrades
  gracefully.
- **KP denominator is enemy deaths, not killer-side counting** — avoids miscrediting tower/execute
  kills (killerId may be 0). Counting enemy deaths is the robust denominator.
- Honor the M9 lesson conceptually: any property the view binds must raise INPC. Here `Load` is
  synchronous so a plain `[ObservableProperty]` assignment is sufficient; the screenshot gate is the
  final proof the card actually renders.
```

