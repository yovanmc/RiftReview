# RiftReview M5 — Climb View — Design Spec

**Date:** 2026-06-19
**Milestone:** M5 of the 5-milestone roadmap (M5 = "rank/LP view"). **Final roadmap milestone.**
**Status:** design locked via owner brainstorming.

---

## 1. Goal

A **Climb** page: the owner's current ranked standing, his recent win/loss momentum (rich today), and an honest LP-history ledger that **accrues going forward** as he keeps syncing.

## 2. The data reality (drives the design)

Riot's API exposes **only current** rank/LP (LEAGUE-V4); match data (MATCH-V5) has **no LP field**. So true per-game LP for *past* games is unrecoverable. What we have:
- `lp_snapshots(taken_utc, queue_type, tier, division, league_points, wins, losses)` — one row **per sync**, currently sparse (clustered on backfill days). `GetLpSnapshots()` returns oldest→newest.
- `matches` — 631 games with win/loss, `GameStartUtc` (unix seconds), `queue_id` (420 solo, 440 flex).

**Owner-locked choice:** "Climb view + start real LP history" — current rank + win/loss streaks (rich now) + **net-LP segments between real snapshots** + the per-sync attribution that makes per-game LP history accrue from now on. (NOT the "estimated curve" option; NOT "aggressive snapshotting".)

## 3. Scope guard

- **Read-only.** NO schema change (stays **v3**), NO migration, NO Riot calls, **NO `SyncService` change.** The per-sync LP snapshot already exists; M5's `ClimbCalculator.Segments` IS the attribution wiring (it pairs each snapshot-delta with the ranked games in its time window). As the owner syncs more often over real time, windows shrink toward per-game — automatically, no further code.
- Pure calculators in `RiftReview.Core.Analysis`; a transient `ClimbViewModel` + `ClimbView`; mirrors the existing patterns.

## 4. Core concepts

### 4.1 `RankLadder` (shared helper — extract from `TrendsViewModel.Cap()`)

Converts `(tier, division, lp)` → an absolute "ladder points" int for diffing across divisions/tiers, and formats a display string. Replaces the ad-hoc `Cap()` in `TrendsViewModel` (which M5 updates to call `RankLadder.Format`, DRY).

- Tiers (low→high): IRON, BRONZE, SILVER, GOLD, PLATINUM, EMERALD, DIAMOND — each 4 divisions IV(0),III(1),II(2),I(3). Apex: MASTER/GRANDMASTER/CHALLENGER (no divisions, one continuous LP pool above Diamond I).
- `ToPoints(tier, division, lp)`:
  - non-apex: `tierIndex*400 + divisionOffset*100 + lp` (Iron IV 0 = 0; Diamond I 100 = 6*400+3*100+100 = 2800).
  - apex: `7*400 + lp` (= 2800 + lp; Master 0 LP = 2800, continuous with GM/Chall).
  - Must be **monotonic** across every boundary.
- `Format(tier, division, lp)`: non-apex `"Gold II · 47 LP"`; apex `"Master 312 LP"` (no division). `Cap()` title-cases the all-caps tier.

### 4.2 `ClimbModels`

```csharp
public sealed record StreakSummary(
    int CurrentStreak,                 // signed: +3 = 3 wins, -2 = 2 losses, 0 = none
    int LongestWinStreak,
    int LongestLossStreak,
    IReadOnlyList<bool> RecentForm);   // last N ranked games, oldest->newest (true = win)

public sealed record LpSegment(
    string QueueType,
    long FromUtc, long ToUtc,
    int FromPoints, int ToPoints, int NetLp,   // ToPoints - FromPoints, can be +/-
    int GamesInWindow,
    string FromLabel, string ToLabel);         // rank strings via RankLadder.Format
```

### 4.3 `ClimbCalculator` (pure static)

- `StreakSummary Streaks(IReadOnlyList<MatchRow> rankedMatches, int recentFormCount = 10)`:
  - Order by `GameStartUtc` ascending. Computed over **all ranked** (420+440) combined = "ranked momentum".
  - `CurrentStreak` = signed trailing run; `LongestWinStreak`/`LongestLossStreak` = max runs; `RecentForm` = last `recentFormCount` wins (oldest→newest).
  - Empty input → `(0, 0, 0, [])`.
- `IReadOnlyList<LpSegment> Segments(IReadOnlyList<LpSnapshot> snapshots, IReadOnlyList<MatchRow> rankedMatches, string queueType)`:
  - Filter snapshots to `queueType`, order by `TakenUtc`. For each consecutive `(prev, cur)`: `NetLp = ToPoints(cur) - ToPoints(prev)`; `GamesInWindow` = count of matches whose `queue_id` maps to `queueType` and `prev.TakenUtc < GameStartUtc <= cur.TakenUtc`; build labels via `RankLadder.Format`.
  - Returns segments **newest-first** for display. < 2 snapshots → empty.
  - Queue map: `RANKED_SOLO_5x5`↔420, `RANKED_FLEX_SR`↔440.

## 5. ViewModel (`ClimbViewModel`, transient)

```csharp
public sealed partial class ClimbViewModel : ObservableObject
{
    public ClimbViewModel(RiftReviewDb db, DataDragonClient ddragon);  // ddragon for parity/EnsureLoaded; not strictly needed

    // Current standing per queue (latest snapshot):
    [ObservableProperty] private StandingViewModel? _solo;
    [ObservableProperty] private StandingViewModel? _flex;
    [ObservableProperty] private bool _hasAnyStanding;

    // Ranked momentum:
    [ObservableProperty] private string _streakText;       // "On a 3-win streak" / "2-loss skid" / "No ranked games yet"
    [ObservableProperty] private bool _streakIsPositive;
    [ObservableProperty] private string _longestStreaksText;// "Best: 6W · Worst: 4L"
    public ObservableCollection<FormPip> _recentForm;       // last N pips

    // LP history (Solo primary; Flex optional section):
    public ObservableCollection<LpSegmentViewModel> SoloSegments { get; }
    [ObservableProperty] private bool _hasSoloSegments;
    [ObservableProperty] private string _lpHistoryNote;     // honesty note about sparse/forward-fill

    [ObservableProperty] private bool _isEmpty;             // no snapshots AND no ranked games

    public async Task InitializeAsync();  // EnsureLoadedAsync (best-effort) then Load()
    public void Load();
}
```

- `StandingViewModel`: `QueueLabel` ("Ranked Solo/Duo" / "Flex"), `RankText` (`RankLadder.Format`), `SeasonRecord` ("142W / 118L"), `WinRateText` ("55%"), built from a `LpSnapshot`.
- `FormPip`: `{ bool Win }` for the recent-form strip.
- `LpSegmentViewModel`: `WhenRange` ("Jun 12 → Jun 18"), `GamesLabel` ("8 games"), `NetLpText` ("+47 LP" / "−23 LP"), `IsGain` (NetLp ≥ 0), `RankRange` ("Gold IV → Gold II").
- `Load()`: `var snaps = _db.GetLpSnapshots(); var ranked = _db.AllMatches(rankedOnly:true);` → build standings (latest snapshot per queue), streaks, Solo segments. Set `LpHistoryNote` = "LP history fills in as you sync — Riot doesn't expose past per-game LP." Set `IsEmpty` when no snapshots and no ranked games.

## 6. View (`ClimbView.xaml`)

`PanelBgBrush` root (header Auto + body scroll). Top→bottom:
- Gold "Climb" title.
- **Standing cards row:** a horizontal pair of `CardBgBrush` tiles (Solo, Flex). Each: queue label (muted), big `RankText` (`AccentBrush` gold, FontSize ~20), `SeasonRecord` + `WinRateText` (muted). Collapse a card when its standing is null (DataTrigger). If `HasAnyStanding` is false, show a muted "No ranked standing yet — sync while ranked."
- **Ranked momentum tile:** `StreakText` big (colored green if `StreakIsPositive`, red otherwise, via DataTrigger), `LongestStreaksText` muted, and a **recent-form pip strip**: an `ItemsControl` of `RecentForm` rendering small rounded rectangles colored `WinBrush`/`LossBrush` (via the `BoolToBrushConverter`/`WinResultBrush`).
- **LP history section:** muted "LP HISTORY (RANKED SOLO)" header, the `LpHistoryNote` (muted, italic, small), then:
  - an `ItemsControl` of `SoloSegments` (newest first): each row `WhenRange` + `RankRange` (muted) on the left, `NetLpText` on the right colored by `IsGain` (green/red). Hairline separators.
  - if `HasSoloSegments` is false: a muted "Only one snapshot so far — your next sync starts the ledger."
- **Empty state** (`IsEmpty`): "Sync while ranked to start tracking your climb."

(Optional, only if it renders cleanly with ≥2 points: a small `Sparkline` of Solo absolute-points over snapshots above the segment list, reusing the existing control. Skip if sparse-looks-bad — the segment ledger is the primary artifact.)

## 7. Navigation + DI

- Nav item in `AppShell.xaml` after "Session Health": `Content="Climb" TargetPageType="{x:Type views:ClimbView}"`, symbol `ui:SymbolIcon Symbol="..."` — try `ChartMultiple24` / `ArrowTrendingLines24` / `Trophy24`; fall back to a known-good if absent.
- `--page climb` case in `AppShell.xaml.cs`.
- DI: `s.AddTransient<ViewModels.ClimbViewModel>();` + `s.AddTransient<ClimbView>();` (both transient — no shared state needed).

## 8. Demo seeder

The demo currently has no `lp_snapshots` (confirm). Add a handful spanning the demo's ranked games so the Climb page renders: ~3–4 Solo snapshots over time showing a climb (e.g. Gold IV 30 → Gold III 60 → Gold II 12 → Gold I 75) plus 1–2 Flex snapshots, with `taken_utc` values interleaved among the demo `game_start_utc`s so `GamesInWindow` is non-zero per segment. Use `db.InsertLpSnapshot(new LpSnapshot(takenUtc, "RANKED_SOLO_5x5", "GOLD", "IV", 30, wins, losses))` etc.

## 9. Acceptance criteria

- `dotnet build` clean (0 warnings); `dotnet test` all green incl. new `RankLadder` + `ClimbCalculator` + `ClimbViewModel` tests.
- No schema change / migration / Riot calls / SyncService change.
- Climb page shows current Solo + Flex standing (or a clear no-data state); ranked-momentum streaks + recent-form strip from the full ranked history; an LP-segment ledger (net LP per snapshot window, colored, with games-in-window) + the honesty note.
- `RankLadder.ToPoints` is monotonic across all tier/division boundaries; `Format` matches the existing "Gold II · 47 LP" idiom and handles apex.
- `TrendsViewModel` LP headline now uses `RankLadder.Format` (no behavior change, DRY).
- Demo renders standings + a multi-segment LP ledger; screenshot-verified (real + demo).
- No secrets; `appsettings.json` placeholders only.

## 10. Hand-back (owner runs locally)

- LP segments are coarse today (snapshots cluster on backfill days) — they sharpen as you sync more often going forward. Confirm the first real post-M5 sync adds a snapshot and a new segment appears.
- Your past per-game LP is genuinely unrecoverable (Riot limitation) — confirm the honesty note reads right.
- Apex-tier ladder math (Master+) is modeled as a continuous pool above Diamond I; verify if/when you climb there.
