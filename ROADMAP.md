# RiftReview — ROADMAP

> Source of truth for what to build next. Follows the `/roadmap` workflow
> (Opus plans/researches · Sonnet subagents implement · ping at every phase handoff).

**Legend:** ✅ Merged · 📝 Plan ready (execute next) · 🔬 Researching/Planning · [ ] Not started (plan first)

## Definition
Single-user, **local-only, post-game, data-honest** League of Legends self-coach.
C#/.NET 10 WPF (+ WPF-UI), `RiftReview.Core` (no-WPF, testable) + `RiftReview.App`.
SQLite (schema v3). Riot API only. Black-glass + Hextech Gold (#C8AA6E) theme.
Repo: github.com/yovanmc/RiftReview · Specs: `docs/superpowers/specs/` · Plans: `docs/superpowers/plans/`

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
| 6 | You-vs-rank on graphs | 📝 Plan ready | `docs/superpowers/plans/2026-06-20-riftreview-m6.md` | — | **execute next** — rank baseline on charts + sparklines + Rank⇄Own toggle + measured-baseline methodology (research items 1–9) |
| 7 | Vision + objectives | [ ] Not started | — | — | wards placed/cleared + vision-score proxy + objective participation from timeline events already fetched (items 10–12); confirm timeline ward/objective events are stored first |
| 8 | Timeline causality | [ ] Not started | — | — | "where the game turned" swing marker + game-state-at-death context + back-timing/item-spike lag (items 13–15) |
| 9 | Build analysis + discipline | [ ] Not started | — | — | own-best-build per champ (item 17 only — NO external build data) + number-under-every-verdict audit + no-composite-score non-goal + optional timeline mini-score (items 16–20) |

## Decision log & gotchas
- **2026-06-20 — Post-v1 expansion scoped from competitive research** (op.gg/u.gg/lolalytics/
  Mobalytics/Porofessor; two deep-research passes). 20 actionable items → milestones M6–M9 by
  cluster. Headline owner ask: "you vs your rank" stat comparisons rendered ON the graphs.
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
