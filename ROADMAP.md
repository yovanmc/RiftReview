# RiftReview — ROADMAP

> Source of truth for what to build next. Follows the `/roadmap` workflow
> (Opus plans/researches · Sonnet subagents implement · ping at every phase handoff).

## North Star (2026-07-07) — read NORTHSTAR.md before planning

**Vision one-liner:** RiftReview, finished, is the permanent record of the owner's League career
and the instrument he reviews it with — eventually with a coaching layer on top. The workbench
(queued milestones below) comes first; goals/trend tracking and the coaching layer are far-horizon
stages, gated on real usage of the workbench.

**Path phases** (full detail in `NORTHSTAR.md`):
- **Phase 0 — Friction + key strategy (first, already locked).** Deploy to `C:\Self Apps\RiftReview\`;
  one-action sync-day key workflow; investigate whether a Riot personal-product registration grants
  a persistent key (**UNVERIFIED** — daily-expiring dev keys and continuous capture don't coexist).
- **Phase 1 — Workbench completion.** M11 → M12 → M13, then U1 (usage window) starts the week M13
  merges, **with no gap**.
- **Phase 2 — Permanent-record infrastructure.** Scheduled/semi-automatic sync, gap detection that
  warns as unsynced games approach Riot's match-history retention window (**Riot match-v5 retention
  window is UNVERIFIED — verify before building the warning threshold**), DB backup rotation, NAS
  archival (once the DS423+ exists).
- **Phase 3 — Map + scrubber.** M15 → M16 → M17, sequenced behind U1.
- **Phase 4 — Goals & trend tracking.** Owner-defined focus areas over metrics the workbench
  already computes; far-horizon, post-M17, post-usage.
- **Phase 5 — The coaching layer.** Far horizon; pattern mining over the accumulated archive,
  gated on sustained workbench usage + the owner explicitly asking for it.

**Locked decisions:**
- **⚠ SUPERSEDED 2026-08-16 (owner-ruled) — completeness bar M11–M13 is CUT from the pre-usage path.**
  The 2026-07-03 lock ("M11+M12+M13 ship before U1") is replaced by the plan-of-record below: real-game
  test → F0-real → B5 → REC post-game card on manual sync → U1. M11–M13 are re-decided by the U1 log,
  not before it. Recorded as a divergence from the owner-locked bar; ruled by the owner in the
  Free-Reign pass (`C:\Agent Zone\FreeReign-2026-08\_RULINGS-2026-08-16.md`, RiftReview.md §9 as
  amended by §0).
- **The honesty tripwire (kept, re-aimed):** if the plan-of-record ships and real usage still doesn't
  happen within U1, the friction was never the blocker — re-grill the project before building anything.

**Project status: PARKED (2026-08-16, round 3 ruling) — planning only, no build.** The block below is
the decided plan for un-parking; nothing in it is scheduled until the owner un-parks.

## Plan-of-record for un-parking (2026-08-16; owner-approved direction; no build until un-parked)
Source: `C:\Agent Zone\FreeReign-2026-08\RiftReview.md` §9 as amended by §0 (dissect pass 1). In order:
0. **Real-game test (free, ~20 min, no code):** run the existing Debug build against the real DB and review
   ONE real game (not `--seed-demo`). Write down: does the deep-dive clip? hitch on click? do M6–M10 say
   anything about a remembered game? This can falsify everything below for free.
1. **F0-real (S):** DPAPI key store + "Paste new key" field; drop `appsettings.json`/`AddJsonFile`/UserSecrets
   dependency; publish; exe in `C:\Self Apps\RiftReview\`.
2. **B5 (S):** async cancellable deep-dive load, cached baselines, `ScrollViewer`, ranked filter + sample size
   on the baseline label, de-imperative the tilt banner.
3. **B3 part 1 (S, HIGH — dissect twice):** WAL + `PRAGMA busy_timeout=5000` on every open + backup-before-migrate
   via the SQLite online backup API (**UNVERIFIED overload in the pinned Microsoft.Data.Sqlite — confirm**) or
   `wal_checkpoint(TRUNCATE)` under a `Global\RiftReview.Migrate` mutex + `integrity_check` on the copy.
4. **REC post-game card (M) on MANUAL sync:** five tiles each with baseline + sample size, spine chart with
   static event pins, capture strip, "what this game does not know" panel; workbench behind a button.
5. **U1 (~3 weeks):** real games reviewed per week; blockers only. The read must distinguish "capture ran,
   nothing to review" from "capture did not run" if a capture host exists.
6. **B4 MCP read surface (S–M), in parallel with U1:** read-only `get_schema`/`query_data`-style with an
   engine-level ATTACH-deny authorizer copied WITH its RED-first test (Reserve pattern); logs `tool_calls`
   from day one.
7. **B1 capture service — ONLY on trigger:** built only if U1 shows reviews happening but capture gaps are the
   limiter, or a week of games is lost to a forgotten sync. Preconditions: B3 part 1 landed; additive
   `SyncResult.PerMatchOutcomes` (§0 #1); GUI-held `Global\RiftReview.App` mutex so swaps refuse while the GUI
   is open (§0 #2b); `capture_runs` crash reconciliation (§0 #7); exit code 3 = 30-min re-fire (§0 #8); for the
   SCHEDULED variant the long-lived-key question is a precondition OR a consecutive-failure counter surfaces
   amber after N failures (§0 #10). B6 (extend `ParticipantFrameDto`/`ParticipantDto`) rides with B1.
8. **B3 part 2 (M):** rotation gated on off-site verification (§0 #4), NAS archive with SHA256, gap-watch.
9. **Then** re-read the U1 log and choose between M11–M13, the phone card, and the map/scrubber arc.

**Legend:** ✅ Merged · 📝 Plan ready (execute next) · 🔬 Researching/Planning · [ ] Not started (plan first)

## Definition
Single-user, **local-only, post-game, data-honest** League of Legends self-coach.
C#/.NET 10 WPF (+ WPF-UI), `RiftReview.Core` (no-WPF, testable) + `RiftReview.App`.
SQLite (schema v3). Riot API only. Black-glass + Hextech Gold (#C8AA6E) theme.
Repo: github.com/yovanmc/RiftReview · Specs: `docs/superpowers/specs/` · Plans: `docs/superpowers/plans/` · Research: `docs/research/`

## Conventions
- Build: `dotnet build RiftReview.slnx -v minimal` · Test: `dotnet test RiftReview.slnx`
- Run (demo, no key): `dotnet run --project src/RiftReview.App -- --seed-demo`
- Commit author = repo default (`yovanmc`); **NO `--author`**. Trailer on every commit:
  `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`
- Secrets: User Secrets (dev); `appsettings.json` placeholders only (never a real `RGAPI-` key).
- Screenshot gate: `.m?shots/` harness (PrintWindow + DisableHWAcceleration workaround); a
  **Sonnet subagent** views the PNGs and returns a **text verdict** — never load PNGs into the
  controller. PNGs gitignored.
- Merge: PR → foreground `gh pr checks <#> --watch` → `--merge --delete-branch` from master.
- CI: `.github/workflows/ci.yml` (`build-and-test`) — added 2026-07-02 via PR #8.

## Non-goals
- **No single composite "RiftScore":** RiftReview will never reduce a player or game to one opaque score. The decomposed, per-metric, number-under-every-verdict approach is the architecture; any future score-like surface must decompose into named, individually-numbered components.
- **No external/recommended-build or live-overlay/draft-scouting data:** item 16 (recommended-build comparison against aggregators) and any live-game overlay or draft-scouting integration are permanently out of scope. Data Dragon is used only for item names + the completed-item filter; all win-rate signal is own-games-only.
- **Never fabricate:** sparse baselines stay sparse (absent cell → no number, not a made-up default); own-games-only builds require ≥3 games or show "not enough games yet."

## Shipped history (condensed)
Verbose per-milestone decision-log prose (gotchas, exact math, screenshot-harness detail) moved to
`docs/roadmap-archive-2026-07.md` on 2026-07-07; older shipped rows (M1–M7) moved to
`docs/ROADMAP-archive.md` on 2026-07-14 — three most recently merged below.

| # | Title | PR | What shipped |
|---|-------|----|----|
| 8 | Timeline causality | #3 | gold-diff swing marker + death context + back-timing cadence + turning-point lag; EventDto +2 fields. Suite 133. |
| 9 | Build analysis + discipline | #4 | own-best-build per champ (Data Dragon item.json for names only) + number-under-every-verdict audit + no-composite-score non-goal enshrined. Suite 155. |
| 10 | By game phase | #5 | deep-dive "By game phase" card (Early/Mid/Late × gold/CS/deaths/KP, own-role-baseline deltas). Suite 166. **M6–M10 competitive-research expansion COMPLETE.** |

Full detail, gotchas, and math for each: `docs/roadmap-archive-2026-07.md`.

## Milestones (queued)
Older shipped milestones (full detail): [docs/ROADMAP-archive.md](docs/ROADMAP-archive.md)

| # | Title | Status | Plan | PR | Notes |
|---|-------|--------|------|----|-------|
| F0 | Friction + key strategy | [ ] Not started | — | — | Inserted before M11 (North Star, 2026-07-07). Deploy packaged build to `C:\Self Apps\RiftReview\`; one-action sync-day key workflow; investigate Riot personal-product key persistence (**UNVERIFIED**) — if unavailable, script re-keying to a single prompt |
| 11 | Lane-opponent diff | [ ] Deferred post-U1 (2026-08-16) | — | — | Gold **and XP** diff vs the direct lane opponent across the game, in the deep-dive. FIRST task: verify per-participant `xp` + lane-opponent resolution against stored `timeline_json` frames (believed present in match-v5 participantFrames — UNVERIFIED 2026-07-03) |
| 12 | Head-to-head stat panel | [ ] Deferred post-U1 (2026-08-16) | — | — | Per-game scoreboard vs laner: damage dealt, KP, gold earned, damage to towers, vision, CS — deltas highlighted. Data from stored match/timeline JSON only |
| 13 | Timeline explorer | [ ] Deferred post-U1 (2026-08-16) | — | — | Pick any metric(s), rendered across game time in the deep-dive. Also builds the time-axis plumbing M17 (scrubber) needs |
| U1 | Usage window (~3 weeks) | [ ] Not started | — | — | Bar = plan-of-record steps 0–4 (2026-08-16 ruling; M11–M13 no longer gate U1). Owner reviews after every session; blockers fixed immediately, everything else logged as the M14 backlog. Reserve-style usage-before-features gate |
| 14 | Usage-fed fixes (perf + bugs) | [ ] Not started | — | — | Backlog = the U1 log. Speculative perf/bug work before U1 was DECLINED (owner 2026-07-03 — no observed defects yet) |
| 15 | Map foundation | [ ] Not started | — | — | Rift map render + timeline→map coordinate transform (verify empirically) + death/kill locations with phase filter. MUST verify position-data granularity (believed 60s participant frames + exact-time events) and map-asset source/licensing before M17 is planned |
| 16 | Ward map + position trail | [ ] Not started | — | — | Ward placed/cleared locations; per-minute own-position trail (roam timing, lane presence) |
| 17 | Scrubber v1 — the dream | [ ] Not started | — | — | Timeline scrubber moving all 10 players frame by frame + event pins at exact timestamps. Expectation owner-accepted 2026-07-03: minute-frame board states, NOT a smooth replay (bound by Riot data granularity, verified in M15) |

## Decision log & gotchas
- **2026-07-14 — Decision-log dedup.** The M6–M10 shipped/planned prose below the 2026-07-03 entry
  had been duplicated near-verbatim in `docs/roadmap-archive-2026-07.md` since the 2026-07-07 prune
  (copied but never removed). ROADMAP copy deleted; the archive is the sole home for that prose.
  Two ROADMAP-only facts preserved here: the CI attribution (PR #8, `.github/workflows/ci.yml`)
  moved to **Conventions**, and the load-bearing Riot-API-limits bullet kept below.
- **2026-07-03 — Vision scoping (owner-grilled, M11–M17 + U1 queued).** Owner's product vision: an
  analysis workbench that eases post-game review — query recent games, click in, explore lane diffs
  (gold/XP), big stat gaps, trends, all against the game's timeline and map. Decisions: (1) **soul =
  explorer-first** — the app is a self-serve data workbench; auto-callouts are a possible later layer,
  NOT queued. (2) **Completeness bar for daily use = M11+M12+M13** (owner-picked minimal set), then U1
  usage window; map explicitly NOT required for usage. (3) **Scrubber (M17) is the owner's dream
  milestone** — kept on the roadmap, sequenced last because M13/M15/M16 are its foundation (time-axis
  plumbing, map render + coordinate transform, position plumbing). (4) **Data-granularity caveat
  accepted:** participant positions are believed 60s-frame-only (events carry exact ts+coords) —
  M15 verifies against stored blobs; scrubber v1 = board-state stepper + event pins, not smooth replay.
  (5) **DECLINED: speculative perf/bug milestone** before real usage (owner has no observed defects);
  M14 is fed by the U1 log instead. (6) Repo hygiene noted: `.m2shots/.m3shots/.m4shots` show as
  untracked in `git status` despite "scripts committed" notes — reconcile (commit scripts or gitignore)
  during M11.
- **Riot API limits (load-bearing):** league-v4 = current standing only (no historical per-game
  LP — Climb uses snapshot diffs); match-v5 timeline is the SOLE source of per-minute gold/CS +
  ward/objective/death events (powers M7/M8). Verified both research passes.
