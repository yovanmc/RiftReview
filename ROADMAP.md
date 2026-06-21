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

## Non-goals
- **No single composite "RiftScore":** RiftReview will never reduce a player or game to one opaque score. The decomposed, per-metric, number-under-every-verdict approach is the architecture; any future score-like surface must decompose into named, individually-numbered components.
- **No external/recommended-build or live-overlay/draft-scouting data:** item 16 (recommended-build comparison against aggregators) and any live-game overlay or draft-scouting integration are permanently out of scope. Data Dragon is used only for item names + the completed-item filter; all win-rate signal is own-games-only.
- **Never fabricate:** sparse baselines stay sparse (absent cell → no number, not a made-up default); own-games-only builds require ≥3 games or show "not enough games yet."

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
| 8 | Timeline causality | ✅ Merged | `docs/superpowers/plans/2026-06-20-riftreview-m8.md` | #3 | "where the game turned" team-gold-diff swing marker (+ translucent band on chart) + game-state-at-death context + back-timing cadence (recall clusters; NO external item data) + turning-point lag; on-demand from timeline blob (NO migration); EventDto +2 fields. Suite 133. |
| 9 | Build analysis + discipline | ✅ Merged | `docs/superpowers/plans/2026-06-20-riftreview-m9.md` | #4 | own-best-build per champ (item 17, own-games-only; Data Dragon item.json supplies names + completed-item filter) on Champions practicing cards + number-under-every-verdict audit (Session-Health decay reasons get deltas) + no-composite-score non-goal enshrined. Timeline mini-score (item 20) DEFERRED. On-demand from timeline_json (NO migration). Suite 155. |
| 10 | By game phase (timeline mini-score) | 📝 Plan ready | `docs/superpowers/plans/2026-06-20-riftreview-m10.md` | — | item 20 scoped + planned (autonomous run): deep-dive "By game phase" card — Early[0,10)/Mid[10,20)/Late[20,end] × {gold-diff Δ, CS/min, deaths(+behind), kill participation}, each a raw number + signed delta vs own same-role average; never-fabricate (phase not reached → absent; <3 prior games → no badge). KP = new CHAMPION_KILL extraction. Pure `BuildPhaseBreakdown` + `PhaseBaselineCalculator` piggyback the existing 20-game baseline loop (zero extra timeline I/O); NO schema change. Layout A (phase rows). Spec: `docs/superpowers/specs/2026-06-20-riftreview-m10-phase-breakdown-design.md`. |

## Decision log & gotchas
- **2026-06-20 — M9 shipped (PR #4, all plan tasks; autonomous run).** Champions page gained a **Best
  build** panel on the "Currently Practicing" cards: per champ (dominant role), the completed items you
  build most often in YOUR games, each with its own `N games · X% WR` caption. Suite **155** (Core 131,
  App 24). Computed **on demand** from `timeline_json` — **no migration**.
  - **Build math is PURE + unit-tested** (no DB/network): `BuildExtractor.CompletedItemsPurchased`
    (flatten frames→events, `ITEM_PURCHASED` for my pid, keep ids in the completed-set, first-purchase
    order, deduped) + `BuildExtractor.MyParticipantId` + `BuildAnalyzer.Analyze` (per-item games/wins/
    winRate, order Games↓ then WinRate↓ then ItemId↑, topN=6). App-layer `ChampPoolViewModel.BuildFor`
    does the I/O (load each match's timeline, resolve me, extract, aggregate, map ids→names).
  - **"Me" resolved from `TimelineMetadata.Participants`** (ordered PUUID list, index+1 = participantId;
    guaranteed present) with the new `TimelineInfo.Participants` as fallback. puuid source =
    `_db.GetMeta("puuid")` (same as `DeepDiveViewModel`).
  - **Item metadata = Data Dragon item.json** fetched in `DataDragonClient.EnsureLoadedAsync` (disk-cached
    per version, mirrors champion.json; wrapped in its OWN try/catch so an item.json failure can't break
    champion names). `ItemCatalogParser.Parse` is pure + tested. Predicate validated 16.12.1 (706→115).
    `HasItemData=false` (offline, no cache) → panel shows "item data unavailable"; `<3 games` → "not
    enough games". Names only — NO recommended-build/external/gold-valuation data.
  - **GOTCHA (cost a silent-invisible-panel bug, caught by the screenshot gate, fix `e937c27`):**
    `ChampCardViewModel.BestBuild` is set on a background thread AFTER first render, so it MUST be an
    `[ObservableProperty]` on an `ObservableObject` — a plain `{get;set;}` never fires INPC and the
    `NullToCollapsed` binding leaves the panel permanently collapsed. Compiles + unit-tests pass either
    way; only the screenshot reveals it. (App-layer `BuildFor` integration is still untested — Core
    coverage offsets; add an async-VM test when the App harness supports it.)
  - **Demo seeder:** Ahri games now `AddBack(22, {6655,3157,3089|6655,3157})` (Luden's/Zhonya's in all,
    Rabadon's in every other game → non-constant WR column). Player puuid `"ME"` sits at
    `Metadata.Participants[2]` (pid 3), matching the pid the purchases are emitted under. M8 recall
    cadence untouched. `.m9shots` clones `.m8shots` (page=champions, default capture — panel is top-of-
    page; no list-selection/tall variant). PNGs gitignored (`.m?shots/*.png`); script committed.
  - **Number-under-every-verdict (item 18):** only gap was Session Health decay reasons — now
    `deaths climbing (+1.8/game)` / `CS@10 falling (-9)` / `KDA falling (-1.6)` (thresholds/severity
    unchanged). Audit table + **no-composite-score** (item 19) in
    `docs/superpowers/specs/2026-06-20-riftreview-verdict-audit.md` + ROADMAP **## Non-goals**.
- **2026-06-20 — M9 planned (autonomous run).** Plan:
  `docs/superpowers/plans/2026-06-20-riftreview-m9.md`. Owner decisions: (1) **fetch Data Dragon
  item.json** — Riot's OWN static dict (same source as champion names), NOT a 3rd-party build
  aggregator, so it honors data-honest/local-only. Supplies item **names** + the **completed-item
  filter** only; the build win-rate signal is **100% own games**. (2) **Placement = per-champ aggregate
  on the Champions page**, MVP rendered on the **"Currently Practicing"** cards (≤3 champs → bounded
  timeline I/O, async off-UI-thread). (3) **Item 20 (timeline mini-score) DEFERRED** to a later
  milestone. (4) Item 16 (external recommended-build) stays a documented **non-goal**.
  - **Build source = ITEM_PURCHASED stream only** (no final-inventory data is persisted —
    `ParticipantDto` has no `item0..6`; `match_detail` is opaque JSON). Computed **on demand** from
    `timeline_json` → **no schema migration** (M7/M8 pattern).
  - **Completed-item predicate** (web-validated against live item.json 16.12.1, 706 items → **115**
    kept; STRUCTURAL not version-pinned): `idStr.Length ≤ 4 && maps["11"]==true && gold.total ≥ 2000 &&
    !tags∋(Consumable|Trinket|Boots) && (into absent/empty)`. **Do NOT gate on `gold.purchasable`** —
    transform results (Muramana/Seraph's/Fimbulwinter) are `purchasable:false`; their precursor is the
    terminal ≥2000 item we want. Transform items appear in the purchase stream as their **precursor id**
    (e.g. "Manamune") — shown as-purchased by design (honest: sole source is what was bought).
  - **`info.participants` modeled on `TimelineDto`** (additive/nullable) to resolve "me" (puuid →
    participantId) from the timeline alone, avoiding a `match_json` load per game. Fallback =
    `MatchExtractor.Summarize` idiom if absent.
  - **Number-under-every-verdict audit:** the codebase is already number-backed everywhere (Trends,
    Matchups, Swing, Vision) EXCEPT Session Health **decay reasons** ("deaths climbing"/"CS@10
    falling"/"KDA falling") — the only fix. Deltas already exist on `PlaySession`; M9 appends them to the
    reason strings. Audit table + **no-composite-score** non-goal enshrined in
    `docs/superpowers/specs/2026-06-20-riftreview-verdict-audit.md` + a ROADMAP **Non-goals** section.
  - **Demo seeder** must emit completed-item `ITEM_PURCHASED` events for a practicing champ (≥3 games,
    real ids e.g. 6655/3157/3089, one varied for non-constant WR) or the build panel renders empty —
    same class of gotcha as M8's synthetic recalls. `.m9shots` clones `.m8shots` (page=champions; build
    panel is top-of-page → default capture, no list-selection/tall variant).
- **2026-06-20 — M8 shipped (PR #3, all plan tasks + the risk-gated chart band).** Deep-dive gained a
  **"Swing & causality"** card at the TOP (above Vision, so PrintWindow captures it). Suite **133**
  (Core 109 incl. 7 new causality tests, App 24). Computed **on demand** from `timeline_json` — **no
  migration**. `EventDto` +2 nullable fields (`ParticipantId`, `ItemId`) appended at end (additive).
  - `TimelineExtractor.BuildCausality(tl, myParticipantId)` → `CausalityResult(SwingPoint?, Deaths,
    Backs, TurningPointLagMinutes?)`. New private `TeamGoldDiffSeries` mirrors `BuildDeepDive`'s
    team-gold formula + skip-rule so the swing band aligns with the displayed chart; **`BuildDeepDive`
    left untouched** (exact-value tested).
  - **Swing tie-break = earliest.** On a perfectly LINEAR gold curve every 3-min window ties, so the
    swing lands at 0:00→3:00 — which is exactly what the `--seed-demo` data does (its gold curve is
    linear). Real non-linear games land it on the steepest section (proven by the unit fixture: swing
    at min 5→8, Δ +2000). Did NOT change the demo curve (it feeds M6/M7 baselines/visuals).
  - **Back-timing has NO item metadata** by design (honors M6 drop-external-build-data): a back = an
    `ITEM_PURCHASED` cluster (>10s gap → new back); we report minute + item count only. Demo seeder
    now emits synthetic recalls (player pid=3; backs at min 2/8/14/20) or it would render empty.
  - `LineChart` gained nullable `SwingStartMinute`/`SwingEndMinute` DPs (band drawn behind series via
    the existing `ChartScaler.X`; no-op when null). Bound on the gold-diff chart only.
  - **`.m8shots`** harness clones `.m7shots` (review page, UIAutomation select-first-match,
    DisableHWAcceleration set/restore, `.m2shots/Capturer/out/Capturer.exe`). Gold-diff chart sits
    BELOW the default-window capture fold — use the **tall variant** (`run_capture_tall.ps1`,
    `TransformPattern.Resize` to ~1120×1500) to frame the chart/band. PNGs gitignored; scripts committed.
- **2026-06-20 — M8 planned (autonomous run).** Plan:
  `docs/superpowers/plans/2026-06-20-riftreview-m8.md`. Three timeline-only items, **no external data**,
  **no schema migration** (on-demand from `timeline_json`, M7 pattern).
  - **Swing (item 13) = TEAM gold-diff**, not lane (macro "where the game turned" vs micro 1v1).
    Largest-magnitude change over a **rolling 3-frame (~3 min) window**, tie-break earliest; `null`
    (→ UI "No decisive swing") if <2 frames or |Δ| < 1g. Positive team diff = my team ahead (confirmed
    `myTeamGold − enemyTeamGold` convention in `BuildDeepDive`).
  - **Death context (item 14)** = team gold-diff at each of my deaths (reuses death scan; nearest team
    series point). UI tags ahead/behind by sign only (no judgment).
  - **Back-timing (item 15) = recall CADENCE, no item metadata.** Honors M6's drop-external-build-data
    decision: a "back" = a cluster of my `ITEM_PURCHASED` events (gap > 10s → new back); report minute +
    item count only. NO Data Dragon / item names / gold values / "legendary completion." Turning-point
    lag = min from swing start back to the preceding recall.
  - **`EventDto` +2 nullable fields** (`ParticipantId`, `ItemId`) appended at END (additive, like M7's
    10 fields; existing constructions/blobs unaffected). `ITEM_PURCHASED` uses `participantId` (NOT
    killerId). Demo seeder must emit synthetic recalls or back-timing renders empty.
  - **Surgical:** `BuildDeepDive` (exact-value tested) left untouched; new `BuildCausality` + a private
    `TeamGoldDiffSeries` helper (identical formula/skip-rule, so the swing aligns with the displayed
    chart). Chart swing-band is a **risk-gated secondary task** (STOP if `LineChart` render math is
    unclear) — the text card is the primary deliverable.
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
