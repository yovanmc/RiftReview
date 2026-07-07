# RiftReview — agent/developer runbook

State lives in `ROADMAP.md` (canonical milestones + decision log). This file is the
how-to-work-here layer — read ROADMAP.md for what's being built; read this for how to build it.
**Read `NORTHSTAR.md` before planning anything here** — end-state vision, path phases, and locked owner decisions (North Star session 2026-07-07).

## What this is
Single-user, local-only, post-game, data-honest League of Legends self-coach.
C#/.NET 10 WPF (+ WPF-UI), split into `RiftReview.Core` (no-WPF, testable) and `RiftReview.App`.
SQLite (schema v3). Riot API only — no third-party aggregators. Public repo:
github.com/yovanmc/RiftReview. M1–M10 merged; nothing queued. Riot personal dev keys
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
`.m<N>shots/` per-milestone folders (`.m1shots`…`.m10shots`), each with a `run_capture.ps1`
(some also have `run_capture_tall.ps1`). Pattern:
- Launch the Debug exe with `--seed-demo --page <review|champions|trends|matchups|sessions|climb|settings>`
  (hook lives in `AppShell.OnLoaded`).
- Set `HKCU:\Software\Microsoft\Avalon.Graphics\DisableHWAcceleration=1`, capture, then restore it.
- Capture via `.m2shots/Capturer/out/Capturer.exe` (PrintWindow, `PW_RENDERFULLCONTENT`).
- `DeepDiveView` is embedded in `ReviewView`, not its own nav page — reach it via UIAutomation
  `SelectionItemPattern.Select()` on the first matching ListItem.
- Use the `_tall` variant when the target card sits below the default capture fold (chart/band
  content especially — M8, M10 both needed it).
- **Gate**: a cheap subagent views the PNGs and returns a text verdict — never load PNGs into
  the controller session. PNGs are gitignored (`.m?shots/*.png`; the single-digit glob needed an
  explicit extra entry for `.m10shots`); the capture scripts themselves ARE committed.
- No `--capture`/`--autostart`/`--done-signal` hooks exist here (that's a different project's harness).

## Conventions & safety
- CI: GitHub Actions since 2026-07-02 (`.github/workflows/ci.yml` — restore → build `-warnaserror` → test,
  windows-latest / .NET 10, on push/PR to master). The local merge gate is still `dotnet test` + the screenshot
  subagent verdict. Flow: plan doc in `docs/superpowers/plans/` → branch → PR →
  `--merge --delete-branch` from `master` (default branch is **master**, not main).
- Commit author = repo default `yovanmc`; **never pass `--author`**. End commit messages with
  the current model's `Co-Authored-By: Claude …` trailer.
- Non-goals (enforced, not aspirational): no single composite "RiftScore" — every verdict
  must decompose into named, individually-numbered components; no external/recommended-build
  comparison or live-overlay/draft-scouting data (Data Dragon used only for item names +
  completed-item filter); never fabricate — sparse baselines stay sparse, own-games-only builds
  need ≥3 games or show "not enough games yet."

## Cross-cutting gotchas (from the decision log)
- **Background-thread `[ObservableProperty]` trap (M9):** any VM property set off the UI thread
  after first render MUST be `[ObservableProperty]` on an `ObservableObject` — a plain
  `{get;set;}` never fires INPC, compiles fine, unit tests pass, but the UI silently never
  updates. Only the screenshot gate catches this.
- **Demo seeder must emit matching synthetic events or panels render empty** — recurring across
  M8 (recalls), M9 (item purchases), M10 (team kills for KP): a real DB feature with no demo-data
  analog looks broken in every screenshot even though the logic is correct and unit-tested.
- **On-demand-from-blob pattern:** M7–M10 all computed new metrics on demand from the stored
  `match_detail.timeline_json` blob rather than adding DB columns — no schema migration needed;
  keep following this pattern before reaching for a migration.
- **`.gitignore` PNG glob is single-digit-milestone-specific** (`.m?shots/*.png`) — a new
  double-digit milestone folder needs its own explicit ignore entry.
- **Riot timeline schema fields are optional/open-ended** — treat all event fields as optional
  and enum values as an open set (never throw on an unrecognized one); team invariant is
  1–5→100, 6–10→200.
- **WPF-UI `FluentWindow` title-bar trap (M4):** `ui:TitleBar` must be a first-class top row of
  the window `Grid`, not re-templated — the screenshot gate asserts caption-button presence.
