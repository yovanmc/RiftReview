# RiftReview M1 — Deep spine + Champion pool (design)

**Date:** 2026-06-19
**Status:** Approved (brainstorm complete) — ready for implementation planning
**Builds on:** v1 (post-game review spine) — see `2026-06-19-riftreview-v1-design.md`

---

## 1. Context & the roadmap this belongs to

v1 shipped the match-history spine + single-game deep-dive + a thin 4-metric trend strip,
verified end-to-end on live data. The owner's goal for RiftReview is **improving his own
League performance**, and the agreed way to get there is a sequence of milestones, each its
own spec → plan → build:

| Milestone | Theme | Status |
|---|---|---|
| **M1** | Deep spine + Champion pool dashboard | **this spec** |
| M2 | Weakness diagnosis (death-timing clusters, CS-vs-baseline, lost matchups) | planned |
| M3 | Matchup notes (write once, get reminded) | planned |
| M4 | Tilt & session guard | planned |
| M5 | Progress tracking view (rank/LP/champ trends) | planned |

All five are committed; this is only the order. The one feature explicitly cut is the
build/rune scraper (duplicates op.gg/lolalytics, fragile, ToS-risky, near-zero edge).

## 2. M1 scope

**In scope**
- Deepen match history (raise the v1 hard-coded count of 20 → a Settings value, default 150).
- App navigation shell (WPF-UI `NavigationView`: Review / Champions / Settings).
- **Champions dashboard** — hybrid layout (practice-champ cards on top, full table below).
- A minimal **Settings** surface.
- Begin **LP snapshotting** (data collection only — feeds M5, no view in M1).

**Explicitly NOT in M1** (deferred to later milestones)
- Weakness diagnosis / pattern detection (M2).
- Matchup notes (M3). Tilt guard (M4). Rank/LP *view* (M5).
- Manual champ-pool pinning (M1 auto-detects; pinning may come later if wanted).
- Build/rune aggregation (cut entirely).

## 3. Decisions locked during brainstorm

| Decision | Choice | Rationale |
|---|---|---|
| Timeline data at depth | **Store full raw match + timeline for every game** (v1 model, just deeper) | Surgical/MVP; everything stays re-derivable; existing deep-dive untouched. ~150–200 MB DB and ~8–10 min one-time backfill are acceptable locally. Leanness deferred until disk actually pinches. |
| Champ dashboard form | **Option C — hybrid** (practice cards + full table) | Matches a two-champ grind: surfaces "am I improving?" while keeping the long tail accessible. |
| "Currently practicing" | **Auto-detect**: top ≤3 champs by games in the most-recent ~15 games | Zero config; K'Sante/Galio surface naturally. Manual pinning deferred. |
| Navigation | **WPF-UI `NavigationView`** (Review/Champions/Settings) | Native pattern for the stack; M2–M5 all add pages, so build the shell once. |
| Match depth default | **150**, Settings-bounded ~20–300 | Enough history for meaningful aggregates without abusing the dev key or disk. |
| Sparkline metric | **CS@10** per champ (chronological) | The most coachable laning metric; toggle to other metrics can come later. |
| LP snapshot cadence | **One snapshot per sync** | Cheap (2 extra calls); M5's view samples/de-dupes. |
| Champ-pool data source | **Existing lean `matches` columns only** (no timeline reads) | Aggregates are cheap; keeps the dashboard fast. |

## 4. Navigation / information architecture

Introduce a top-level shell hosting a WPF-UI `NavigationView` with three destinations:

- **Review** — the entire existing v1 screen (match-list rail + trend strip + deep-dive),
  moved wholesale into a page. Behavior unchanged.
- **Champions** — new (section 5).
- **Settings** — new (section 7).

This is the one non-trivial refactor in M1: the current `MainWindow` content is extracted
into a `ReviewPage`/`ReviewView`, and a new shell window owns the `NavigationView`. The
existing `MainViewModel` continues to back the Review page unchanged.

> **Build-time gate:** confirm the WPF-UI 4.3 `NavigationView` API (item definition, page
> hosting, DI-friendly page resolution) against the installed package and/or a working
> reference app before trusting representative XAML. Do not invent API surface.

## 5. Champions dashboard (Option C — hybrid)

### Layout
- **"Currently practicing"** (top): up to 3 cards for auto-detected active champs. Each card:
  champion identity (icon + name + role), a prominent **CS@10 trend sparkline**, win-rate,
  KDA, and average deaths.
- **"All champions"** (below): a sortable table — champion, games, win-rate (with W–L),
  KDA, CS@10. Default sort: games descending.
- A **queue filter** defaulting to **Ranked (Solo+Flex)** (queue 420/440), toggle to **All**.
  In All mode, non-laning queues (e.g. Arena — queue ~1700/1750 in real data) appear with
  "—" for CS@10, since `cs_at_10` is null for them. Exact non-ranked queue IDs are not
  enumerated; the filter is simply "ranked = 420/440, everything else = All".

### Practice-champ auto-detection
`practicing` = the top ≤3 champions by game count within the most-recent ~15 games (after the
queue filter is applied). A champion needs ≥2 games in that window to qualify; if none qualify,
the practice section is hidden and only the table shows.

### Data & logic
- A new **pure** `ChampPoolCalculator` in `RiftReview.Core` takes `IReadOnlyList<MatchRow>`
  (the stored matches, queue-filtered) and returns:
  - per-champion aggregate: games, wins/losses, win-rate, KDA = (ΣK+ΣA)/max(1,ΣD),
    avg CS@10 (nulls excluded), avg deaths;
  - per-champion **trend series**: chronological `cs_at_10` values (for the sparkline);
  - the auto-detected practice-champ set.
- Reads **only** existing lean `matches` columns (`my_champion_id`, `win`, `kills/deaths/assists`,
  `cs_at_10`, `queue_id`, `game_start_utc`, `my_team_position`). No timeline access.
- Champion names/icons via the existing `DataDragonClient` (`ChampionName`, and
  `ChampionIconUrl` finally gets a consumer for the card/table icons).
- `RiftReviewDb` gains an `AllMatches(bool rankedOnly)` read (or equivalent high-limit query)
  so the calculator sees the full stored depth, not just the latest 20.

### ViewModels
- `ChampPoolViewModel` (queue filter, practicing cards collection, all-champs rows, sort state).
- `ChampCardViewModel` / `ChampRowViewModel` for the two presentations.
- Reuse the existing hand-rolled `LineChart`/sparkline approach for the card trend (a thin
  sparkline variant is acceptable rather than the full axis-labeled chart).

## 6. Deep spine (history depth + backfill)

- Match depth is read from Settings (`meta` key, default 150).
- **Pagination:** MATCH-V5 caps `count` at 100 per ids call. `GetMatchIdsAsync` usage in
  `SyncService` paginates (`start` 0,100,… up to the configured depth) and concatenates ids.
- **Backfill = a sync at the larger depth.** v1's per-match transaction + "skip already
  stored" (`HasMatch`) already make it **resumable**: a dev-key expiry mid-backfill just
  resumes on the next sync. No new resumability machinery needed.
- Progress reporting (existing `IProgress<SyncProgress>`) reflects the larger total.

> **Build-time gate:** confirm MATCH-V5 ids pagination params (`start`/`count`, max 100) and
> behavior at the end of history against developer.riotgames.com.

## 7. LP snapshots (collection only — no M1 view)

- On each successful sync, fetch current ranked standing and append one row to a new
  `lp_snapshots` table: `(id, taken_utc, queue_type, tier, division, league_points, wins, losses)`.
- Resolution path uses the already-built `GetSummonerByPuuidAsync` (SUMMONER-V4, platform host)
  → then LEAGUE-V4 entries.
- A failed LP fetch must **not** fail the match sync (snapshot is best-effort; log/skip).
- No M1 UI for this — it silently accrues so M5's trend view has real history.

> **Build-time gate (important — Riot has been migrating these):** confirm the correct
> LEAGUE-V4 endpoint — prefer `/lol/league/v4/entries/by-puuid/{puuid}` if it exists; else
> `/lol/league/v4/entries/by-summoner/{summonerId}` using SUMMONER-V4's `id`. Verify
> `LeagueEntryDTO` field names (`queueType`, `tier`, `rank`, `leaguePoints`, `wins`, `losses`)
> against developer.riotgames.com. Do not assume.

## 8. Settings surface

Minimal, single page:
- **Match depth** (numeric, default 150, bounded ~20–300).
- **Default queue filter** (Ranked Solo/Flex vs All) — shared default for Review + Champions.

Persisted in the existing `meta` key/value table (no new settings table). Read at startup and
on sync; changes apply on the next sync/refresh.

## 9. Schema → v2

Single **additive** migration through the existing `RiftReviewDb.RunVersionedMigrations`
runner; bump `LatestSchemaVersion` to 2:
- `CREATE TABLE lp_snapshots (...)` as in §7.
- `matches` / `match_detail` unchanged. Settings live in `meta` (no schema change).
- Migration is idempotent and crash-safe (mirrors the v1 v0→v1 step).

## 10. New / changed code surface (grounding for the plan)

- **Core**
  - `Analysis/ChampPoolCalculator.cs` (new, pure, unit-tested).
  - `Riot/RiotApiClient.cs` — add `GetLeagueEntriesAsync(...)`; `GetMatchIdsAsync` usage paginated in sync.
  - `Riot/Dtos/LeagueEntryDto.cs` (new).
  - `Sync/SyncService.cs` — depth pagination + best-effort LP snapshot after match sync.
  - `Data/RiftReviewDb.cs` — `lp_snapshots` table + insert; `AllMatches(...)` read; v2 migration; `LatestSchemaVersion=2`.
  - A new settings accessor over `meta` (key/value) for match depth / default queue filter —
    kept separate from `RiotOptions`, which holds only the API key / Riot ID / platform.
- **App**
  - New shell window/page host with WPF-UI `NavigationView`.
  - `Views/ReviewView` (extracted from current `MainWindow` content; `MainViewModel` unchanged).
  - `Views/ChampPoolView` + `ChampPoolViewModel` (+ card/row VMs).
  - `Views/SettingsView` + `SettingsViewModel`.
  - DI wiring in `App.xaml.cs` for the new pages/VMs and the league client method.
  - `Demo/DemoSeeder.cs` — extend to a multi-champ dataset so Champions renders under `--seed-demo`.

## 11. Testing & verification

- **Unit (Core):** `ChampPoolCalculator` (aggregates, KDA edge with 0 deaths, trend series
  ordering, practice-champ detection incl. the "<2 games → hidden" case, empty pool);
  LP DTO parse + snapshot insert/read; sync pagination (multi-page id fetch, dedupe vs stored);
  v2 migration (table created, idempotent, v1 DB upgrades cleanly).
- **`dotnet test`** green after each Core task (TDD red→green→commit).
- **Screenshot gate (real data + demo):** launch the app, verify the **Champions** page
  (practice cards with sparklines + all-champs table), **Settings**, and **nav** render
  correctly — inside a Sonnet subagent returning a TEXT verdict + file paths (do not load PNGs
  into the main context). Verify on the real ~150-game DB and on `--seed-demo`.
- **Demo seeder** extended so the dashboard is populated offline.

## 12. Build-time verification gates (consolidated — confirm before trusting representative code)

1. WPF-UI 4.3 `NavigationView` API (page hosting, DI page resolution) vs installed package / reference app.
2. LEAGUE-V4 endpoint (by-puuid vs by-summoner) + `LeagueEntryDTO` fields vs developer.riotgames.com.
3. MATCH-V5 ids pagination (`start`/`count`, max 100, end-of-history behavior).
4. SUMMONER-V4 `SummonerDto.id` presence (needed only if the by-summoner LEAGUE path is used).

## 13. Acceptance criteria

- `dotnet build RiftReview.slnx` clean; `dotnet test` all green.
- Sync pulls up to the configured depth (default 150), paginated, resumable; existing matches skipped.
- Champions page shows auto-detected practice cards (CS@10 sparkline + WR/KDA/deaths) and a
  sortable all-champs table, honoring the Ranked/All filter; renders on real data and demo.
- Settings changes match depth + default filter and persist across launches.
- Each sync appends an `lp_snapshots` row (when LP is fetchable); LP failure never breaks match sync.
- Schema at v2; a v1 DB upgrades in place without data loss.
- No secrets added; appsettings.json still placeholders only.

## 14. What can't be verified here (hand back to owner)

- The **live LEAGUE-V4 LP pull** with a real key (same constraint as v1's live pull) — confirm
  on the owner's machine that a real ranked standing is fetched and a snapshot row is written.
- A **full ~150-game live backfill** end-to-end (time + dev-key expiry behavior) — the owner
  runs it locally; the build verifies pagination/resumability logic via tests + a bounded live check.
