# RiftReview M3 — Per-Champ Matchup Scouting — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a per-champion "Matchups" page that, for a selected champ, lists every enemy laner the owner has faced on it with laning + combat aggregates, and lets a click on any game open that game's full Review deep-dive.

**Architecture:** A pure `MatchupCalculator` groups the owner's existing ranked `MatchRow`s for a champ by `opponent_champion_id` and computes per-opponent aggregates — **no schema change, no migration, no backfill, no Riot calls**. A new `MatchupsViewModel` + `MatchupsView` render layout C (opponent list ‖ detail pane with metric tiles + games list). Clicking a game routes through the **singleton** `MainViewModel.ShowMatch(matchId)` + a tiny `NavigationService` to open the existing M1 Review deep-dive. Mirrors the `ChampTrendCalculator`/`TrendsViewModel` patterns from M2.

**Tech Stack:** C#/.NET 10, WPF + WPF-UI 4.3, Microsoft.Data.Sqlite, CommunityToolkit.Mvvm, xUnit.

**Design spec:** `docs/superpowers/specs/2026-06-19-riftreview-m3-design.md`.

---

## Build-time verification gates (confirm BEFORE trusting representative code)

1. **Cross-page navigation + off-list deep-dive.** `MainViewModel` is a **singleton** (`App.xaml.cs` — `s.AddSingleton<ViewModels.MainViewModel>()`); every transient `ReviewView` binds the same instance. `DeepDiveViewModel.Load(MatchRow)` renders **any** stored match (fetches blobs by `match_id`), independent of the Review rail's recent-20. The navigation trigger from the Matchups VM is a tiny event-based `NavigationService` the `AppShell` subscribes to and forwards to `RootNavigation.Navigate(typeof(ReviewView), null)`. Confirm `RootNavigation.Navigate(Type, object?)` is the real WPF-UI 4.3 API (it is already used in `AppShell.xaml.cs`).
2. **`_namesReady` guard.** A second navigation to Review must NOT clobber a deep-dive set by `ShowMatch`. The guard makes `MainViewModel.InitializeAsync()` skip its `Reload()` once Data Dragon names have loaded (the default landing page is Review, so names are already loaded by the time the user reaches Matchups). Verified behaviorally by the screenshot gate (Task 8).
3. **WPF-UI symbol** for the Matchups nav item compiles (verify the `SymbolRegular` value exists, e.g. `Sword24`; fall back to a known-good one such as `PeopleSwap24` or `Versus24` if absent — same approach as M2's `DataTrending24`).
4. **Null-opponent reality** (read-only): some ranked games have a null `opponent_champion_id` and are excluded from matchups — a data fact to report in the hand-back, no code dependency.

## Existing code touch-points (read before starting)

- `src/RiftReview.Core/Data/MatchRow.cs` — `MatchRow` record (20 positional params; `OpponentChampionId` is the 16th, nullable `int?`; derived `KillParticipation`/`DamageShare`/`DeathsPre15` are the trailing three).
- `src/RiftReview.Core/Data/RiftReviewDb.cs` — `AllMatches(bool rankedOnly)`, `GetMatch(string)→MatchRow?`, `RecentMatches(bool,int)`, `UpsertMatch`, `SetMeta`/`GetMeta`.
- `src/RiftReview.Core/Analysis/ChampTrendCalculator.cs` + `ChampPoolCalculator.cs` — pure-calculator pattern to mirror.
- `src/RiftReview.App/ViewModels/MainViewModel.cs` — **singleton**; `Matches` (recent 20), `SelectedMatch`, `DeepDive` (`DeepDiveViewModel`), `Reload()`, `InitializeAsync()`, `OnSelectedMatchChanged`.
- `src/RiftReview.App/ViewModels/DeepDiveViewModel.cs` — `Load(MatchRow)` works on any match; `HasData`.
- `src/RiftReview.App/ViewModels/MatchListItemViewModel.cs` — display-wrapper pattern; `src/RiftReview.App/ViewModels/TrendsViewModel.cs` — VM + nested `ChampChoice` + `[ObservableProperty]` pattern to mirror.
- `src/RiftReview.App/Views/ChampPoolView.xaml` + `TrendsView.xaml` — theme idioms: `PanelBgBrush`, `AccentBrush` (gold title), `CardBgBrush` (selected/hover surface — use for the **selected opponent row**), `HairlineBrush`, `TextPrimaryBrush`, `TextMutedBrush`, `WinBrush`, `LossBrush`, `conv:BoolToBrushConverter`, `BooleanToVisibilityConverter`.
- `src/RiftReview.App/Views/ReviewView.xaml.cs` — DI ctor + `Loaded → InitializeAsync` pattern.
- `src/RiftReview.App/AppShell.xaml(.cs)`, `App.xaml.cs`, `Demo/DemoSeeder.cs`.
- `tests/RiftReview.App.Tests/DeepDiveViewModelTests.cs` — pattern for building valid match/timeline blobs in App tests.

App test classes have NO namespace (top-level). Core tests `using` the namespaces. `RiotOptions` is in `RiftReview.Core.Configuration`. Commit author is the repo default (`yovanmc`); append `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>` to every commit. Plain `git commit` — never `--author`.

---

## Task 1: `MatchupModels` + `MatchupCalculator` (pure)

**Files:**
- Create: `src/RiftReview.Core/Analysis/MatchupModels.cs`
- Create: `src/RiftReview.Core/Analysis/MatchupCalculator.cs`
- Test: `tests/RiftReview.Core.Tests/MatchupCalculatorTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using RiftReview.Core.Analysis;
using RiftReview.Core.Data;
using Xunit;

public class MatchupCalculatorTests
{
    // champ 103 (K'Sante) vs an opponent; nullable scalars set explicitly per test
    private static MatchRow Row(long start, int champ, int? oppChamp, bool win,
        int deaths = 3, int? gold15 = 100, int? cs10 = 60, int? pre15 = 1) =>
        new("NA1_" + start, 420, start, 1800, "15.12", champ, "TOP", win,
            5, deaths, 7, 200, cs10, gold15, 8, oppChamp, 100, 0.6, 0.25, pre15);

    [Fact]
    public void Build_groups_by_opponent_computes_winrate_and_sorts_by_games_desc()
    {
        var games = new List<MatchRow>();
        // champ 103 vs 114 (Camille): 4 games, 1 win
        for (int i = 0; i < 4; i++) games.Add(Row(i, 103, 114, win: i == 0));
        // champ 103 vs 24 (Jax): 2 games, 2 wins
        for (int i = 4; i < 6; i++) games.Add(Row(i, 103, 24, win: true));

        var rows = MatchupCalculator.Build(games);

        Assert.Equal(2, rows.Count);
        Assert.Equal(114, rows[0].OpponentChampionId);     // 4 games first (games desc)
        Assert.Equal(4, rows[0].Games);
        Assert.Equal(0.25, rows[0].WinRate, 3);
        Assert.Equal(24, rows[1].OpponentChampionId);
        Assert.Equal(1.0, rows[1].WinRate, 3);
    }

    [Fact]
    public void Build_excludes_games_with_null_opponent()
    {
        var games = new List<MatchRow>
        {
            Row(0, 103, 114, true),
            Row(1, 103, null, true),   // no lane opponent — excluded
        };
        var rows = MatchupCalculator.Build(games);
        Assert.Single(rows);
        Assert.Equal(114, rows[0].OpponentChampionId);
        Assert.Equal(1, rows[0].Games);
    }

    [Fact]
    public void Build_averages_skip_null_scalars_and_null_when_all_missing()
    {
        var games = new List<MatchRow>
        {
            Row(0, 103, 114, true,  gold15: 100, pre15: 2),
            Row(1, 103, 114, false, gold15: null, pre15: null),   // gold/pre15 null here
            Row(2, 103, 114, true,  gold15: 300, pre15: 4),
        };
        var rows = MatchupCalculator.Build(games);
        Assert.Equal(200, rows[0].AvgGoldDiff15!.Value, 1);   // (100+300)/2, null skipped
        Assert.Equal(3, rows[0].AvgPre15!.Value, 1);          // (2+4)/2
    }

    [Fact]
    public void Build_avg_is_null_when_every_value_missing()
    {
        var games = new List<MatchRow>
        {
            Row(0, 103, 114, true,  gold15: null),
            Row(1, 103, 114, false, gold15: null),
        };
        var rows = MatchupCalculator.Build(games);
        Assert.Null(rows[0].AvgGoldDiff15);
    }

    [Fact]
    public void Build_games_list_is_newest_first()
    {
        var games = new List<MatchRow> { Row(10, 103, 114, true), Row(30, 103, 114, false), Row(20, 103, 114, true) };
        var rows = MatchupCalculator.Build(games);
        Assert.Equal(new long[] { 30, 20, 10 }, rows[0].GamesList.Select(g => g.GameStartUtc).ToArray());
    }

    [Fact]
    public void EligibleChampions_filters_by_min_games_and_sorts_desc()
    {
        var rows = new List<MatchRow>();
        for (int i = 0; i < 8; i++) rows.Add(Row(i, 103, 114, true));      // champ 103: 8 games
        for (int i = 8; i < 11; i++) rows.Add(Row(i, 24, 114, true));      // champ 24: 3 games
        var eligible = MatchupCalculator.EligibleChampions(rows, minGames: 5);
        Assert.Equal(new[] { 103 }, eligible.ToArray());                   // 24 excluded (<5)
    }
}
```

- [ ] **Step 2: Run to verify failure** — `dotnet test RiftReview.slnx --filter MatchupCalculatorTests`; Expected: FAIL (types missing).

- [ ] **Step 3: Create `MatchupModels.cs`**

```csharp
using RiftReview.Core.Data;

namespace RiftReview.Core.Analysis;

// One enemy-laner matchup for a selected champ: aggregates + the games behind it.
public sealed record MatchupRow(
    int OpponentChampionId,
    int Games,
    int Wins,
    double WinRate,
    double? AvgGoldDiff15,
    double? AvgCs10,
    double AvgDeaths,
    double AvgKda,
    double? AvgPre15,
    IReadOnlyList<MatchRow> GamesList);
```

- [ ] **Step 4: Create `MatchupCalculator.cs`**

```csharp
using RiftReview.Core.Data;

namespace RiftReview.Core.Analysis;

// Pure: group a champ's ranked games by lane opponent, compute laning+combat aggregates.
// No DB, no I/O, deterministic. Mirrors ChampTrendCalculator.
public static class MatchupCalculator
{
    public static IReadOnlyList<int> EligibleChampions(IReadOnlyList<MatchRow> rankedMatches, int minGames = 5) =>
        rankedMatches.GroupBy(m => m.MyChampionId)
            .Where(g => g.Count() >= minGames)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key).ToList();

    public static IReadOnlyList<MatchupRow> Build(IReadOnlyList<MatchRow> champGames) =>
        champGames
            .Where(m => m.OpponentChampionId.HasValue)   // no lane opponent → cannot attribute a matchup
            .GroupBy(m => m.OpponentChampionId!.Value)
            .Select(g =>
            {
                var games = g.OrderByDescending(m => m.GameStartUtc).ToList();   // newest first
                int n = games.Count;
                int wins = games.Count(m => m.Win);
                return new MatchupRow(
                    OpponentChampionId: g.Key,
                    Games: n,
                    Wins: wins,
                    WinRate: wins / (double)n,
                    AvgGoldDiff15: AvgNullable(games, m => m.GoldDiffAt15),
                    AvgCs10: AvgNullable(games, m => m.CsAt10),
                    AvgDeaths: games.Average(m => m.Deaths),
                    AvgKda: games.Average(m => (m.Kills + m.Assists) / (double)Math.Max(1, m.Deaths)),
                    AvgPre15: AvgNullable(games, m => m.DeathsPre15),
                    GamesList: games);
            })
            .OrderByDescending(r => r.Games)
            .ToList();

    // Mean over games whose value is non-null; null if none have a value.
    private static double? AvgNullable(IReadOnlyList<MatchRow> games, Func<MatchRow, int?> sel)
    {
        var vals = games.Where(m => sel(m).HasValue).Select(m => (double)sel(m)!.Value).ToList();
        return vals.Count == 0 ? null : vals.Average();
    }
}
```

- [ ] **Step 5: Run to verify pass** — `dotnet test RiftReview.slnx --filter MatchupCalculatorTests`; Expected: PASS (6).

- [ ] **Step 6: Build + full test + commit**

```bash
dotnet build RiftReview.slnx
dotnet test RiftReview.slnx
git add src/RiftReview.Core/Analysis/MatchupModels.cs src/RiftReview.Core/Analysis/MatchupCalculator.cs tests/RiftReview.Core.Tests/MatchupCalculatorTests.cs
git commit -m "feat(core): MatchupCalculator — per-champ lane-opponent aggregates

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 2: `MatchupRowViewModel` + `MatchupGameViewModel` (display wrappers)

**Files:**
- Create: `src/RiftReview.App/ViewModels/MatchupGameViewModel.cs`
- Create: `src/RiftReview.App/ViewModels/MatchupRowViewModel.cs`
- Test: `tests/RiftReview.App.Tests/MatchupRowViewModelTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System.Linq;
using RiftReview.App.ViewModels;
using RiftReview.Core.Analysis;
using RiftReview.Core.Data;
using Xunit;

public class MatchupRowViewModelTests
{
    private static MatchRow Game(long start, bool win, int? gold15) =>
        new("NA1_" + start, 420, start, 1800, "15.12", 103, "TOP", win,
            5, 3, 7, 200, 60, gold15, 8, 114, 100, 0.6, 0.25, 1);

    [Fact]
    public void Favorable_when_sampled_and_winrate_high()
    {
        var games = new List<MatchRow> { Game(0, true, 200), Game(1, true, 100), Game(2, true, 300), Game(3, false, -50) };
        var mr = MatchupCalculator.Build(games)[0];   // 4 games, 75%
        var vm = new MatchupRowViewModel(mr, "Camille");
        Assert.Equal("Camille", vm.OpponentName);
        Assert.Equal("75%", vm.WinPercent);
        Assert.True(vm.IsFavorable);
        Assert.False(vm.IsThin);
        Assert.Equal(4, vm.GameRows.Count);
        Assert.Equal("+138", vm.GoldDiff15);          // (200+100+300-50)/4 = 137.5 -> +138
    }

    [Fact]
    public void Thin_when_under_three_games_no_color()
    {
        var games = new List<MatchRow> { Game(0, true, 100), Game(1, false, -100) };
        var mr = MatchupCalculator.Build(games)[0];   // 2 games
        var vm = new MatchupRowViewModel(mr, "Garen");
        Assert.True(vm.IsThin);
        Assert.False(vm.IsFavorable);
        Assert.False(vm.IsUnfavorable);
    }

    [Fact]
    public void Null_gold_renders_dash()
    {
        var games = new List<MatchRow> { Game(0, true, null), Game(1, true, null), Game(2, false, null) };
        var mr = MatchupCalculator.Build(games)[0];
        var vm = new MatchupRowViewModel(mr, "Sett");
        Assert.Equal("—", vm.GoldDiff15);
    }

    [Fact]
    public void Game_row_formats_result_and_gold()
    {
        var vm = new MatchupGameViewModel(Game(1_700_000_000, false, -620));
        Assert.Equal("Loss", vm.Result);
        Assert.False(vm.Win);
        Assert.Equal("5/3/7", vm.Kda);
        Assert.Equal("-620", vm.GoldDiff15);
        Assert.Equal("NA1_1700000000", vm.MatchId);
    }
}
```

- [ ] **Step 2: Run to verify failure** — `dotnet test RiftReview.slnx --filter MatchupRowViewModelTests`; Expected: FAIL.

- [ ] **Step 3: Create `MatchupGameViewModel.cs`**

```csharp
using System;
using RiftReview.Core.Data;

namespace RiftReview.App.ViewModels;

// One game inside a matchup's games list; clicking it opens its deep-dive.
public sealed class MatchupGameViewModel
{
    private readonly MatchRow _row;
    public MatchupGameViewModel(MatchRow row) => _row = row;

    public string MatchId => _row.MatchId;
    public bool Win => _row.Win;
    public string Result => _row.Win ? "Win" : "Loss";
    public string Kda => $"{_row.Kills}/{_row.Deaths}/{_row.Assists}";
    public string GoldDiff15 => _row.GoldDiffAt15 is int g ? g.ToString("+0;-0;0") : "—";
    public string WhenLocal => DateTimeOffset.FromUnixTimeSeconds(_row.GameStartUtc).LocalDateTime.ToString("MMM d");
}
```

- [ ] **Step 4: Create `MatchupRowViewModel.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using RiftReview.Core.Analysis;

namespace RiftReview.App.ViewModels;

// Display wrapper over a MatchupRow: formats aggregates, maps win% -> thin/favorable/unfavorable flags.
public sealed class MatchupRowViewModel
{
    public const int ConfidenceMinGames = 3;

    private readonly MatchupRow _m;
    public MatchupRowViewModel(MatchupRow m, string opponentName)
    {
        _m = m;
        OpponentName = opponentName;
        GameRows = m.GamesList.Select(g => new MatchupGameViewModel(g)).ToList();
    }

    public string OpponentName { get; }
    public int GamesCount => _m.Games;
    public string GamesLabel => _m.Games + "g";
    public string Record => $"{_m.Wins}–{_m.Games - _m.Wins}";   // e.g. "5–2"
    public string WinPercent => Math.Round(_m.WinRate * 100) + "%";
    public IReadOnlyList<MatchupGameViewModel> GameRows { get; }

    public bool IsThin => _m.Games < ConfidenceMinGames;
    public bool IsFavorable => !IsThin && _m.WinRate >= 0.55;
    public bool IsUnfavorable => !IsThin && _m.WinRate <= 0.45;

    public string GoldDiff15 => Round0(_m.AvgGoldDiff15);                 // "+138" / "-480" / "—"
    public string Cs10 => _m.AvgCs10 is double c ? Math.Round(c).ToString() : "—";
    public string Deaths => Math.Round(_m.AvgDeaths, 1).ToString();
    public string Kda => Math.Round(_m.AvgKda, 1).ToString();
    public string Pre15 => _m.AvgPre15 is double p ? Math.Round(p, 1).ToString() : "—";

    private static string Round0(double? v) => v is double d ? Math.Round(d).ToString("+0;-0;0") : "—";
}
```

- [ ] **Step 5: Run to verify pass** — `dotnet test RiftReview.slnx --filter MatchupRowViewModelTests`; Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/RiftReview.App/ViewModels/MatchupGameViewModel.cs src/RiftReview.App/ViewModels/MatchupRowViewModel.cs tests/RiftReview.App.Tests/MatchupRowViewModelTests.cs
git commit -m "feat(app): matchup row + game display wrappers

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 3: `MainViewModel.ShowMatch` + `_namesReady` guard

**Files:**
- Modify: `src/RiftReview.App/ViewModels/MainViewModel.cs`
- Test: `tests/RiftReview.App.Tests/MainViewModelShowMatchTests.cs`

`ShowMatch(matchId)` loads any stored game into the shared deep-dive; the `_namesReady` guard stops a second navigation to Review from re-`Reload()`-ing and clobbering it.

- [ ] **Step 1: Write the failing test** (valid blobs so the `MainViewModel` ctor's `Reload → DeepDive.Load` doesn't throw — empty timeline frames are fine):

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RiftReview.App.ViewModels;
using RiftReview.Core.Configuration;
using RiftReview.Core.Data;
using RiftReview.Core.DataDragon;
using RiftReview.Core.Riot.Dtos;
using Xunit;

public class MainViewModelShowMatchTests
{
    private static MainViewModel Make(RiftReviewDb db)
    {
        var dd = new DataDragonClient(new System.Net.Http.HttpClient(), System.IO.Path.GetTempPath());
        // sync + client are stored but never touched by the ctor or ShowMatch -> null! is safe here.
        return new MainViewModel(db, null!, null!, dd, Options.Create(new RiotOptions()), new SettingsStore(db));
    }

    private static MatchRow Row(string id, long start, int oppChamp) =>
        new(id, 420, start, 1800, "15.12", 103, "TOP", true, 5, 3, 7, 200, 60, 100, 8, oppChamp, 100, 0.6, 0.25, 1);

    private static void Seed(RiftReviewDb db, string id, long start, int oppChamp)
    {
        var parts = new List<ParticipantDto>
        {
            new("ME", 3, 103, 100, "TOP", true, 5, 3, 7, 200, 10, 1000),
            new("OPP", 8, oppChamp, 200, "TOP", false, 3, 5, 4, 180, 0, 800),
        };
        var match = new MatchDto(new MatchMetadata(id, parts.Select(p => p.Puuid).ToList()),
            new MatchInfo(420, 1_700_000_000_000, 1800, "15.12.1", parts));
        var tl = new TimelineDto(new TimelineMetadata(id, new()), new TimelineInfo(60000, new List<FrameDto>()));
        db.UpsertMatch(Row(id, start, oppChamp), JsonSerializer.Serialize(match), JsonSerializer.Serialize(tl));
    }

    [Fact]
    public void ShowMatch_selects_an_in_list_match()
    {
        using var db = RiftReviewDb.Open("Data Source=:memory:");
        db.SetMeta("puuid", "ME");
        Seed(db, "NA1_1", 5, 114);
        Seed(db, "NA1_2", 6, 24);
        var vm = Make(db);                       // ctor selects newest (NA1_2)

        vm.ShowMatch("NA1_1");

        Assert.Equal("NA1_1", vm.SelectedMatch!.MatchId);
        Assert.True(vm.DeepDive.HasData);
    }

    [Fact]
    public void ShowMatch_unknown_id_is_a_noop()
    {
        using var db = RiftReviewDb.Open("Data Source=:memory:");
        db.SetMeta("puuid", "ME");
        Seed(db, "NA1_1", 5, 114);
        var vm = Make(db);
        var before = vm.SelectedMatch;

        vm.ShowMatch("NOPE");                    // must not throw

        Assert.Equal(before, vm.SelectedMatch);  // unchanged
    }
}
```

- [ ] **Step 2: Run to verify failure** — `dotnet test RiftReview.slnx --filter MainViewModelShowMatchTests`; Expected: FAIL (`ShowMatch` missing).

- [ ] **Step 3: Add `ShowMatch` + the `_namesReady` guard to `MainViewModel`**

Add the field near the other private fields:

```csharp
    private bool _namesReady;
```

Replace the body of `InitializeAsync` with the guarded version (keeps the first load, skips later ones so a re-navigation doesn't clobber a `ShowMatch` deep-dive):

```csharp
    public async Task InitializeAsync()
    {
        if (_namesReady) return;   // names already loaded; don't Reload (would clobber a ShowMatch deep-dive)
        try { await _ddragon.EnsureLoadedAsync(); _namesReady = true; }
        catch { /* offline — leave _namesReady false so we retry on the next load */ }
        Reload();
        SetIdleStatus();
    }
```

Add `ShowMatch` (place it next to `Reload`):

```csharp
    // Open any stored game in the shared deep-dive. Used by the Matchups page to drill into a game.
    // Works for games outside the recent-20 rail (DeepDive.Load fetches blobs by id); the rail simply
    // won't highlight an off-list game.
    public void ShowMatch(string matchId)
    {
        var row = _db.GetMatch(matchId);
        if (row is null) return;
        var inList = Matches.FirstOrDefault(m => m.MatchId == matchId);
        if (inList is not null) { SelectedMatch = inList; }   // fires OnSelectedMatchChanged -> DeepDive.Load
        else { SelectedMatch = null; DeepDive.Load(row); }
    }
```

(`System.Linq` is already imported via the existing `Reload`/`FirstOrDefault` usage. Confirm the `using` is present; add `using System.Linq;` if the build complains.)

- [ ] **Step 4: Run to verify pass** — `dotnet test RiftReview.slnx --filter MainViewModelShowMatchTests`; Expected: PASS. Then `dotnet test RiftReview.slnx` (full) — confirm no regression.

- [ ] **Step 5: Commit**

```bash
git add src/RiftReview.App/ViewModels/MainViewModel.cs tests/RiftReview.App.Tests/MainViewModelShowMatchTests.cs
git commit -m "feat(app): MainViewModel.ShowMatch + names-ready guard for the matchup deep-dive drill

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 4: `NavigationService` + `AppShell` subscription

**Files:**
- Create: `src/RiftReview.App/Services/NavigationService.cs`
- Modify: `src/RiftReview.App/AppShell.xaml.cs`
- Modify: `src/RiftReview.App/App.xaml.cs` (register the service)
- Test: `tests/RiftReview.App.Tests/NavigationServiceTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System;
using RiftReview.App.Services;
using Xunit;

public class NavigationServiceTests
{
    [Fact]
    public void NavigateTo_raises_event_with_target_type()
    {
        var nav = new NavigationService();
        Type? got = null;
        nav.NavigationRequested += t => got = t;
        nav.NavigateTo(typeof(string));
        Assert.Equal(typeof(string), got);
    }

    [Fact]
    public void NavigateTo_without_subscriber_does_not_throw()
    {
        var nav = new NavigationService();
        nav.NavigateTo(typeof(string));   // no subscriber — must be safe
    }
}
```

- [ ] **Step 2: Run to verify failure** — `dotnet test RiftReview.slnx --filter NavigationServiceTests`; Expected: FAIL (type missing).

- [ ] **Step 3: Create `src/RiftReview.App/Services/NavigationService.cs`**

```csharp
using System;

namespace RiftReview.App.Services;

// Lets a non-shell ViewModel ask the AppShell to navigate the NavigationView to a page.
public sealed class NavigationService
{
    public event Action<Type>? NavigationRequested;
    public void NavigateTo(Type pageType) => NavigationRequested?.Invoke(pageType);
}
```

- [ ] **Step 4: Subscribe in `AppShell.xaml.cs`** — change the ctor to take the service and forward requests to `RootNavigation`:

```csharp
    public AppShell(IServiceProvider sp, RiftReview.App.Services.NavigationService nav)
    {
        InitializeComponent();
        RootNavigation.SetServiceProvider(sp);
        nav.NavigationRequested += t => RootNavigation.Navigate(t, null);
        Loaded += OnLoaded;
    }
```

(Leave `OnLoaded` unchanged.)

- [ ] **Step 5: Register in `App.xaml.cs`** — add beside the other singletons (before the pages):

```csharp
                s.AddSingleton<RiftReview.App.Services.NavigationService>();
```

- [ ] **Step 6: Build + test** — `dotnet build RiftReview.slnx`; `dotnet test RiftReview.slnx --filter NavigationServiceTests`; Expected: clean + PASS. Then `dotnet test RiftReview.slnx` (full).

- [ ] **Step 7: Commit**

```bash
git add src/RiftReview.App/Services/NavigationService.cs src/RiftReview.App/AppShell.xaml.cs src/RiftReview.App/App.xaml.cs tests/RiftReview.App.Tests/NavigationServiceTests.cs
git commit -m "feat(app): NavigationService for cross-page navigation requests

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 5: `MatchupsViewModel`

**Files:**
- Create: `src/RiftReview.App/ViewModels/MatchupsViewModel.cs`
- Test: `tests/RiftReview.App.Tests/MatchupsViewModelTests.cs`

- [ ] **Step 1: Write the failing test** (gives `MainViewModel` its own empty DB so its ctor never loads a deep-dive — the matchup data lives in a separate DB):

```csharp
using System;
using System.Linq;
using Microsoft.Extensions.Options;
using RiftReview.App.Services;
using RiftReview.App.ViewModels;
using RiftReview.Core.Configuration;
using RiftReview.Core.Data;
using RiftReview.Core.DataDragon;
using Xunit;

public class MatchupsViewModelTests
{
    private static DataDragonClient Dd() => new(new System.Net.Http.HttpClient(), System.IO.Path.GetTempPath());

    private static MainViewModel MakeMain()
    {
        var empty = RiftReviewDb.Open("Data Source=:memory:");   // no matches -> ctor never loads a deep-dive
        return new MainViewModel(empty, null!, null!, Dd(), Options.Create(new RiotOptions()), new SettingsStore(empty));
    }

    private static MatchRow Row(long start, int champ, int oppChamp, bool win) =>
        new("NA1_" + start, 420, start, 1800, "15.12", champ, "TOP", win, 5, 3, 7, 200, 60, 100, 8, oppChamp, 100, 0.6, 0.25, 1);

    private static MatchupsViewModel Make(RiftReviewDb db, NavigationService nav) =>
        new(db, Dd(), MakeMain(), nav);

    [Fact]
    public void Lists_eligible_champ_and_builds_opponent_rows()
    {
        using var db = RiftReviewDb.Open("Data Source=:memory:");
        db.SetMeta("puuid", "ME");
        // champ 103: 5 games vs 114, 3 vs 24  -> eligible (8 >= 5); both opponents appear
        for (int i = 0; i < 5; i++) db.UpsertMatch(Row(i, 103, 114, i == 0), "{}", "{}");
        for (int i = 5; i < 8; i++) db.UpsertMatch(Row(i, 103, 24, true), "{}", "{}");

        var vm = Make(db, new NavigationService());
        vm.Load();

        Assert.Contains(vm.Champions, c => c.ChampionId == 103);
        Assert.Equal(2, vm.Opponents.Count);            // faced 114 (5g) and 24 (3g)
        Assert.Equal(5, vm.Opponents[0].GamesCount);    // 114 first (games desc)
    }

    [Fact]
    public void Min_games_filter_hides_rows_below_it()
    {
        using var db = RiftReviewDb.Open("Data Source=:memory:");
        db.SetMeta("puuid", "ME");
        for (int i = 0; i < 6; i++) db.UpsertMatch(Row(i, 103, 114, true), "{}", "{}");   // 6 games vs 114
        for (int i = 6; i < 8; i++) db.UpsertMatch(Row(i, 103, 24, true), "{}", "{}");     // 2 games vs 24

        var vm = Make(db, new NavigationService());
        vm.Load();
        Assert.Equal(2, vm.Opponents.Count);          // default filter 1 -> both shown
        vm.MinGamesFilter = 3;                         // hide the 2-game matchup
        Assert.Single(vm.Opponents);
        Assert.Equal(6, vm.Opponents[0].GamesCount);
    }

    [Fact]
    public void Open_deep_dive_command_requests_review_navigation()
    {
        using var db = RiftReviewDb.Open("Data Source=:memory:");
        db.SetMeta("puuid", "ME");
        for (int i = 0; i < 5; i++) db.UpsertMatch(Row(i, 103, 114, true), "{}", "{}");
        var nav = new NavigationService();
        Type? navigatedTo = null;
        nav.NavigationRequested += t => navigatedTo = t;

        var vm = Make(db, nav);
        vm.Load();
        vm.OpenDeepDiveCommand.Execute("NA1_0");

        Assert.Equal(typeof(RiftReview.App.Views.ReviewView), navigatedTo);
    }
}
```

(The first assertion in `Lists_eligible_champ_and_builds_opponent_rows` is intentionally lenient on champ-name placeholder text — Data Dragon is not loaded in the test, so `ChampionName(114)` returns a placeholder like `"Champ 114"`. Keep the assertion focused on `ChampionId`/`GamesCount`. If you prefer, assert `vm.Opponents.Count == 2` and `vm.Opponents.Any(o => o.GamesCount == 5)`.)

- [ ] **Step 2: Run to verify failure** — `dotnet test RiftReview.slnx --filter MatchupsViewModelTests`; Expected: FAIL.

- [ ] **Step 3: Implement `MatchupsViewModel.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RiftReview.App.Services;
using RiftReview.App.Views;
using RiftReview.Core.Analysis;
using RiftReview.Core.Data;
using RiftReview.Core.DataDragon;

namespace RiftReview.App.ViewModels;

public sealed partial class MatchupsViewModel : ObservableObject
{
    private readonly RiftReviewDb _db;
    private readonly DataDragonClient _ddragon;
    private readonly MainViewModel _main;
    private readonly NavigationService _nav;

    // Matchups are always ranked-only (locked design decision), so no SettingsStore dependency.
    public MatchupsViewModel(RiftReviewDb db, DataDragonClient ddragon, MainViewModel main, NavigationService nav)
    {
        _db = db; _ddragon = ddragon; _main = main; _nav = nav;
    }

    public ObservableCollection<ChampChoice> Champions { get; } = new();
    public ObservableCollection<MatchupRowViewModel> Opponents { get; } = new();

    [ObservableProperty] private ChampChoice? _selectedChampion;
    [ObservableProperty] private MatchupRowViewModel? _selectedOpponent;
    [ObservableProperty] private int _minGamesFilter = 1;
    [ObservableProperty] private bool _isEmpty;

    public sealed record ChampChoice(int ChampionId, string Name);

    public async Task InitializeAsync()
    {
        try { await _ddragon.EnsureLoadedAsync(); } catch { /* names fall back to placeholders */ }
        Load();
    }

    public void Load()
    {
        var ranked = _db.AllMatches(rankedOnly: true);
        var eligible = MatchupCalculator.EligibleChampions(ranked);   // >= 5 ranked games
        Champions.Clear();
        foreach (var id in eligible) Champions.Add(new ChampChoice(id, _ddragon.ChampionName(id)));
        IsEmpty = Champions.Count == 0;
        SelectedChampion = Champions.FirstOrDefault();   // triggers OnSelectedChampionChanged
        if (SelectedChampion is null) Opponents.Clear();
    }

    partial void OnSelectedChampionChanged(ChampChoice? value) => RebuildOpponents();
    partial void OnMinGamesFilterChanged(int value) => RebuildOpponents();

    private void RebuildOpponents()
    {
        Opponents.Clear();
        if (SelectedChampion is null) return;
        var games = _db.AllMatches(rankedOnly: true).Where(m => m.MyChampionId == SelectedChampion.ChampionId).ToList();
        foreach (var mr in MatchupCalculator.Build(games).Where(r => r.Games >= MinGamesFilter))
            Opponents.Add(new MatchupRowViewModel(mr, _ddragon.ChampionName(mr.OpponentChampionId)));
        SelectedOpponent = Opponents.FirstOrDefault();
    }

    [RelayCommand]
    private void OpenDeepDive(string matchId)
    {
        _main.ShowMatch(matchId);
        _nav.NavigateTo(typeof(ReviewView));
    }
}
```

- [ ] **Step 4: Run to verify pass** — `dotnet test RiftReview.slnx --filter MatchupsViewModelTests`; Expected: PASS. Then `dotnet test RiftReview.slnx` (full).

- [ ] **Step 5: Commit**

```bash
git add src/RiftReview.App/ViewModels/MatchupsViewModel.cs tests/RiftReview.App.Tests/MatchupsViewModelTests.cs
git commit -m "feat(app): MatchupsViewModel — eligible champs, opponent rows, min-games filter, deep-dive drill

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 6: `MatchupsView` (layout C) + nav + DI

**Files:**
- Create: `src/RiftReview.App/Views/MatchupsView.xaml` (+ `.xaml.cs`)
- Modify: `src/RiftReview.App/AppShell.xaml` (+ `.xaml.cs`)
- Modify: `src/RiftReview.App/App.xaml.cs`

- [ ] **Step 1: `MatchupsView.xaml`** — master–detail layout C on a `PanelBgBrush` surface. READ `ChampPoolView.xaml` and `TrendsView.xaml` first to copy the exact `xmlns` lines, brush keys, and chip styling. Structure:
  - Root `Grid Background="{StaticResource PanelBgBrush}"`, rows: header (Auto) + body (*).
  - **Header:** gold "Matchups" `TextBlock` (`AccentBrush`, FontSize 18, SemiBold); a champ selector `ListBox` (`ItemsSource={Binding Champions}`, `SelectedItem={Binding SelectedChampion, Mode=TwoWay}`, `WrapPanel` ItemsPanel, gold-pill `ItemContainerStyle` — copy the chip style from `TrendsView.xaml`); a "min games" control — a `Slider Minimum="1" Maximum="10" Value="{Binding MinGamesFilter, Mode=TwoWay}" IsSnapToTickEnabled="True" TickFrequency="1"` with a live `TextBlock Text="{Binding MinGamesFilter}"` and a muted label "min games".
  - **Body:** a 2-column `Grid` (`40*` ‖ `60*`):
    - **Left (opponent list):** `ListBox ItemsSource="{Binding Opponents}" SelectedItem="{Binding SelectedOpponent, Mode=TwoWay}"` with `Background=Transparent`, `BorderThickness=0`. `ItemContainerStyle` (TargetType `ListBoxItem`): `HorizontalContentAlignment=Stretch`, `Padding=10,8`, `BorderThickness=0,0,0,1`, `BorderBrush={StaticResource HairlineBrush}`, and a trigger **`IsSelected=True` → `Background={StaticResource CardBgBrush}`** (the lighter-than-panel surface — NOT gold), plus `IsMouseOver=True → CardBgBrush`. Item template: a `DockPanel` — opponent `OpponentName` (left, `TextPrimaryBrush`) and a right group of `WinPercent` + `GamesLabel`. Win% color via DataTriggers on `IsFavorable` (→ `WinBrush`) / `IsUnfavorable` (→ `LossBrush`), default `TextMutedBrush`; when `IsThin` is `True`, set the whole row `Opacity=0.5`.
    - **Right (detail pane):** `Border Background="{StaticResource CardBgBrush}"`-free panel bound to `SelectedOpponent`; collapse when null (DataTrigger `{Binding SelectedOpponent} == null → Collapsed`). Contains:
      - Heading `"{Binding SelectedChampion.Name} vs {Binding SelectedOpponent.OpponentName}"` (use two `Run`s or a `MultiBinding`/`StringFormat`) + `SelectedOpponent.Record` + `WinPercent`.
      - **Metric tiles** (a `UniformGrid Columns="3"` or `WrapPanel` of small `Border` tiles, each: muted label + value): Gold@15 (`SelectedOpponent.GoldDiff15`), CS@10 (`Cs10`), Deaths (`Deaths`), KDA (`Kda`), Pre-15 (`Pre15`).
      - **Games list:** a muted header "GAMES — click to open deep-dive", then an `ItemsControl ItemsSource="{Binding SelectedOpponent.GameRows}"`; each item a `Button` styled flat (transparent bg, no border, hover `CardBgBrush`, `HorizontalContentAlignment=Stretch`, `Cursor=Hand`) with `Command="{Binding DataContext.OpenDeepDiveCommand, RelativeSource={RelativeSource AncestorType=UserControl}}"` and `CommandParameter="{Binding MatchId}"`; content = a `DockPanel` of `Result` (colored via `BoolToBrushConverter` on `Win`), `Kda`, `GoldDiff15`, `WhenLocal`.
  - **Empty state** (`IsEmpty` → visible): "Play more ranked games (≥ 5 on a champ) to scout your matchups." Use the existing `BooleanToVisibilityConverter`.

  Define `UserControl.Resources` with `conv:BoolToBrushConverter` keys you need (e.g. a `WinDotBrush` Win→`WinBrush`/`LossBrush`) and `BooleanToVisibilityConverter x:Key="BoolToVis"`, mirroring `ReviewView.xaml`.

- [ ] **Step 2: `MatchupsView.xaml.cs`** — DI ctor mirroring `TrendsView`:

```csharp
using System.Windows.Controls;
using RiftReview.App.ViewModels;

namespace RiftReview.App.Views;

public partial class MatchupsView : UserControl
{
    public MatchupsView(MatchupsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Loaded += async (_, _) => await vm.InitializeAsync();
    }
}
```

- [ ] **Step 3: Nav** — in `AppShell.xaml`, add a `NavigationViewItem Content="Matchups" TargetPageType="{x:Type views:MatchupsView}"` between Trends and the Settings footer item, with a valid WPF-UI symbol (try `ui:SymbolIcon Symbol="Sword24"`; if it doesn't compile, use `PeopleSwap24` or `Versus24`). In `AppShell.xaml.cs`, extend the `--page` switch with `"matchups" => typeof(MatchupsView),`.

- [ ] **Step 4: DI** — in `App.xaml.cs` `ConfigureServices`, register beside the other pages:

```csharp
                s.AddTransient<ViewModels.MatchupsViewModel>();
                s.AddTransient<MatchupsView>();
```

- [ ] **Step 5: Build + smoke** — `dotnet build RiftReview.slnx -c Debug`; Expected: clean (0 warnings). Then `dotnet test RiftReview.slnx` (full) — confirm green. (Full visual verification in Task 8.)

- [ ] **Step 6: Commit**

```bash
git add src/RiftReview.App/Views/MatchupsView.xaml src/RiftReview.App/Views/MatchupsView.xaml.cs src/RiftReview.App/AppShell.xaml src/RiftReview.App/AppShell.xaml.cs src/RiftReview.App/App.xaml.cs
git commit -m "feat(app): Matchups page (layout C, master-detail) + nav + DI

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 7: Demo seeder — varied opponents

**Files:**
- Modify: `src/RiftReview.App/Demo/DemoSeeder.cs`

The Matchups page needs a champ that has faced **several distinct opponents** with multiple games each. READ the current `DemoSeeder.cs` first (M2 left champ 103 with 24 games + champ 157 with 8 + one-offs; opponents are currently a fixed/near-fixed champ id).

- [ ] **Step 1: Vary the opponent champion id across the champ-103 games** — in `BuildGame` (or wherever the opponent participant `championId` is set), make the **opponent champion** cycle across a small set so champ 103 has matchups vs ~4 distinct opponents with ≥ 3 games each, with a spread of wins. Concretely, pick the opponent champ from a table keyed by the game index, e.g.:

```csharp
        int[] oppPlan = { 114, 114, 114, 114, 114,   24, 24, 24, 24,   122, 122, 122,   126, 126, 126,
                          114, 24, 122, 126, 114, 24, 122, 126, 114 };   // 24 entries for the 24 champ-103 games
        int oppChamp = pid == 8 ? oppPlan[i % oppPlan.Length] : /* existing enemy ids for other pids */ 145;
```

Adjust so: the **opponent participant** (the one resolved as the lane opponent — same `TeamPosition`, opposite team, i.e. pid 8 in the demo) gets `championId = oppChamp`, and vary the **win** flag so the matchups aren't all 100% (e.g. lose more vs 114 so it reads unfavorable, win more vs 122). Keep the existing CS@10 / damage ramps from M2 intact.

- [ ] **Step 2: Confirm the opponent is attributed** — the demo already sets `MyTeamPosition`/positions such that `MatchExtractor.Summarize` resolves the lane opponent; ensure the opponent participant shares the owner's `TeamPosition` on the opposite team so `opponent_champion_id` is populated (non-null) for champ-103 games. (If the demo currently leaves positions blank for the enemy, set the enemy laner's `TeamPosition` to match the owner's.)

- [ ] **Step 3: Build + smoke** — `dotnet build RiftReview.slnx -c Debug`; Expected: clean. Optionally run `RiftReview.App.exe --seed-demo --page matchups` to eyeball: Matchups lists Ahri (103) with several opponent rows.

- [ ] **Step 4: Commit**

```bash
git add src/RiftReview.App/Demo/DemoSeeder.cs
git commit -m "feat(app): demo seeder varies lane opponents so Matchups renders

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 8: Screenshot verification gate (real data + demo)

**Files:** none (verification only).

- [ ] **Step 1: Build Debug** — `dotnet build RiftReview.slnx -c Debug`; Expected: clean.

- [ ] **Step 2: Verify on the real DB.** **Inside a Sonnet subagent** (PrintWindow flag 2; HW-accel-off registry workaround `HKCU\Software\Microsoft\Avalon.Graphics\DisableHWAcceleration` set+restored; the subagent VIEWS its own PNGs and returns a TEXT verdict + PNG paths — do NOT load PNGs into the main context), launch `RiftReview.App.exe --page matchups` on the owner's real `%LOCALAPPDATA%\RiftReview\riftreview.db` and verify:
  - Nav rail shows Review / Champions / Trends / **Matchups** / Settings; switching works.
  - **Matchups:** champ chips list real mains (K'Sante etc.); selecting one shows the **opponent list** (left) with colored win% + games, sorted games desc, thin (<3) rows muted with no color; the **selected opponent row uses the lighter `CardBgBrush` surface, not gold**.
  - **Detail pane** (right) shows the selected matchup's metric tiles (Gold@15, CS@10, Deaths, KDA, Pre-15) + the **games list**.
  - **Drill:** clicking a game in the list navigates to the **Review** page showing that game's deep-dive (gold-diff/CS charts). Confirm it works for a game **older than the recent-20** if one is reachable.
  - Bump the **min-games filter** and confirm low-sample rows disappear.
  - No crash / empty-state-with-data / error banner.

- [ ] **Step 3: Verify under `--seed-demo`** — relaunch `--seed-demo --page matchups`; confirm the demo champ lists several opponent rows with varied win%, and the drill opens a deep-dive. Same subagent text-verdict method.

- [ ] **Step 4: Holistic review + commit (if any fixes)** — run a final spec-coverage pass; fix any defects in their own commits. No code change → no commit.

---

## Acceptance criteria (from spec §13)

- `dotnet build RiftReview.slnx` clean (0 warnings); `dotnet test RiftReview.slnx` all green (incl. new `MatchupCalculator` + VM tests).
- No schema change, no migration, no backfill, no Riot calls.
- Matchups page lists champs with ≥ 5 ranked games; selecting one shows opponents faced (master–detail layout C) with Games · Win% · Gold@15 Δ · CS@10 · Deaths · KDA · Pre-15, sorted games desc.
- Thin matchups (< 3 games) muted with no win-rate color; sampled rows win-rate colored (green favorable / red unfavorable); the min-games filter hides rows below it; the **selected row uses the lighter `CardBgBrush` surface, not gold**.
- Selecting an opponent shows its metric tiles + games list; clicking a game opens that game's full Review deep-dive (incl. games older than the recent-20).
- No secrets added; `appsettings.json` placeholders only.

## Hand-back (cannot verify here — owner runs locally)

- Sanity-check matchup numbers on real data: do the win% / gold@15 reads for a hard lane on a main (e.g. K'Sante) match the owner's felt experience?
- Confirm coverage: how many ranked games (if any) are excluded for a null lane opponent, and whether that meaningfully thins any champ's matchup table.
- Confirm the deep-dive drill opens the correct game — including an older game outside the Review recent-20 list.
