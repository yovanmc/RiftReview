# RiftReview — ROADMAP
<!-- roadmap-schema: whitelist-v3 -->

## Definition
Single-user, **local-only, post-game, data-honest** League of Legends self-coach.
C#/.NET 10 WPF (+ WPF-UI), `RiftReview.Core` (no-WPF, testable) + `RiftReview.App`.
SQLite (schema v3). Riot API only. Black-glass + Hextech Gold (#C8AA6E) theme.
**Vision:** the permanent record of a League career and the instrument for reviewing it, eventually with a
coaching layer on top. The workbench comes first. Goals/trend tracking and the coaching layer are far-horizon
stages gated on real usage. Read `NORTHSTAR.md` before planning.
**Status: PARKED.** Planning only, no build. The rows below run in order on unpark: real-game test, key
store, deep-dive load, DB safety, post-game card, then U1. After U1 the log picks between M11-M13, a phone
card and the map/scrubber arc (M15-M17).
**Honesty tripwire:** if those rows ship and real usage still does not happen within U1, friction was never
the blocker. Re-examine the project before building anything else.
Repo: github.com/yovanmc/RiftReview

## Milestones

| # | Title | Status | Ready | Plan | Notes |
|---|-------|--------|-------|------|-------|
| R0 | Real-game test | [ ] | DEFERRED: project parked | — | ~20 min, no code. Run the Debug build against the real DB and review ONE real game (not `--seed-demo`). Note whether the deep-dive clips, hitches on click, and says anything about a remembered game. Can falsify every row below |
| F0 | Friction + key strategy | [ ] | DEFERRED: project parked | — | DPAPI key store + "Paste new key" field. Drop the `appsettings.json`/`AddJsonFile`/UserSecrets dependency. Publish the exe to `C:\Self Apps\RiftReview\`. One-action sync-day key workflow. Riot personal-product key persistence is **UNVERIFIED** |
| B5 | Deep-dive load | [ ] | DEFERRED: project parked | — | Async cancellable deep-dive load, cached baselines, `ScrollViewer`, ranked filter + sample size on the baseline label, a non-imperative tilt banner |
| B3a | DB safety part 1 (HIGH) | [ ] | DEFERRED: project parked | — | WAL + `PRAGMA busy_timeout=5000` on every open. Backup-before-migrate via the SQLite online backup API (**UNVERIFIED** overload in the pinned Microsoft.Data.Sqlite) or `wal_checkpoint(TRUNCATE)` under a `Global\RiftReview.Migrate` mutex, plus `integrity_check` on the copy |
| REC | Post-game card on manual sync | [ ] | DEFERRED: project parked | — | Five tiles each with baseline + sample size, spine chart with static event pins, capture strip, a "what this game does not know" panel. Workbench behind a button |
| U1 | Usage window (~3 weeks) | [ ] | BLOCKED: R0, F0, B5, B3a, REC | — | Real games reviewed after every session. Blockers fixed immediately, everything else logged as the M14 backlog. Tell "capture ran, nothing to review" apart from "capture did not run" if a capture host exists |
| B4 | MCP read surface | [ ] | DEFERRED: runs in parallel with U1 | — | Read-only `get_schema`/`query_data`-style tools with an engine-level ATTACH-deny authorizer copied WITH its RED-first test (Reserve pattern). Logs `tool_calls` from day one |
| B1 | Capture service | [ ] | BLOCKED: U1 shows capture gaps limit reviews | — | Also triggered by a week of games lost to a forgotten sync. Needs B3a, additive `SyncResult.PerMatchOutcomes`, a GUI-held `Global\RiftReview.App` mutex, `capture_runs` crash reconciliation, exit code 3 = 30-min re-fire. Extends `ParticipantFrameDto`/`ParticipantDto` |
| B1s | Scheduled capture variant | [ ] | BLOCKED: B1 | — | Needs a long-lived key, or a consecutive-failure counter that surfaces amber after N failures |
| B3b | DB safety part 2 | [ ] | BLOCKED: B3a | — | Backup rotation gated on off-site verification, NAS archive with SHA256, gap-watch that warns before Riot's match-history retention window (**UNVERIFIED**, verify before building the threshold) |
| 11 | Lane-opponent diff | [ ] | BLOCKED: U1 log | — | Gold **and XP** diff vs the direct lane opponent across the game, in the deep-dive. FIRST task: verify per-participant `xp` + lane-opponent resolution against stored `timeline_json` frames (**UNVERIFIED**) |
| 12 | Head-to-head stat panel | [ ] | BLOCKED: U1 log | — | Per-game scoreboard vs laner: damage dealt, KP, gold earned, damage to towers, vision, CS, deltas highlighted. Data from stored match/timeline JSON only |
| 13 | Timeline explorer | [ ] | BLOCKED: U1 log | — | Pick any metric(s), rendered across game time in the deep-dive. Also builds the time-axis plumbing M17 needs |
| 14 | Usage-fed fixes (perf + bugs) | [ ] | BLOCKED: U1 | — | Backlog = the U1 log. Speculative perf/bug work before U1 is declined |
| 15 | Map foundation | [ ] | BLOCKED: U1 log | — | Rift map render + timeline→map coordinate transform (verify empirically) + death/kill locations with phase filter. MUST verify position-data granularity (believed 60s participant frames + exact-time events) and map-asset source/licensing before M17 is planned |
| 16 | Ward map + position trail | [ ] | BLOCKED: 15 | — | Ward placed/cleared locations. Per-minute own-position trail (roam timing, lane presence) |
| 17 | Scrubber v1 | [ ] | BLOCKED: 13, 15, 16 | — | Timeline scrubber moving all 10 players frame by frame + event pins at exact timestamps. Minute-frame board states, NOT a smooth replay (bound by Riot data granularity) |
| H | Screenshot folder hygiene | [ ] | BACKLOG | — | `.m2shots`-`.m6shots` capture scripts are untracked. Commit them or gitignore the folders |

## Pointers
- Vision and path phases: [NORTHSTAR.md](NORTHSTAR.md) · Commands, conventions, screenshot harness, gotchas and non-goals: [CLAUDE.md](CLAUDE.md)
- Product shape: explorer-first self-serve workbench. Auto-callouts are a possible later layer, not queued
- Riot API limits: league-v4 = current standing only (no historical per-game LP, Climb uses snapshot diffs). match-v5 timeline is the SOLE source of per-minute gold/CS + ward/objective/death events
