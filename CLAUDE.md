# RiftReview runbook

State lives in `ROADMAP.md` (canonical milestones). This file is the how-to-work-here layer.
Read `NORTHSTAR.md` when planning or judging scope: end-state vision, path phases and locked decisions.
Read [docs/MAP.md](docs/MAP.md) when you need to locate code you have not seen.
Working files go in `.scratch/`. `docs/` holds only the maintained docs listed in docs/MAP.md.

## What this is
Single-user, local-only, post-game, data-honest League of Legends self-coach.
C#/.NET 10 WPF (+ WPF-UI), split into `RiftReview.Core` (no-WPF, testable) and `RiftReview.App`.
SQLite. Riot API only, no third-party aggregators. Public repo github.com/yovanmc/RiftReview.
Riot personal dev keys **expire about daily**, so real-key testing works only on Yovan's machine.
All agent work uses `--seed-demo` synthetic data.

## Commands
```powershell
dotnet build RiftReview.slnx -v minimal
dotnet test RiftReview.slnx
dotnet run --project src/RiftReview.App -- --seed-demo   # demo mode, no key needed
```
Yovan sets secrets via User Secrets (README, Setup), never an agent. `appsettings.json` ships
`"SET-VIA-USER-SECRETS"` placeholders only. Commits carry placeholders, never a real `RGAPI-` key.

## Screenshot verification
Every merge needs a screenshot verdict from `--seed-demo` captures. Harness steps (launch flags,
software render, capture scripts, the `DeepDiveView` drill, `_tall` variants):
[docs/SCREENSHOTS.md](docs/SCREENSHOTS.md).

## Conventions & safety

Test gate: `.github/workflows/ci.yml` · whole · ci · 1.5 min [V 2026-09-28 6ffe5f91]

- CI (`.github/workflows/ci.yml`): restore, build `-warnaserror`, test on windows-latest / .NET 10,
  on push and PR to `master`. Merge only after CI is green and the screenshot verdict is in. Default branch is **master**.
- Non-goals (enforced, not aspirational): no single composite "RiftScore", so every verdict
  decomposes into named, individually-numbered components. No external or recommended-build
  comparison or live-overlay/draft-scouting data (Data Dragon supplies only item names and the
  completed-item filter). Never fabricate: sparse baselines stay sparse, and own-games-only builds
  need ≥3 games or show "not enough games yet."

## Cross-cutting gotchas
- **Background-thread `[ObservableProperty]` trap:** any VM property set off the UI thread
  after first render MUST be `[ObservableProperty]` on an `ObservableObject`. A plain
  `{get;set;}` never fires INPC, compiles fine and passes unit tests, but the UI silently never
  updates. Only the screenshot gate catches this.
- **Demo seeder must emit matching synthetic events or panels render empty** (recalls, item
  purchases, team kills for KP). A feature with no demo-data analog looks broken in every
  screenshot even when the logic is correct and unit-tested.
- **On-demand-from-blob pattern:** deep-dive metrics are computed from the stored
  `match_detail.timeline_json` blob rather than new DB columns, so no migration. Prefer this
  pattern before reaching for a migration.
- **Riot timeline schema fields are optional and open-ended.** Treat all event fields as optional
  and enum values as an open set (accept an unrecognized one without throwing). Team invariant:
  participants 1 to 5 are team 100, 6 to 10 are team 200.
- **WPF-UI `FluentWindow` title-bar trap:** `ui:TitleBar` must be a first-class top row of
  the window `Grid`, not re-templated. The screenshot gate asserts caption-button presence.
