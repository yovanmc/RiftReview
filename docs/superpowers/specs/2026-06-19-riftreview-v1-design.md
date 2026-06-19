# RiftReview v1 — Design Spec

Date: 2026-06-19
Status: Approved (brainstorming) → ready for implementation planning
Owner: Yovan Collins (yovanmc)

## 1. What this is

RiftReview is a **personal, single-user, read-only, non-commercial** Windows desktop app for
self-coaching at League of Legends. It pulls **my own** match history from the official Riot API
into a local SQLite store and surfaces post-game performance trends.

Stack matches my other apps (VideoShelf / Curio):

- **.NET 10**, **WPF**, **WPF-UI 4.3**, **CommunityToolkit.Mvvm** (MVVM)
- **Microsoft.Data.Sqlite** (raw SQL, no EF)
- **Microsoft.Extensions.Hosting** generic host for DI + configuration
- Black-glass dark theme + **Hextech Gold (`#C8AA6E`)** accent

## 2. v1 scope

**Build only:** the match-history spine + post-game review.

1. Resolve my Riot ID → PUUID (ACCOUNT-V1), then incrementally pull + cache my recent matches
   (MATCH-V5 detail + timeline) into local SQLite.
2. One analysis screen:
   - **Primary — single-game deep-dive:** for a selected match, three timeline-derived views.
   - **Secondary — cross-game trend strip:** four scalar metrics across recent games.

**Explicitly deferred (do NOT build in v1):**
champ-pool dashboard, multi-source build aggregator / web scraping (lolalytics/coachless),
rank/LP tracking, tilt-guard, DPAPI encrypted key store (User Secrets is fine for now),
multi-account, anything social/multi-user.

## 3. Locked design decisions (from brainstorming)

| Decision | Choice |
|---|---|
| Review lens | Single-game deep-dive (primary) **+** thin cross-game trend strip |
| Gold-differential-over-time | **Two lines:** vs lane opponent **and** vs enemy team total |
| CS-per-minute curve | Game curve **vs my own same-role rolling baseline** (data-grounded; no invented target) |
| Death timing | **Markers on the gold chart** (deaths shown in context of gold swings) |
| Trend strip metrics | **Mirror the deep-dive (4):** win/loss, deaths, CS@10, gold-diff@15 |
| Sync model | **Manual "Sync" button**, last ~20 matches, **incremental** (skip already-stored), throttled |
| Queue handling | **Store all queues**; trend strip + baseline default to **Ranked Solo/Flex**, toggle to **All** |
| Layout | Match-list rail (left) + trend strip (top of main) + game header + stacked gold/CS charts |
| Accent / theme | Black-glass + Hextech Gold `#C8AA6E` |
| Charting | **Hand-rolled WPF** line-chart control (Canvas/Polyline/Path) — zero extra dependency |

## 4. Solution shape (projects)

- `RiftReview.Core` (`net10.0-windows`) — Riot API client, Data Dragon client, rate limiter,
  SQLite store, sync service, analysis service. **No WPF.**
- `RiftReview.App` (`net10.0-windows`, `WinExe`) — WPF + WPF-UI + CommunityToolkit.Mvvm,
  generic Host for DI + config, views + view models, hand-rolled chart control.
- `RiftReview.Core.Tests`, `RiftReview.App.Tests` (xUnit), with `InternalsVisibleTo`.
- Solution: `RiftReview.slnx` (matches VideoShelf/Curio).

## 5. Data layer (SQLite, Microsoft.Data.Sqlite)

Versioned `user_version` migration runner (same pattern as VideoShelf/Curio). v1 schema:

- `meta(key TEXT PRIMARY KEY, value TEXT)` —
  `puuid`, `riot_id` (`GameName#TAG`), `platform` (e.g. `na1`), `regional_route` (e.g. `americas`),
  `ddragon_version`, `last_sync_utc`.
- `matches(`
  `match_id TEXT PRIMARY KEY, queue_id INTEGER, game_start_utc INTEGER, duration_s INTEGER,`
  `patch TEXT, my_champion_id INTEGER, my_team_position TEXT, win INTEGER,`
  `kills INTEGER, deaths INTEGER, assists INTEGER, cs INTEGER,`
  `cs_at_10 INTEGER, gold_diff_at_15 INTEGER,`
  `opponent_participant_id INTEGER, opponent_champion_id INTEGER, synced_at INTEGER)` —
  lean row; `cs_at_10`, `gold_diff_at_15`, `deaths`, `win` are **precomputed scalars** so the
  trend strip is a plain indexed `SELECT`.
- `match_detail(match_id TEXT PRIMARY KEY, match_json TEXT, timeline_json TEXT)` —
  **raw API blobs kept as the re-derivable source of truth.** We never re-fetch a stored match;
  single-game curves are parsed from `timeline_json` on demand (cheap for one game). If extraction
  logic changes later, we re-derive from these blobs rather than re-hitting Riot.
  Foreign key to `matches(match_id)`.

Rationale: precomputed scalars keep the cross-game query trivial and fast; raw blobs keep the
expensive per-frame data without bloating list queries and make all derived values recomputable
(recoverable / crash-safe ethos).

## 6. Riot API integration (Core)

> **Endpoint verification gate:** exact ACCOUNT-V1 / SUMMONER-V4 / MATCH-V5 paths and the current
> **puuid-vs-summonerId** variant (Riot is mid-migration) MUST be verified against
> developer.riotgames.com at build time. Do **not** hardcode from a single remembered example.

- `RiotApiClient` (typed `HttpClient` via `IHttpClientFactory`):
  - `ResolvePuuidAsync(gameName, tagLine)` — ACCOUNT-V1, **regional** routing.
  - `GetSummonerByPuuidAsync(puuid)` — SUMMONER-V4, **platform** routing.
  - `GetMatchIdsAsync(puuid, start, count)` — MATCH-V5, **regional**.
  - `GetMatchAsync(matchId)` / `GetMatchTimelineAsync(matchId)` — MATCH-V5, **regional**.
  - Auth header `X-Riot-Token`.
- **Routing split (load-bearing):** ACCOUNT-V1 + MATCH-V5 use **regional** hosts
  (`americas` / `europe` / `asia`); SUMMONER-V4 uses **platform** hosts (`na1`, `euw1`, …).
  Platform→regional map: `na1/br1/la1/la2/oc1 → americas`, `euw1/eun1/tr1/ru → europe`,
  `kr/jp1 → asia` (verify exact set at build). Platform is configured; regional is derived.
- `RiotRateLimiter` — honors dev-key limits (**20 req/s**, **100 req/2 min**) and `Retry-After`
  on HTTP 429. Uses an **injected clock** so tests are deterministic (no real sleeping).
- `DataDragonClient` — fetches the current version, then `champion.json` / `item.json`; caches
  id→name and icons under `%LOCALAPPDATA%\RiftReview\ddragon\`.

### Error handling

- `401/403` → key expired/invalid (daily-key reality): clear "set a fresh Riot key" message.
- `429` → auto-backoff honoring `Retry-After`; surface "waiting Ns".
- `404` on Riot ID → "couldn't resolve {riotId}".
- Network errors → surfaced, retryable.

## 7. Sync flow (`SyncService`)

Manual "Sync" button. Steps:

1. Ensure PUUID (resolve from configured Riot ID via ACCOUNT-V1 if not in `meta`).
2. `GetMatchIdsAsync(start=0, count=20)` (all queues).
3. Skip IDs already in `matches`; for each **new** match:
   - fetch detail + timeline (throttled),
   - extract scalar columns,
   - identify **lane opponent** (match my `teamPosition` to the enemy participant in the same
     position; participants 1–5 = team 100, 6–10 = team 200),
   - insert `matches` + `match_detail` in a **per-match transaction**.
4. Update `meta.last_sync_utc`.

Partial sync is safe: each match commits independently, so a mid-sync failure never loses progress
and the next sync resumes from the gap. ~40 calls for a full 20-match pull — comfortably under limits.
Match count is hardcoded to 20 in v1 (Settings-exposed later).

## 8. Analysis

### Single-game deep-dive (`matchId`)

Parse stored `timeline_json`:

- **Gold differential (two lines):** per frame, `myTotalGold − laneOppTotalGold` and
  `myTeamTotalGold − enemyTeamTotalGold`. **Death markers** overlaid = `CHAMPION_KILL` events
  where the victim is me, plotted at their timestamp on the gold chart.
  Fallback: if no clean lane opponent (e.g. ARAM/odd roles), draw only the team line + a note.
- **CS per minute:** this game's CS/min curve (`minionsKilled + jungleMinionsKilled` per frame)
  plus a dashed **same-role rolling baseline** = average per-minute CS across recent same-role
  games (parsed on demand from their stored timelines; cached in memory).

### Cross-game trend strip

Last 20 games, default **Ranked Solo/Flex** (toggle **All**), ordered by date:
win/loss dots, deaths, CS@10, gold-diff@15 — read straight from `matches` columns.

### Scalar definitions (documented in-code)

- `cs_at_10` = my CS at the timeline frame nearest **600 000 ms**.
- `gold_diff_at_15` = **lane** gold diff (`me − lane opponent`) at the frame nearest **900 000 ms**.

## 9. UI (WPF + WPF-UI)

Approved layout (mockup-validated):

```
+----------------+------------------------------------------+
| [Sync] Ranked▾ |  TREND STRIP (last 20): W/L · Deaths ·    |
|  match list    |               CS@10 · Gold@15             |
|  ▸ game (sel)  +------------------------------------------+
|  ▸ game        |  Header: champ · role · result · time    |
|  ▸ game        |  Gold diff over time (2 lines + ✕deaths)  |
|  ...           |  CS/min curve (you vs same-role baseline) |
+----------------+------------------------------------------+
```

- MVVM (CommunityToolkit.Mvvm). Match-list rail = the spine; selecting a row drives the deep-dive.
- Sync button + queue filter (Ranked / All) in the rail header.
- **Theming gotcha (from VideoShelf):** apply accent via `ApplicationAccentColorManager.Apply`
  with `#C8AA6E` **before first render**; Mica off.
- **Charts — hand-rolled WPF:** a small reusable line-chart control built on `Canvas` +
  `Polyline`/`Path`, supporting: multiple series, a zero gridline, axis labels, a dashed baseline
  series, and point markers (deaths). No external chart library. (Rationale: only 3 simple line
  charts, full black-glass theme fidelity, zero dependency conflict with WPF-UI.)

## 10. Configuration & secrets (public repo)

- `UserSecretsId` in `RiftReview.App`. `appsettings.json` ships **placeholders only**.
- `appsettings.local.json`, `.env`, and `secrets.json` are **gitignored from the first commit**.
- Bind a `RiotOptions { ApiKey, RiotId, Platform }` via `Microsoft.Extensions.Configuration` +
  `IOptions`. User Secrets override placeholders. Regional route derived from platform.
- Ready-to-run setup block left for Yovan (run locally, never paste real values into chat):

  ```
  dotnet user-secrets set "Riot:ApiKey" "RGAPI-..."
  dotnet user-secrets set "Riot:RiotId" "GameName#TAG"
  dotnet user-secrets set "Riot:Platform" "na1"
  ```

- **Shipped build (deferred):** store the key encrypted via Windows **DPAPI** under
  `%LOCALAPPDATA%\RiftReview\`. Not in v1.

## 11. Repository & git

- Local git repo at `C:\Agent Projects\RiftReview`. Commit identity:
  `yovanmc <yovanmc@users.noreply.github.com>` (plain `git commit`; never override per-commit).
- Public GitHub repo `yovanmc/RiftReview` created + pushed at the **first execution commit**
  (after this spec is reviewed and real content exists), not as a spec-only history.
- **Before the first push:** explicitly confirm no secret is staged
  (`git status` / `git ls-files` review). Treat all pushed public history as permanent.

## 12. Testing & verification

### Offline, fixture-driven (the core logic — fully testable here)

- Riot request construction: routing split (ACCOUNT/MATCH → regional host, SUMMONER → platform
  host), correct paths, `X-Riot-Token` header — via a stubbed `HttpMessageHandler` (no live calls).
- `RiotRateLimiter` behavior (respects limits, honors `Retry-After`) with an injected clock.
- **Timeline extraction math** against **synthetic-but-schema-accurate** MATCH-V5 fixtures:
  gold-diff two lines, CS/min series, CS@10, gold-diff@15, death minutes, lane-opponent matching,
  team grouping. (Confirm the timeline JSON shape against Riot docs before writing the fixture.)
- DB upsert + incremental sync against a temp SQLite file.

### Screenshot verification (offline)

- A dev-only `--seed-demo` launch flag loads the synthetic fixture into a throwaway DB so the
  **real charts render without a live key**. A **Sonnet subagent** screenshots the running app and
  returns a **text verdict** (standing rule — never load PNGs into the main session unless asked).
  The seed is clearly fake data and is never shipped or presented as real matches.

### What I CANNOT verify in this environment (hard-rule flag)

- The actual **live pull** against Riot: real dev key, real network, my real match data, and the
  exact **live JSON matching the fixture assumptions**. **Yovan must verify end-to-end sync
  locally.** A short manual checklist will be provided (set secrets → Sync → confirm matches +
  charts populate → confirm key-expiry and rate-limit messaging).

## 13. Non-goals & risks

- Daily dev-key expiry → surfaced clearly as a key-expired state.
- MMR/LP are not official via these APIs and are out of v1.
- No web scraping in v1.
- Single-user, read-only, non-commercial; respects Riot API terms.
