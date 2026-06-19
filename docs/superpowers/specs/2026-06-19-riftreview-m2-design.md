# RiftReview M2 — Per-Champ Improvement Trends — Design

**Status:** Approved for planning (2026-06-19).
**Milestone:** M2 of the 5-milestone self-coaching roadmap (M1 deep spine + champ pool ✅ shipped; M2 = weakness diagnosis, realized here as an *improvement-trajectory* view).

## 1. Goal

Answer one question for the owner: **"Am I getting better on my main champions?"** M2 adds a **Trends** page that, per champion, trends a fixed set of performance metrics over time using a trailing rolling-window average, and renders each metric as a trajectory with a plain-English verdict (improving / steady / declining).

This is **self-relative** analysis (your recent block vs your earlier block) and, for laning, **opponent-relative** (gold@15 vs the same-role lane opponent). It deliberately does **not** compare against rank/population benchmarks — that needs an external data source and is out of scope (see §11).

## 2. Locked design decisions (from brainstorming)

| Decision | Choice |
|---|---|
| Primary job | Improvement trend over time |
| Metrics | Laning (CS@10, gold@15), Survivability (deaths, pre-15 deaths), Combat (KDA, kill participation, damage share), Outcomes (win rate) |
| Slice | **Per-champ** (mains only, eligibility-gated), champ-selectable |
| Time axis | **Trailing N-game rolling average** on that champ |
| Layout | **Option C — trajectory rows**, row click → big rolling-trend chart (Option B drill-down) |
| Queue | Ranked only (420/440), matching M1 |
| Window N | Default **10**, exposed as a Setting |

## 3. Metrics and "good" direction

Each metric has a direction in which movement is an improvement:

| Metric | Source | Good direction |
|---|---|---|
| Win rate | stored (`win`) | ↑ |
| CS@10 | stored (`cs_at_10`) | ↑ |
| Gold@15 vs lane | stored (`gold_diff_at_15`) | ↑ |
| KDA | stored (K/D/A) | ↑ |
| Deaths / game | stored (`deaths`) | ↓ |
| Pre-15 deaths | **new** (timeline) | ↓ |
| Kill participation | **new** (match blob) | ↑ |
| Damage share | **new** (match blob) | ↑ |

**LP / rank is account-level, not per-champ** — solo-queue LP can't be cleanly attributed to one champion. It is shown as an account-level strip at the top of the page (current tier/division/LP + a delta over a recent window), and is the heart of the future M5 view. It is **not** a per-champ trajectory row.

## 4. The verdict rule (precise)

For a given champ + metric, take that champ's **ranked** games ordered oldest→newest, producing a value series `v[0..k-1]` (one value per game; win rate uses 1/0 per game; gold@15 may be null for Arena/no-lane games and is excluded from that metric's series).

- **Rolling series (the sparkline):** point `i` = average of the trailing window `v[max(0, i-N+1) .. i]`. This is the smoothed line shown per row and in the drill-down chart.
- **Current block:** average of the last `N` games.
- **Prior block:** average of the `N` games immediately before the current block (games `[k-2N .. k-N)`).
- **Signed delta:** `(current - prior)`, then multiplied by the metric's direction (+1 for ↑-good, −1 for ↓-good) to get an *improvement* delta.
- **Classification** (with a dead-band so noise isn't read as movement):
  - `improvement delta` beyond a per-metric threshold → **Improving**
  - within the dead-band → **Steady**
  - beyond the threshold in the wrong direction → **Declining**
  - Thresholds default to ~5–8% relative change, with sensible absolute floors per metric (e.g. win rate in points, deaths in absolute count); exact constants are set in the implementation plan and unit-tested.
- **Eligibility / low-data:**
  - `k ≥ 2N`: full verdict.
  - `N ≤ k < 2N`: show current value + sparkline but verdict = **"Building — need more games"** (no prior block to compare).
  - `k < N`: champ is **not listed** in Trends.

## 5. Rolling window and eligibility

- **N** defaults to **10**, stored via `SettingsStore` (`settings.trend_window`, clamped e.g. 5–30), editable on the Settings page. Changing N re-computes everything.
- A champion appears in the Trends champ-selector if it has **≥ 2N** ranked games (≈20 at the default). With 631 stored games / 408 ranked, the owner's real mains clear this comfortably; one-off champs are correctly excluded.

## 6. Schema v2 → v3 (derived metric scalars + backfill)

Three metrics aren't stored yet. Following the established architecture (lean precomputed scalars in `matches`, raw blobs in `match_detail` as the re-derivable source), M2 **precomputes** them into new columns so the Trends view stays fast (reads scalars, never re-parses blobs at view time).

- **Migration (`RiftReviewDb`, `LatestSchemaVersion` 2→3):** in the `v < 3` block, additive `ALTER TABLE matches ADD COLUMN`:
  - `kill_participation REAL` (nullable; 0–1)
  - `damage_share REAL` (nullable; 0–1)
  - `deaths_pre15 INTEGER` (nullable)
  - Set `PRAGMA user_version = 3`. Purely additive — a v2 DB upgrades in place with no data loss.
- **Backfill (one-time, local, zero Riot calls, idempotent/resumable):** a routine processes `matches` rows where the new columns are `NULL`, reading each row's stored `match_detail` blobs, recomputing via the extractors, and `UPDATE`-ing the row. Because it reads the **immutable** raw blobs and only fills null rows, it is safe to interrupt and re-run (guard-destructive-ops principle — never mutates the raw source). The heavy part is the timeline parse (pre-15 deaths); KP and damage share come from the smaller match blob. The backfill runs once off the UI thread with a status indicator ("Preparing trends… N/631"); the Trends page shows a "preparing" state until complete.
- **Forward population:** `SyncService` computes and stores the three new scalars for every newly synced match (so the backfill is only ever needed for pre-M2 rows).

## 7. Derived metric definitions

Computed from data already in the stored blobs (no new Riot endpoints):

- **Kill participation** = `(myKills + myAssists) / teamKills`, where `teamKills` = sum of `kills` across the 5 participants on my team (`teamId`). Guard `teamKills == 0` → 0.
- **Damage share** = `myDamageToChampions / sum(teamDamageToChampions)` over my team. Requires extending `ParticipantDto` to read the champion-damage field from the match blob (**build-time gate §12** — confirm the exact field name against a real stored blob; do not assume). Guard divide-by-zero → 0.
- **Pre-15 deaths** = count of `CHAMPION_KILL` timeline events whose `victimId` == my participant id and `timestamp < 900000` ms (15:00). Reuses the same event parsing already used for the deep-dive death markers.

`MatchExtractor` / `TimelineExtractor` gain these computations; `MatchSummary` / `MatchRow` gain the three fields (nullable for rows not yet backfilled).

## 8. Architecture

**Core (`RiftReview.Core`)**
- `Data/MatchRow.cs` — add `double? KillParticipation, double? DamageShare, int? DeathsPre15`.
- `Data/RiftReviewDb.cs` — v3 migration; `AllMatches`/`RecentMatches`/`UpsertMatch` include the new columns; backfill query + update API.
- `Riot/Dtos/MatchDtos.cs` — extend `ParticipantDto` with the champion-damage field (name confirmed via §12).
- `Analysis/MatchExtractor.cs` — compute KP + damage share in `Summarize` (or a sibling method).
- `Analysis/TimelineExtractor.cs` — add `DeathsBeforeMinute(timeline, myParticipantId, minute)`.
- `Analysis/ChampTrendCalculator.cs` (**new, pure**) — given a champ's ranked `MatchRow`s + N, produce per-metric `MetricTrend` (rolling series, current, prior, signed delta, verdict, eligibility). Mirrors `ChampPoolCalculator`.
- `Analysis/TrendModels.cs` (**new**) — `ChampTrend`, `MetricTrend`, `TrendVerdict` enum (Improving/Steady/Declining/Building), metric key/direction metadata.
- `Sync/DerivedMetricsBackfill.cs` (**new**) — the idempotent backfill routine over `match_detail` blobs.
- `Sync/SyncService.cs` — populate the three scalars on new matches.
- `Configuration/SettingsStore.cs` — `TrendWindow` accessor (meta-backed, clamped).

**App (`RiftReview.App`)**
- `AppShell.xaml(.cs)` — add a **Trends** `NavigationViewItem` (Review · Champions · Trends · Settings); add `--page trends` to the existing test selector.
- `ViewModels/TrendsViewModel.cs` (**new**) — eligible champ list, selected champ, account LP strip (from `GetLpSnapshots`), the metric rows (`MetricTrendViewModel`), the selected-metric drill chart; reacts to champ + window changes; surfaces the "preparing" backfill state.
- `Views/TrendsView.xaml(.cs)` (**new**) — layout C: champ chips, LP strip, trajectory rows (label + value + `Sparkline` + verdict + delta), row-click expands the selected metric into a larger rolling-trend chart (reuse the hand-rolled `LineChart` or a scaled `Sparkline`).
- `ViewModels/SettingsViewModel.cs` + `Views/SettingsView.xaml` — add the trend-window control.
- `App.xaml.cs` — register the new VM/view; trigger the backfill at startup (off-thread) when needed.

**Black-glass + Hextech Gold** theme throughout, consistent with M1; verdict colors: improving = win-green, declining = loss-red, steady/building = muted.

## 9. Data flow

Sync (or startup backfill) → `matches` rows carry all scalars incl. KP/damage/pre-15 → `TrendsViewModel` loads the selected champ's ranked rows via `AllMatches(rankedOnly: true)` filtered to the champ → `ChampTrendCalculator.Build(rows, N)` → `MetricTrend` per metric → layout-C rows + drill chart. LP strip reads `GetLpSnapshots()`.

## 10. Error / empty / low-data states

- **Backfill in progress:** Trends shows "Preparing trends data… N/total"; populates when done.
- **No eligible champs** (none with ≥2N ranked games): friendly empty state ("Play more ranked games on a champ to see trends").
- **Champ N≤k<2N:** value + sparkline + "Building — need more games" instead of a verdict.
- **Metric with all-null values** for a champ (e.g. gold@15 on an Arena-heavy champ): that row reads "no lane data".
- **No LP snapshots:** hide the account LP strip.
- **LP delta needs time:** the 30-day delta is only meaningful once snapshots span real calendar time; until then the strip shows current standing with a muted "delta pending" note (snapshots currently cluster on backfill day).

## 11. Out of scope for M2

- External rank/population benchmarks ("vs a typical Gold mid") — requires a new data source; separate future discussion.
- Champion-square icons on the chips — easy polish, deferred (keeps M2 focused on the trend engine).
- Full LP/rank history view — that is **M5**; M2 only surfaces current standing + a recent-window delta.

## 12. Build-time verification gates (confirm before trusting code)

1. **Champion-damage field name:** grep a real stored `match_detail` match blob (we have 631) to confirm the exact participant field for champion damage (expected `totalDamageDealtToChampions`) before extending `ParticipantDto`. Do not assume.
2. **Timeline kill-event shape:** confirm `CHAMPION_KILL` events expose `victimId` + `timestamp` as already parsed for death markers, against a real timeline blob.
3. **SQLite ALTER TABLE ADD COLUMN** integrates with the existing `RunVersionedMigrations` + `PRAGMA user_version` runner, and the new columns flow through the `matches` SELECT/UPSERT (update explicit column lists if `SELECT *` isn't used).
4. **WPF-UI NavigationView** 3-item menu (already proven in M1).

## 13. Acceptance criteria

- `dotnet build` clean (0 warnings); `dotnet test` all green (incl. new calculator/extractor/migration/VM tests).
- Schema at v3; a v2 DB upgrades in place without data loss; derived metrics backfilled for all existing rows (idempotent, zero Riot calls).
- Sync populates KP / damage share / pre-15 deaths on new matches.
- Trends page lists champs with ≥2N ranked games; per metric shows the trailing-N value, a smoothed rolling-trend sparkline, the correct verdict (good-direction aware), and the delta; a row click opens the larger rolling-trend chart.
- Account LP strip shows current standing (or hides when no snapshots).
- Trend window N is editable in Settings and re-computes the view.
- No secrets added; `appsettings.json` placeholders only.

## 14. Hand-back (live verification — owner runs locally)

- Sanity-check the backfilled derived metrics on real data (KP / damage share in plausible ranges; pre-15 deaths reasonable).
- Confirm verdicts and sparklines look right for a known main (e.g. K'Sante) across the real 408-ranked dataset.
- The LP-strip delta becomes meaningful only as `lp_snapshots` accumulate over real calendar time (current snapshots cluster on the backfill day).
