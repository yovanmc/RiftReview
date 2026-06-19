# RiftReview M3 — Per-Champ Matchup Scouting — Design

**Status:** Approved for planning (2026-06-19).
**Milestone:** M3 of the 5-milestone self-coaching roadmap (M1 deep spine + champ pool ✅, M2 per-champ improvement Trends ✅, M3 = matchup notes — realized here as **auto matchup scouting**; M4 tilt guard; M5 rank/LP view).

## 1. Goal

Answer one question for the owner: **"Which lane matchups am I good or bad at, on each of my champs?"** M3 adds a **Matchups** page that, for a selected champion, lists every enemy laner the owner has faced on that champ and shows the laning + combat performance of each pairing, computed entirely from the games already stored.

This is **data-only** scouting (no freeform text notes — that was explicitly de-scoped) and **self-relative** (your own results vs each opponent). It needs **no new Riot calls, no schema change, no migration, and no backfill** — it is pure read-only analysis over the 408 ranked games already in the DB. That makes M3 a substantially lower-risk milestone than M2.

## 2. Locked design decisions (from brainstorming)

| Decision | Choice |
|---|---|
| Core job | Auto matchup scouting (data), **not** freeform notes |
| Slice / unit | **Per (my champ × enemy laner)** pairing |
| Navigation | **Pick my champ → table of opponents faced on it** (champ-first, like Trends) |
| Row metrics | **Laning + combat**: Opponent · Games · Win% · avg Gold@15 Δ · avg CS@10 · avg Deaths · avg KDA · avg Pre-15 deaths |
| Thin samples | **Show all, sorted by games desc, de-emphasize thin** (muted, no win-rate color) + a page-level min-games filter |
| Drill | **Jump to the full M1 Review deep-dive** for a specific game |
| Layout | **Option C — master–detail split** (opponent list ‖ detail pane with metric tiles + games list) |
| Queue | Ranked only (420/440), matching M1/M2 |

## 3. The matchup unit and metrics

A **matchup** = the set of the owner's **ranked** games on the **selected champion** against a given **enemy laner** (`opponent_champion_id`). The lane opponent is already stored per match: `MatchExtractor.Summarize` resolves it as the same-`teamPosition`, opposite-team participant, so for TOP/MID/ADC it is the lane counterpart, and for JUNGLE/UTILITY it is the role counterpart (enemy jungler/support) — an acceptable "matchup" for those roles.

Per matchup, computed from the stored scalars (no blob parsing at view time):

| Metric | Source column | Aggregate |
|---|---|---|
| Games | (count) | number of ranked games on this champ vs this opponent |
| Win% | `win` | wins / games |
| Gold@15 Δ | `gold_diff_at_15` | mean over **non-null** games (already a vs-lane diff) |
| CS@10 | `cs_at_10` | mean over non-null (your own CS, absolute) |
| Deaths | `deaths` | mean |
| KDA | `kills`,`deaths`,`assists` | mean of per-game `(K+A)/max(1,D)` |
| Pre-15 deaths | `deaths_pre15` | mean over non-null (M2 backfilled these) |

Games with a **null `opponent_champion_id`** (no lane opponent could be attributed — rare in ranked SR, e.g. a role-detection miss) are **excluded** from grouping; they cannot be assigned to a matchup. (This is a data reality to surface, not a bug — see §10.)

## 4. Confidence / thin-sample handling

408 ranked games split across the owner's champ pool × dozens of distinct opponents means many specific pairings are 1–3 games. A "100% vs Yasuo (n=1)" is noise. Rules:

- **Sort:** opponents sorted by **games desc** (most-faced, most statistically meaningful first).
- **Confidence threshold `ConfidenceMinGames = 3` (constant):** rows with `games < 3` render **muted** (reduced opacity, neutral text) and get **no win-rate color** — visible but clearly "small sample, don't over-read."
- **Win-rate color (rows with `games ≥ 3` only):** favorable `win% ≥ 0.55` → win-green; unfavorable `win% ≤ 0.45` → loss-red; in between → neutral/muted.
- **Page-level "min games" filter (default 1 = show all; range 1–10):** raising it **hides** opponents below the chosen count, to declutter. Distinct from the fixed confidence threshold, which only governs muting/coloring.

Nothing is hidden by default; confidence is visually obvious.

## 5. Champion selector

The champ picker lists the owner's champs with **≥ 5 ranked games** (avoids one-off champs cluttering the selector), sorted by games desc, defaulting to the most-played. (The matchup table itself handles thin per-opponent samples, so the selector can be permissive; 5 is a tunable floor set in the plan.)

## 6. Layout — Option C (master–detail split)

A `PanelBgBrush` surface, black-glass + Hextech Gold theme, consistent with M1/M2:

- **Header:** gold "Matchups" title + the **champ selector** (chips or a selector, mirroring the Trends champ chips), and the page-level **min-games filter** control.
- **Left column (~40%):** the **opponent list** — one compact row per opponent faced on the selected champ: opponent name + colored win% + games. Sorted games desc; thin rows muted. Selecting an opponent populates the detail pane (the first/most-faced opponent is selected by default).
- **Right column (~60%):** the **detail pane** for the selected matchup:
  - Heading: "{champ} vs {opponent}" + record (e.g. "2–6 · 25% win") with a one-word favorability read.
  - **Metric tiles:** Gold@15 Δ, CS@10, Deaths, KDA, Pre-15 deaths (the laning+combat set, win% shown in the heading).
  - **Games list:** the individual games behind this matchup — `date · W/L · KDA · gold@15 Δ` per row, sorted newest first. **Clicking a game opens its full deep-dive on the Review page** (see §8).
- **Empty states** per §10.

## 7. Architecture

**Core (`RiftReview.Core`) — pure, no DB/schema/migration changes:**
- `Analysis/MatchupModels.cs` (**new**): `MatchupRow` record — `int OpponentChampionId, int Games, int Wins, double WinRate, double? AvgGoldDiff15, double? AvgCs10, double AvgDeaths, double AvgKda, double? AvgPre15, IReadOnlyList<MatchRow> GamesList`. (Nullable averages are null when every game's source value is null.)
- `Analysis/MatchupCalculator.cs` (**new, pure**): mirrors `ChampTrendCalculator`/`ChampPoolCalculator`.
  - `EligibleChampions(IReadOnlyList<MatchRow> ranked, int minGames=5)` → champ ids with ≥ minGames ranked games, games desc.
  - `Build(IReadOnlyList<MatchRow> champRankedGames)` → `IReadOnlyList<MatchupRow>` grouped by `OpponentChampionId` (skipping null/absent opponents), per-opponent aggregates over non-null values, each `GamesList` ordered newest-first, result sorted games desc.
- No new DB method strictly required — reads the existing `AllMatches(rankedOnly: true)` and filters by champ in the VM. (A thin convenience query may be added if it keeps the VM clean; that is a plan decision.)

**App (`RiftReview.App`):**
- `ViewModels/MatchupsViewModel.cs` (**new**, `partial ObservableObject`): eligible champ list, selected champ, the opponent `MatchupRow` list (wrapped in a display VM), selected opponent + its detail (metric tiles + games list), the min-games filter; reacts to champ + filter changes. Reuses `DataDragonClient.ChampionName` for champ + opponent names.
- `ViewModels/MatchupRowViewModel.cs` + `MatchupGameViewModel.cs` (**new**): thin display wrappers (format the averages by unit, map win% → brush-key + IsThin/IsFavorable/IsUnfavorable, format a game row's date/result/KDA/gold).
- `Views/MatchupsView.xaml(.cs)` (**new**): layout C; DI ctor mirroring `TrendsView`/`ChampPoolView`; `Loaded` → `vm.Initialize()` (ensure Data Dragon, then load). No backfill needed (unlike M2).
- `AppShell.xaml(.cs)`: add a **Matchups** `NavigationViewItem` (Review · Champions · Trends · **Matchups** · Settings) with a valid WPF-UI symbol (e.g. `ui:SymbolIcon Symbol="Sword24"` or `PeopleSwap24` — verify it compiles; fall back to a known-good one); extend the `--page` switch with `"matchups" => typeof(MatchupsView)`.
- `App.xaml.cs`: register `MatchupsViewModel` + `MatchupsView` (transient, matching the other pages).
- `Demo/DemoSeeder.cs`: extend so the demo champ faces **several distinct opponent champions** (varied `opponent_champion_id`) with a spread of results, so the Matchups page renders meaningfully under `--seed-demo`.

## 8. The deep-dive drill (cross-page navigation)

Clicking a game in a matchup's games list must open that specific game in the existing **M1 Review deep-dive** (gold-diff/CS charts, death markers). Feasibility is already established:

- `MainViewModel` is a **singleton** (`App.xaml.cs`), shared by every (transient) `ReviewView` instance, and it owns the `DeepDive` (`DeepDiveViewModel`).
- `DeepDiveViewModel.Load(MatchRow)` renders **any** stored match (it fetches the blobs by `match_id`), independent of the Review rail's recent-20 list.

**Design:** `MainViewModel` gains a small method, e.g. `ShowMatch(string matchId)`, that fetches the `MatchRow` via `GetMatch`, calls `DeepDive.Load(row)`, and — if that match is present in the recent-20 `Matches` list — sets `SelectedMatch` to it so the rail highlights it; otherwise it still loads the deep-dive (the rail simply won't highlight an older off-list game). The `MatchupsViewModel` calls `ShowMatch(...)` and then triggers navigation to `ReviewView`.

The **navigation trigger** (changing the `NavigationView`'s page from a non-Review VM) is the one genuinely new piece of wiring. Two acceptable mechanisms, decided in the plan:
1. A lightweight app-level navigation request (an event/mediator the `AppShell` subscribes to and calls `RootNavigation.Navigate(typeof(ReviewView), null)`), or
2. WPF-UI's navigation service if cleanly registrable alongside the existing manual `SetServiceProvider` setup.

Either is small. **Build-time gate §12.1** confirms the chosen mechanism against the real WPF-UI `NavigationView` API before relying on it.

## 9. Data flow

`AllMatches(rankedOnly: true)` → filter to selected champ → `MatchupCalculator.Build(rows)` → `MatchupRow` list → layout-C opponent list + selected-opponent detail (tiles + games list). Selecting an opponent picks its `MatchupRow`; clicking a game → `MainViewModel.ShowMatch(matchId)` + navigate to Review. Champion + opponent names via `DataDragonClient.ChampionName`.

## 10. Error / empty / low-data states

- **No eligible champs** (none with ≥ 5 ranked games): friendly empty state ("Play more ranked games to scout your matchups").
- **Selected champ, but every opponent below the min-games filter:** show a hint to lower the filter.
- **Opponent with all-null Gold@15/CS@10/Pre-15** (e.g. games that lacked timeline-derived scalars): that tile reads "—" rather than a fabricated 0.
- **Games excluded for null opponent:** if a champ's games largely lack an attributable lane opponent, the table simply shows fewer/zero matchups; not an error. (Surfaced to the owner as a hand-back sanity check.)
- **Deep-dive of an off-list (older) game:** loads correctly; the Review rail just won't highlight a game outside its recent-20.

## 11. Out of scope for M3

- **Freeform text notes** per matchup (explicitly de-scoped — M3 is data-only).
- **External rank/population benchmarks** ("vs a typical Diamond top") — needs a new data source; self-relative only, consistent with M2.
- **Cross-champ aggregation** ("all my games vs Darius regardless of champ") — the unit is your champ × opponent. A possible future toggle.
- **Champion-square icons** on rows — deferred polish, as in M1/M2.
- **Sort controls** beyond default games-desc, and a **win%-ascending "worst lanes first"** view — possible future toggle, not v1.
- Any **schema change, migration, backfill, or Riot API call** — M3 is read-only over existing data.

## 12. Build-time verification gates (confirm before trusting code)

1. **Cross-page navigation.** Confirm the exact mechanism to navigate the `NavigationView` to `ReviewView` from the `MatchupsViewModel` (event-to-`AppShell` calling `RootNavigation.Navigate(typeof(ReviewView), null)`, vs a WPF-UI nav service) against the real WPF-UI 4.3 API. Confirm the singleton `MainViewModel.ShowMatch(matchId)` loads the deep-dive for an **off-list** (older than recent-20) match without error.
2. **Null-opponent reality.** Confirm how many ranked games have a null `opponent_champion_id` (excluded from matchups) so the owner knows the coverage — a read-only count, no code dependency.
3. **WPF-UI symbol** for the Matchups nav item compiles (verify the chosen `SymbolRegular` value exists, as in M2's `DataTrending24`).

## 13. Acceptance criteria

- `dotnet build RiftReview.slnx` clean (0 warnings); `dotnet test RiftReview.slnx` all green (incl. new `MatchupCalculator` + VM tests).
- No schema change, no migration, no backfill, no Riot calls.
- Matchups page lists the owner's champs (≥ 5 ranked games); selecting one shows the opponents faced on it (master–detail layout C) with Games · Win% · Gold@15 Δ · CS@10 · Deaths · KDA · Pre-15, sorted games desc.
- Thin matchups (< 3 games) render muted with no win-rate color; sufficiently-sampled rows are win-rate colored (green favorable / red unfavorable); the min-games filter hides rows below it.
- Selecting an opponent shows its metric tiles + games list; clicking a game opens that game's full deep-dive on the Review page (incl. games older than the recent-20).
- Account/secret safety unchanged; `appsettings.json` placeholders only.

## 14. Hand-back (live verification — owner runs locally)

- Sanity-check matchup numbers on real data: do the win% / gold@15 reads for a known lane (e.g. a hard matchup on K'Sante) match the owner's felt experience?
- Confirm coverage: how many ranked games (if any) are excluded for a null lane opponent, and whether that meaningfully thins any champ's matchup table.
- Confirm the deep-dive drill opens the correct game — including an older game outside the Review recent-20 list.
