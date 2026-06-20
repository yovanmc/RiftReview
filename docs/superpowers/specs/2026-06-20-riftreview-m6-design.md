# RiftReview M6 — "You vs Your Rank" on the Graphs — Design Spec

**Date:** 2026-06-20
**Milestone:** M6 (first post-v1 expansion milestone). Implements research-derived items 1–9.
**Status:** design locked (owner decisions captured below).

---

## 1. Goal

Render a **rank-relative baseline** alongside the player's own performance on the existing
charts and Trends sparklines — "you vs your current rank" — so a metric reads as *"6.8 CS@10,
+0.8 vs Gold"* instead of a bare number. Keep the existing **own-trailing** baseline as a
toggle. Be ruthlessly honest where rank data doesn't exist.

## 2. Owner decisions (locked)

- **Rank baseline data source → HYBRID.** A patch-tagged, source-labeled, **owner-editable**
  per-rank benchmark table is the *primary* overlay; a toggle compares against the player's own
  trailing average (the rigorous, zero-external-data path that already exists for CS-pace).
- **Win-rate anchor correction.** The lolalytics "51.77%, not 50%" finding is a *champion*
  baseline, not a player one. A player's own win rate trends to ~50% by matchmaking design, so
  **player WR keeps a 50% anchor.** The measured-baseline idea is reserved for champion/matchup
  win-rates (future M-work), NOT applied to the player's climb WR here.
- **Sparse, never-fabricate table.** The seed ships only values that are defensible
  (CS@10 / CS-per-min by role×tier). Any absent (metric, role, tier) cell → the UI shows the
  **own-trailing baseline only** and a quiet "no rank baseline" state. We never render a made-up
  per-rank number.

## 3. The data reality (drives the design)

- Riot's API exposes the player's **current** tier/division (latest `LpSnapshot`, queue
  `RANKED_SOLO_5x5`) but **no per-rank stat distributions**. The research pass found no clean
  public source either. So rank baselines come from an **embedded seed table**, clearly labeled
  *approximate · source · patch*, editable without a rebuild (JSON resource).
- The existing per-game baseline (`DeepDiveViewModel.BuildBaseline` →
  `BaselineCalculator.Average`) is **already** the own-trailing average of the player's prior
  same-role/same-champion games. M6 reuses it as the "Own" comparison mode; it adds "Rank" mode.
- `MatchRow` carries the comparable scalars: `CsAt10` (int?), `GoldDiffAt15` (int?, a
  *differential* → rank baseline is ~even/0, so we do NOT push a rank line on it), `Kills/Deaths/
  Assists`, `KillParticipation` (double?), `DamageShare` (double?), `DeathsPre15` (int?).

## 4. Scope

- **In:** items 1–9 — the rank-baseline table + provider (Core, pure, tested); a rank-baseline
  overlay on the deep-dive **CS-pace** LineChart; a baseline series added to the **Sparkline**
  control; per-metric **delta-vs-baseline** badges on Trends; a **Rank ⇄ Own** compare-mode
  toggle; a **"Compare vs <tier>"** selector that auto-tracks current rank with manual override;
  provenance/approximate labeling; demo coverage; screenshot gate.
- **Out:** vision/objective metrics (M7), timeline causality (M8), build analysis + discipline
  (M9). No schema change, no migration, no Riot API change, no new network calls.
- **Item 5 (percentile):** we only have *means*, not distributions → we render **mean-delta
  only**, never a fabricated percentile. True percentiles deferred until distribution data exists.

## 5. Core model (RiftReview.Core)

```csharp
// Data/RankBaselineTable.cs
public sealed record RankBaselineMeta(string Source, string Patch, bool Approximate);

public sealed record RankBaselineTable(
    RankBaselineMeta Meta,
    // canonicalRole ("TOP"/"JUNGLE"/"MID"/"ADC"/"SUPPORT") -> tier ("GOLD"...) -> metricKey -> value
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>>> Cells);
```

```csharp
// Analysis/RankBaselineProvider.cs  (pure static)
public static class RankBaselineProvider
{
    // Loads the embedded seed once (caller passes the deserialized table).
    // Returns null when the (role, tier, metric) cell is absent — caller must handle gracefully.
    public static double? Resolve(RankBaselineTable table, string teamPosition, string tier, string metricKey);

    // Canonicalize Riot TeamPosition -> our role key. STOP-and-report if an unexpected value appears.
    public static string CanonicalRole(string teamPosition);   // MIDDLE->MID, BOTTOM->ADC, UTILITY->SUPPORT, TOP/JUNGLE passthrough

    // Apex tiers (MASTER/GRANDMASTER/CHALLENGER) reuse the DIAMOND row (highest seeded); unranked -> null.
    public static string NormalizeTier(string tier);
}
```

Metric keys reuse the Trends `Def.Key` values: `cs10`, `gold15`, `kda`, `deaths`, `pre15`,
`kp`, `dmg`, `winRate`. Seed populates only `cs10` (and a derived `csPerMin` for the CS-pace
chart) for now; everything else resolves to `null` → own-trailing only.

## 6. Seed table (approximate, owner-editable)

`RiftReview.Core/Data/rank-baselines.json` (embedded resource). CS@10 by role×tier, seeded from
common community CS/min heuristics (`CS@10 ≈ CS/min × 10`), **support intentionally null** (CS is
not a support's job — pushing it would be dishonest coaching):

| Tier | TOP/MID/ADC CS@10 | JUNGLE CS@10 |
|------|------|------|
| IRON | 45 | 40 |
| BRONZE | 50 | 45 |
| SILVER | 55 | 50 |
| GOLD | 60 | 55 |
| PLATINUM | 65 | 58 |
| EMERALD | 68 | 60 |
| DIAMOND | 72 | 64 |

Meta: `{ "source": "community CS/min heuristics", "patch": "<current>", "approximate": true }`.
The UI surfaces this verbatim near the toggle so the owner knows it's a seed.

## 7. App layer

- **Sparkline** (`Controls/Sparkline.cs`): add `Baseline` (`double?` → flat reference line) +
  `BaselineBrush`; render as a dashed horizontal line scaled to the same min/max as the series.
  (A flat line is correct: the rank baseline is one scalar per metric.)
- **LineChart** deep-dive CS-pace: `DeepDiveViewModel` adds a rank-baseline `ChartSeries`
  (flat line at `rankCs10/10` CS/min) when resolvable, distinct dashed brush, **only in Rank
  mode**; Own mode keeps today's trailing-average baseline series. Gold chart unchanged (diff).
- **MetricTrendViewModel**: add `BaselineValue` (double?), `BaselineLabel` (e.g. "Gold avg"),
  `DeltaVsBaseline` (formatted, signed, direction-aware good/bad color), `HasBaseline` (bool).
- **TrendsViewModel**: resolve current tier from latest solo `LpSnapshot`; `CompareMode`
  (`Rank` | `Own`); `CompareTier` (auto = current, with an override list of all tiers);
  recompute baselines + deltas on mode/tier change; expose `BaselineProvenance` string +
  `BaselineApproximate` flag for the label. Unranked / no snapshot → default to Own mode and
  hide the rank selector.
- **TrendsView.xaml**: baseline line on each sparkline; delta badge next to `Current`; a compact
  **Rank ⇄ Own** ToggleButton/SegmentedControl; a **"Compare vs"** ComboBox of tiers (visible
  only in Rank mode); a small muted italic provenance label ("baseline: community heuristics ·
  patch X · approximate").

## 8. Demo seeder

Demo already inserts solo `LpSnapshot`s (Gold range, from M5). Confirm the latest demo solo
snapshot tier resolves to a seeded tier (GOLD) so the demo renders a real rank overlay + delta.
Add nothing if already covered; otherwise nudge one snapshot's tier to GOLD.

## 9. Acceptance criteria

- `dotnet build RiftReview.slnx` clean; `dotnet test RiftReview.slnx` green incl. new
  `RankBaselineProvider` / table-load / canonicalize / normalize-tier tests.
- No schema change / migration / Riot calls.
- Trends shows, per applicable metric: a baseline line on the sparkline, a signed delta-vs-rank
  badge colored by metric direction, in **Rank** mode; switching to **Own** restores the
  trailing-average comparison; the **Compare vs <tier>** selector defaults to current rank and
  overrides on change.
- Deep-dive CS-pace chart shows the rank-baseline line in Rank mode (flat, dashed, labeled).
- Metrics with no seeded cell (kda/deaths/kp/dmg/support-cs10) show **no rank line** and fall
  back to own-trailing with a quiet "no rank baseline" affordance — **never a fabricated number**.
- Provenance/approximate label visible; player WR keeps a 50% anchor (no 51.77% misapplication).
- Demo renders the overlay (Gold); screenshot-verified (real + demo) via subagent text verdict.
- No secrets; `appsettings.json` placeholders only; commits trailer `Co-Authored-By: Claude
  Opus 4.8 <noreply@anthropic.com>`.

## 10. Hand-back (owner)

- The rank numbers are **seeds** — edit `rank-baselines.json` (CS@10 by role×tier) to your own
  trusted source/patch; the "approximate" label is intentional until you do.
- Own mode is the mathematically honest comparison; Rank mode is the directional "what does my
  tier look like" overlay. Confirm both read right on your real data.
