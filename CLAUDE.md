# RiftReview — agent/developer runbook

State lives in `ROADMAP.md` (canonical milestones). This file is the
how-to-work-here layer — read ROADMAP.md for what's being built; read this for how to build it.
**Read `NORTHSTAR.md` before planning anything here** — end-state vision, path phases, and locked owner decisions.

## What this is
Single-user, local-only, post-game, data-honest League of Legends self-coach.
C#/.NET 10 WPF (+ WPF-UI), split into `RiftReview.Core` (no-WPF, testable) and `RiftReview.App`.
SQLite (schema v3). Riot API only — no third-party aggregators. Public repo:
github.com/yovanmc/RiftReview. Riot personal dev keys
**expire ~daily** — real-key testing only works on the owner's machine; all agent work
uses `--seed-demo` synthetic data instead.

## Commands
```powershell
dotnet build RiftReview.slnx -v minimal
dotnet test RiftReview.slnx
dotnet run --project src/RiftReview.App -- --seed-demo   # demo mode, no key needed
```
Secrets (owner-only, never agent-set): User Secrets in dev —
`dotnet user-secrets set "Riot:ApiKey" "RGAPI-..."` — `appsettings.json` ships placeholders
(`"SET-VIA-USER-SECRETS"`) only; never commit a real `RGAPI-` key.

## Screenshot verification harness
`.m<N>shots/` per-milestone folders. `.m7shots` to `.m10shots` hold the committed capture scripts
(`run_capture.ps1`, some with `run_capture_tall.ps1`). `.m2shots` to `.m6shots` and the Capturer
build are untracked, main checkout only. Pattern:
- Launch the Debug exe with `--seed-demo --page <review|champions|trends|matchups|sessions|climb|settings>`
  (hook lives in `AppShell.OnLoaded`).
- Set `HKCU:\Software\Microsoft\Avalon.Graphics\DisableHWAcceleration=1`, capture, then restore it.
- Capture via `.m2shots/Capturer/out/Capturer.exe` (PrintWindow, `PW_RENDERFULLCONTENT`).
- `DeepDiveView` is embedded in `ReviewView`, not its own nav page — reach it via UIAutomation
  `SelectionItemPattern.Select()` on the first matching ListItem.
- Use the `_tall` variant when the target card sits below the default capture fold (chart/band
  content especially).
- **Gate**: a cheap subagent views the PNGs and returns a text verdict — never load PNGs into
  the controller session. PNGs are gitignored; the capture scripts ARE committed.
- No `--capture`/`--autostart`/`--done-signal` hooks here (those belong to another project).

## Conventions & safety
- CI: GitHub Actions (`.github/workflows/ci.yml`: restore → build `-warnaserror` → test,
  windows-latest / .NET 10, on push/PR to master). The local merge gate is `dotnet test` + the
  screenshot subagent verdict. Flow: plan → branch → PR → `--merge --delete-branch` from
  `master` (default branch is **master**, not main).
- Commit author = repo default `yovanmc`; **never pass `--author`**. End commit messages with
  the current model's `Co-Authored-By: Claude …` trailer.
- Non-goals (enforced, not aspirational): no single composite "RiftScore" — every verdict
  must decompose into named, individually-numbered components; no external/recommended-build
  comparison or live-overlay/draft-scouting data (Data Dragon used only for item names +
  completed-item filter); never fabricate — sparse baselines stay sparse, own-games-only builds
  need ≥3 games or show "not enough games yet."

## Cross-cutting gotchas
- **Background-thread `[ObservableProperty]` trap:** any VM property set off the UI thread
  after first render MUST be `[ObservableProperty]` on an `ObservableObject` — a plain
  `{get;set;}` never fires INPC, compiles fine, unit tests pass, but the UI silently never
  updates. Only the screenshot gate catches this.
- **Demo seeder must emit matching synthetic events or panels render empty** (recalls, item
  purchases, team kills for KP): a feature with no demo-data analog looks broken in every
  screenshot even when the logic is correct and unit-tested.
- **On-demand-from-blob pattern:** deep-dive metrics are computed from the stored
  `match_detail.timeline_json` blob rather than new DB columns, so no migration. Prefer this
  pattern before reaching for a migration.
- **`.gitignore` PNG glob is single-digit-milestone-specific** (`.m?shots/*.png`) — a new
  double-digit milestone folder needs its own explicit ignore entry.
- **Riot timeline schema fields are optional/open-ended** — treat all event fields as optional
  and enum values as an open set (never throw on an unrecognized one); team invariant is
  1–5→100, 6–10→200.
- **WPF-UI `FluentWindow` title-bar trap:** `ui:TitleBar` must be a first-class top row of
  the window `Grid`, not re-templated — the screenshot gate asserts caption-button presence.
