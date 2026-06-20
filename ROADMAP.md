# RiftReview — ROADMAP

> Source of truth for what to build next. Follows the `/roadmap` workflow
> (Opus plans/researches · Sonnet subagents implement · ping at every phase handoff).

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

## Milestones
| # | Title | Status | Plan | PR | Notes |
|---|-------|--------|------|----|-------|
| 1 | Review shell + nav | ✅ Merged | — | — | NavigationView shell; per-match deep-dive (gold-diff + CS-pace-vs-baseline + death markers) |
| 2 | Trends | ✅ Merged | `docs/superpowers/plans/2026-06-19-riftreview-m2.md` | — | per-champ trailing-window trajectory across 8 metrics + verdicts; Sparkline (double-precision) |
| 3 | Matchups | ✅ Merged | `docs/superpowers/plans/2026-06-19-riftreview-m3.md` | — | per-champ vs each enemy-laner aggregates; drill into deep-dive |
| 4 | Session Health | ✅ Merged | `docs/superpowers/plans/2026-06-19-riftreview-m4.md` | — | sessions + loss-streak/decay tilt guard; cross-page banner (singleton VM) |
| 5 | Climb | ✅ Merged | `docs/superpowers/plans/2026-06-19-riftreview-m5.md` | — | current rank + ranked streaks + net-LP-per-snapshot segments; RankLadder |
| 6 | You-vs-rank on graphs | ✅ Merged | `docs/superpowers/plans/2026-06-20-riftreview-m6.md` | #1 | rank baseline on Trends sparklines + deep-dive CS-pace chart; Rank⇄Own toggle + per-tier selector + signed delta badges + provenance; sparse never-fabricate seed table |
| 7 | Vision + objectives | ✅ Merged | `docs/superpowers/plans/2026-06-20-riftreview-m7.md` | #2 | deep-dive Vision & objectives section: exact wards placed/cleared/control + labeled vision proxy + objective participation (dragons/herald/baron/towers/inhibitors, Void Grubs if present); on-demand from stored timeline blob (NO schema migration). Suite 126. |
| 8 | Timeline causality | [ ] Not started | — | — | "where the game turned" swing marker + game-state-at-death context + back-timing/item-spike lag (items 13–15) |
| 9 | Build analysis + discipline | [ ] Not started | — | — | own-best-build per champ (item 17 only — NO external build data) + number-under-every-verdict audit + no-composite-score non-goal + optional timeline mini-score (items 16–20) |

## Decision log & gotchas
- **2026-06-20 — M7 shipped (PR #2, all plan tasks).** Deep-dive **Vision & objectives**
  section (compact, placed at the TOP of the deep-dive under the header so PrintWindow
  captures it without scrolling). Computed **on demand** from the stored `timeline_json`
  blob — **no DB schema migration**. Suite **126** (Core 102, App 24).
  - `EventDto` extended with 10 nullable fields (defaults preserve existing positional
    constructions). `TimelineExtractor.BuildVisionObjectives(tl, myParticipantId, myTeamId)`
    does all the work. `MatchSummary` gained `MyTeamId` (from `me.TeamId`).
  - **Vision numbers are exact; only the composite is a proxy.** Vision proxy =
    placed + cleared + control (control counts twice); UI caption labels it an estimate,
    NOT Riot's Vision Score (Riot's real visionScore is a *match*-participant field, absent
    from the timeline). Exact ward counts shown alongside.
  - **Objective crediting:** elites use `killerTeamId` (fallback `TeamOfNullable(killerId)`);
    buildings use `teamId` = team that LOST the building, so my team destroyed it iff
    `teamId != myTeamId` — my own lost buildings are correctly excluded. Team total 0 →
    UI shows **"none taken"** (never a fabricated 0%).
  - **Gotcha:** `TimelineExtractor` already had a non-nullable `TeamOf(int)` (used by
    BuildDeepDive for team-gold). The nullable variant is `TeamOfNullable(int?)` (returns
    null for participantId 0/null = minion/structure) to avoid overload ambiguity.
  - **No CI on this repo** (no `.github/workflows`) — merge gate = local `dotnet test` +
    the `.m?shots` screenshot subagent verdict, same as M6. `.m7shots/run_capture.ps1`
    reuses the M6 harness (review page, UIAutomation `SelectionItemPattern.Select()` first
    match, DisableHWAcceleration set/restore, `.m2shots/Capturer/out/Capturer.exe`). PNGs
    gitignored via `.m?shots/*.png` (harness scripts ARE committed).
  - **Foundational for M8** (timeline causality): reuses the extended `EventDto` + the
    frame/event flattening idiom.
- **2026-06-20 — M7 planned (gate cleared).** Recon confirmed match-v5 ward/objective
  events ARE persisted: the **full raw timeline JSON** is stored in `match_detail.timeline_json`
  since M1 and is immutably re-parseable (proven by `DerivedMetricsBackfill`). The current
  `EventDto` only deserialized `Type/Timestamp/KillerId/VictimId`; M7 extends it (nullable
  fields) + adds `TimelineExtractor.BuildVisionObjectives`. **No schema migration** — metrics
  computed on demand from the blob in the deep-dive (like death markers).
  - **Riot match-v5 timeline event schema (web-verified, cited):** `WARD_PLACED.creatorId`
    (placer — NOT participantId) + `wardType`; `WARD_KILL.killerId` + `wardType`;
    `ELITE_MONSTER_KILL` has `killerId`/`killerTeamId`/`monsterType`(DRAGON/RIFTHERALD/
    BARON_NASHOR/HORDE)/`monsterSubType`(dragons)/`assistingParticipantIds`;
    `BUILDING_KILL` has `killerId`(may be 0)/`teamId`(=team that LOST the building)/
    `buildingType`/`towerType`/`laneType`/`assistingParticipantIds` and **no `killerTeamId`**.
    Team invariant 1-5→100, 6-10→200. Treat all fields as optional; enum values open (never throw).
  - **Vision "proxy" is intentional & honest:** Riot's real `visionScore` is a *match*-participant
    field, NOT in the timeline — so a timeline-only vision composite must be a labeled estimate.
    Ward COUNTS (placed/cleared/control) are exact; only the composite is a proxy. Proxy =
    placed + cleared + control (control counts twice); UI labels it "not Riot's Vision Score" and
    shows exact counts alongside. Objective participation (my takedowns ÷ my team's total) is exact.
  - Added `MyTeamId` to `MatchSummary` (from `me.TeamId`) to determine "my team" for objective crediting.
- **2026-06-20 — Post-v1 expansion scoped from competitive research** (op.gg/u.gg/lolalytics/
  Mobalytics/Porofessor; two deep-research passes). 20 actionable items → milestones M6–M9 by
  cluster. Headline owner ask: "you vs your rank" stat comparisons rendered ON the graphs.
  **Full learnings + cited findings + 20-item list + non-goals dossier:**
  `docs/research/2026-06-20-league-analytics-competitive-research.md`.
- **M6 owner decisions:** (1) rank baseline data = **HYBRID** — embedded patch-tagged,
  source-labeled, owner-editable per-rank seed table (primary) + own-trailing-average toggle
  (rigorous fallback). (2) Build analysis = **item 17 only** (own-best-build); item 16 (external
  recommended-build comparison) **dropped** to protect the local-only/data-honest identity.
- **Data-honesty guardrails (M6):** seed table is **sparse + never-fabricate** — absent
  (metric, role, tier) cell → own-trailing only, no made-up number. Support CS@10 intentionally
  null. Player overall WR keeps a **50% anchor** (matchmaking equilibrium); lolalytics' 51.77% is
  a *champion* baseline, not a player one — do NOT apply it to climb WR.
- **Riot API limits (load-bearing):** league-v4 = current standing only (no historical per-game
  LP — Climb uses snapshot diffs); match-v5 timeline is the SOLE source of per-minute gold/CS +
  ward/objective/death events (powers M7/M8). Verified both research passes.
- **Existing baseline is already measured:** `DeepDiveViewModel.BuildBaseline` averages the
  player's own prior same-role/same-champ games (not hardcoded) → M6 item-7 "audit" already
  satisfied; M6 adds the *rank* baseline alongside it.
- **Sparkline caveat:** the M2 "double-series" support is double-*precision* (one line only) —
  M6 Task 4 adds the actual second (baseline) line to the control.
- **WPF-UI FluentWindow title-bar trap** (M4→fix, PR 9e18569): `ui:TitleBar` must be a
  first-class top row of the window Grid; the M6 screenshot gate asserts caption-button presence.
- **2026-06-20 — M6 shipped (PR #1, all 10 plan tasks).** Rank baseline now renders on Trends
  sparklines (dashed gray line + signed delta badge) and on the deep-dive CS-pace chart (blue
  dashed "rank avg" line beside gold=you + gray dashed=own-trailing). Rank⇄Own toggle, per-tier
  selector, and provenance label live. Suite 123/123 (Core 99, App 24).
  - **Never-fabricate is structural:** absent `(role,tier,metric)` cell → `RankBaselineProvider.Resolve`
    returns null → `HasBaseline=false` → no line/badge/number. The embedded seed
    `src/RiftReview.Core/Data/rank-baselines.json` is SPARSE — only `cs10`/`csPerMin` for laners
    (TOP/MID/ADC/JUNGLE); SUPPORT and every non-CS metric (winRate/gold15/kda/deaths/pre15/kp/dmg)
    have no rank cell, so in Rank mode only **CS @ 10** shows a rank delta. Owner extends the JSON
    (embedded resource) to seed more metrics later.
  - **No Core record change was needed:** `MetricTrend` already carried `Current`/`Direction`/`Unit`/
    `RollingSeries`, so the plan's flagged "may need to add Dir/Unit/CurrentValue" risk didn't fire.
  - **Sparkline.OnRender scaling vars are `w`/`h`/`min`/`range`** (range = max-min clamped ≥1e-6);
    the baseline line reuses them so it lines up with the series.
  - **M6 screenshot harness = `.m6shots/`** (reusable for M7/M8): launch Debug exe with
    `--seed-demo --page trends` (the `--page <review|champions|trends|matchups|sessions|climb|settings>`
    hook is read in `AppShell.OnLoaded`), set `HKCU:\Software\Microsoft\Avalon.Graphics\DisableHWAcceleration=1`
    (restore after), capture via `.m2shots/Capturer/out/Capturer.exe` (PrintWindow PW_RENDERFULLCONTENT).
    **DeepDiveView is embedded in ReviewView (not a nav page)** — reach it via UIAutomation
    `SelectionItemPattern.Select()` on the first match ListItem. No `--capture`/`--autostart`/`--done-signal`
    hooks exist (those are VideoTriage-only).
