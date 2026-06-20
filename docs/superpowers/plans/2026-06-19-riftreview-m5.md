# RiftReview M5 — Climb View — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A **Climb** page showing current ranked standing (Solo + Flex), ranked win/loss momentum (streaks + recent-form strip, rich today), and an honest net-LP-per-snapshot-window ledger that accrues finer detail as the owner keeps syncing.

**Architecture:** A shared `RankLadder` (tier/division/LP → absolute points + display string), a pure `ClimbCalculator` (streaks from matches + LP segments by pairing snapshot-deltas with the ranked games in each window) — **no schema change, no migration, no Riot calls, no SyncService change**. A transient `ClimbViewModel` + `ClimbView`. Mirrors the existing pure-calculator + VM + view patterns.

**Tech Stack:** C#/.NET 10, WPF + WPF-UI 4.3, Microsoft.Data.Sqlite, CommunityToolkit.Mvvm, xUnit.

**Design spec:** `docs/superpowers/specs/2026-06-19-riftreview-m5-design.md`.

---

## Build-time facts (from a fresh dossier — trust these)

- `LpSnapshot` (`src/RiftReview.Core/Data/LpSnapshot.cs`): `record (long TakenUtc, string QueueType, string Tier, string Division, int LeaguePoints, int Wins, int Losses)`. `QueueType` ∈ `"RANKED_SOLO_5x5"` / `"RANKED_FLEX_SR"`. `Tier` is ALL-CAPS (`"GOLD"`), `Division` is `"I"/"II"/"III"/"IV"`.
- `RiftReviewDb`: `GetLpSnapshots()` → `IReadOnlyList<LpSnapshot>` ordered `taken_utc ASC`; `InsertLpSnapshot(LpSnapshot s)`; `AllMatches(bool rankedOnly)`; `LatestSchemaVersion = 3` (NO change).
- `MatchRow`: `QueueId` is field #2 (420 solo / 440 flex), `GameStartUtc` (#3, **unix seconds**), `Win` (#8). Full order: `MatchId, QueueId, GameStartUtc, DurationS, Patch, MyChampionId, MyTeamPosition, Win, Kills, Deaths, Assists, Cs, CsAt10(int?), GoldDiffAt15(int?), OpponentParticipantId(int?), OpponentChampionId(int?), SyncedAt, KillParticipation(double?)=null, DamageShare(double?)=null, DeathsPre15(int?)=null`.
- `TrendsViewModel` (`src/RiftReview.App/ViewModels/TrendsViewModel.cs`) builds its LP headline as `$"{Cap(latest.Tier)} {latest.Division} · {latest.LeaguePoints} LP"` with a private `Cap()` — M5 Task 1 replaces this with `RankLadder.Format(...)` (DRY). The `·` is U+00B7.
- App test classes have NO namespace; in-memory DB `RiftReviewDb.Open("Data Source=:memory:")`; `DataDragonClient` test ctor `new(new HttpClient(), Path.GetTempPath())`; seed via `db.UpsertMatch(row, "{}", "{}")` and `db.InsertLpSnapshot(snap)`.
- Theme brushes: `PanelBgBrush #16161A`, `CardBgBrush #1E1E24`, `HairlineBrush #2A2A30`, `AccentBrush #C8AA6E`, `TextPrimaryBrush #ECECEC`, `TextMutedBrush #9A9A9A`, `WinBrush #4CC38A`, `LossBrush #E24A4A`. Converter `conv:BoolToBrushConverter` (TrueBrush/FalseBrush); built-in `BooleanToVisibilityConverter` keyed `BoolToVis` inline.
- `AppShell.xaml` menu items now end after "Session Health" (M4 added it); `AppShell.xaml.cs` `--page` switch has `review/champions/trends/matchups/sessions/settings`. `App.xaml.cs` registers paired `AddTransient<ViewModels.XxxViewModel>()` + `AddTransient<XxxView>()`.
- Commit author repo default `yovanmc`; **plain `git commit`, never `--author`**; append `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`. 0 warnings hard gate.

---

## Task 1: `RankLadder` (shared helper) + TrendsViewModel DRY refactor

**Files:**
- Create: `src/RiftReview.Core/Analysis/RankLadder.cs`
- Modify: `src/RiftReview.App/ViewModels/TrendsViewModel.cs`
- Test: `tests/RiftReview.Core.Tests/RankLadderTests.cs`

- [ ] **Step 1: Write the failing tests** → `tests/RiftReview.Core.Tests/RankLadderTests.cs`

```csharp
using RiftReview.Core.Analysis;
using Xunit;

public class RankLadderTests
{
    [Fact]
    public void ToPoints_is_monotonic_across_boundaries()
    {
        int goldIV0 = RankLadder.ToPoints("GOLD", "IV", 0);
        int goldI99 = RankLadder.ToPoints("GOLD", "I", 99);
        int platIV0 = RankLadder.ToPoints("PLATINUM", "IV", 0);
        Assert.True(goldIV0 < goldI99);
        Assert.True(goldI99 < platIV0);
    }

    [Fact]
    public void ToPoints_diamond_one_meets_master_zero()
    {
        Assert.Equal(2800, RankLadder.ToPoints("DIAMOND", "I", 100));
        Assert.Equal(2800, RankLadder.ToPoints("MASTER", "I", 0));
        Assert.Equal(3000, RankLadder.ToPoints("MASTER", "I", 200));
    }

    [Fact]
    public void Format_nonapex_and_apex()
    {
        Assert.Equal("Gold II · 47 LP", RankLadder.Format("GOLD", "II", 47));
        Assert.Equal("Master 312 LP", RankLadder.Format("MASTER", "I", 312));
    }
}
```

(The `·` in the expected string is U+00B7 — use the literal char.)

- [ ] **Step 2: Run to verify failure** — `dotnet test RiftReview.slnx --filter RankLadderTests`; Expected: FAIL.

- [ ] **Step 3: Create `RankLadder.cs`**

```csharp
using System;

namespace RiftReview.Core.Analysis;

// Maps a (tier, division, lp) rank to absolute "ladder points" for diffing, and to a display string.
// Tiers stack at 400 pts each (4 divisions × 100); apex tiers are one continuous pool above Diamond I.
public static class RankLadder
{
    private static readonly string[] Tiers =
        { "IRON", "BRONZE", "SILVER", "GOLD", "PLATINUM", "EMERALD", "DIAMOND" };
    private static readonly string[] Divisions = { "IV", "III", "II", "I" };

    public static int ToPoints(string tier, string division, int lp)
    {
        string t = (tier ?? "").ToUpperInvariant();
        int ti = Array.IndexOf(Tiers, t);
        if (ti >= 0)
        {
            int di = Array.IndexOf(Divisions, (division ?? "").ToUpperInvariant());
            if (di < 0) di = 0;
            return ti * 400 + di * 100 + lp;
        }
        return Tiers.Length * 400 + lp;   // apex (MASTER/GRANDMASTER/CHALLENGER): 2800 + lp
    }

    public static bool IsApex(string tier)
    {
        var t = (tier ?? "").ToUpperInvariant();
        return t is "MASTER" or "GRANDMASTER" or "CHALLENGER";
    }

    public static string Format(string tier, string division, int lp) =>
        IsApex(tier) ? $"{Cap(tier)} {lp} LP" : $"{Cap(tier)} {division} · {lp} LP";

    public static string Cap(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..].ToLowerInvariant();
}
```

- [ ] **Step 4: Run to verify pass** — `dotnet test RiftReview.slnx --filter RankLadderTests`; Expected: PASS (3).

- [ ] **Step 5: DRY-refactor `TrendsViewModel`** — READ `TrendsViewModel.cs`. Replace the LP-headline line:
  ```csharp
  LpHeadline = $"{Cap(latest.Tier)} {latest.Division} · {latest.LeaguePoints} LP";
  ```
  with:
  ```csharp
  LpHeadline = RankLadder.Format(latest.Tier, latest.Division, latest.LeaguePoints);
  ```
  Then DELETE the now-unused private `Cap()` method (confirm it has no other callers in the file first; if it does, leave it). Ensure `using RiftReview.Core.Analysis;` is present (it already is — `ChampTrendCalculator` is used). Run `dotnet test RiftReview.slnx --filter TrendsViewModelTests` — the existing LP-headline assertion (if any) must still pass because `Format` yields the identical "Gold II · 47 LP" for non-apex.

- [ ] **Step 6: Build + full test + commit** — `dotnet build RiftReview.slnx` (0 warnings); `dotnet test RiftReview.slnx` (all green).

```bash
git add src/RiftReview.Core/Analysis/RankLadder.cs src/RiftReview.App/ViewModels/TrendsViewModel.cs tests/RiftReview.Core.Tests/RankLadderTests.cs
git commit -m "feat(core): RankLadder (rank<->ladder points + format); reuse in TrendsViewModel

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 2: `ClimbModels` + `ClimbCalculator` (pure)

**Files:**
- Create: `src/RiftReview.Core/Analysis/ClimbModels.cs`
- Create: `src/RiftReview.Core/Analysis/ClimbCalculator.cs`
- Test: `tests/RiftReview.Core.Tests/ClimbCalculatorTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using RiftReview.Core.Analysis;
using RiftReview.Core.Data;
using Xunit;

public class ClimbCalculatorTests
{
    private static MatchRow R(long start, bool win, int queue = 420) =>
        new("NA1_" + start, queue, start, 1800, "15.12", 103, "MIDDLE", win,
            5, 3, 7, 200, 70, 100, 8, 145, 100, 0.6, 0.25, 1);

    [Fact]
    public void Streaks_current_and_longest()
    {
        var ms = new List<MatchRow> { R(0, true), R(1, true), R(2, false), R(3, true), R(4, true), R(5, true) };
        var s = ClimbCalculator.Streaks(ms);
        Assert.Equal(3, s.CurrentStreak);
        Assert.Equal(3, s.LongestWinStreak);
        Assert.Equal(1, s.LongestLossStreak);
        Assert.Equal(6, s.RecentForm.Count);
        Assert.True(s.RecentForm[^1]);   // newest is a win
    }

    [Fact]
    public void Streaks_current_negative_on_loss_streak()
    {
        var ms = new List<MatchRow> { R(0, true), R(1, false), R(2, false) };
        Assert.Equal(-2, ClimbCalculator.Streaks(ms).CurrentStreak);
    }

    [Fact]
    public void Streaks_empty()
    {
        var s = ClimbCalculator.Streaks(new List<MatchRow>());
        Assert.Equal(0, s.CurrentStreak);
        Assert.Empty(s.RecentForm);
    }

    [Fact]
    public void Segments_net_lp_and_games_in_window()
    {
        var snaps = new List<LpSnapshot>
        {
            new(100, "RANKED_SOLO_5x5", "GOLD", "IV", 30, 10, 8),
            new(200, "RANKED_SOLO_5x5", "GOLD", "III", 10, 13, 9),   // Gold IV 30 -> Gold III 10
        };
        var ms = new List<MatchRow> { R(150, true, 420), R(180, true, 420), R(180, true, 440) };
        var segs = ClimbCalculator.Segments(snaps, ms, "RANKED_SOLO_5x5");
        Assert.Single(segs);
        Assert.Equal(2, segs[0].GamesInWindow);    // only the 2 solo games in (100,200]
        Assert.Equal(80, segs[0].NetLp);           // 1310 - 1230
    }

    [Fact]
    public void Segments_need_two_snapshots()
    {
        var snaps = new List<LpSnapshot> { new(100, "RANKED_SOLO_5x5", "GOLD", "IV", 30, 10, 8) };
        Assert.Empty(ClimbCalculator.Segments(snaps, new List<MatchRow>(), "RANKED_SOLO_5x5"));
    }
}
```

- [ ] **Step 2: Run to verify failure** — `dotnet test RiftReview.slnx --filter ClimbCalculatorTests`; Expected: FAIL.

- [ ] **Step 3: Create `ClimbModels.cs`**

```csharp
namespace RiftReview.Core.Analysis;

public sealed record StreakSummary(
    int CurrentStreak,                 // signed: +3 = 3 wins, -2 = 2 losses, 0 = none
    int LongestWinStreak,
    int LongestLossStreak,
    IReadOnlyList<bool> RecentForm);   // last N ranked games, oldest -> newest (true = win)

public sealed record LpSegment(
    string QueueType,
    long FromUtc, long ToUtc,
    int FromPoints, int ToPoints, int NetLp,
    int GamesInWindow,
    string FromLabel, string ToLabel);
```

- [ ] **Step 4: Create `ClimbCalculator.cs`**

```csharp
using RiftReview.Core.Data;

namespace RiftReview.Core.Analysis;

// Pure: ranked momentum (streaks) from matches + LP segments by pairing snapshot deltas
// with the ranked games in each time window. No DB, no I/O, deterministic.
public static class ClimbCalculator
{
    public static StreakSummary Streaks(IReadOnlyList<MatchRow> rankedMatches, int recentFormCount = 10)
    {
        var ordered = rankedMatches.OrderBy(m => m.GameStartUtc).ToList();
        if (ordered.Count == 0) return new StreakSummary(0, 0, 0, Array.Empty<bool>());

        int longestW = 0, longestL = 0, runW = 0, runL = 0;
        foreach (var m in ordered)
        {
            if (m.Win) { runW++; runL = 0; longestW = Math.Max(longestW, runW); }
            else       { runL++; runW = 0; longestL = Math.Max(longestL, runL); }
        }

        bool lastWin = ordered[^1].Win;
        int run = 0;
        for (int i = ordered.Count - 1; i >= 0 && ordered[i].Win == lastWin; i--) run++;
        int current = lastWin ? run : -run;

        var form = ordered.Skip(Math.Max(0, ordered.Count - recentFormCount)).Select(m => m.Win).ToList();
        return new StreakSummary(current, longestW, longestL, form);
    }

    public static IReadOnlyList<LpSegment> Segments(
        IReadOnlyList<LpSnapshot> snapshots, IReadOnlyList<MatchRow> rankedMatches, string queueType)
    {
        int qid = QueueIdFor(queueType);
        var snaps = snapshots.Where(s => s.QueueType == queueType).OrderBy(s => s.TakenUtc).ToList();
        var segs = new List<LpSegment>();
        for (int i = 1; i < snaps.Count; i++)
        {
            var p = snaps[i - 1]; var c = snaps[i];
            int fp = RankLadder.ToPoints(p.Tier, p.Division, p.LeaguePoints);
            int cp = RankLadder.ToPoints(c.Tier, c.Division, c.LeaguePoints);
            int games = rankedMatches.Count(m => m.QueueId == qid
                && m.GameStartUtc > p.TakenUtc && m.GameStartUtc <= c.TakenUtc);
            segs.Add(new LpSegment(queueType, p.TakenUtc, c.TakenUtc, fp, cp, cp - fp, games,
                RankLadder.Format(p.Tier, p.Division, p.LeaguePoints),
                RankLadder.Format(c.Tier, c.Division, c.LeaguePoints)));
        }
        segs.Reverse();   // newest first
        return segs;
    }

    public static int QueueIdFor(string queueType) => queueType switch
    {
        "RANKED_SOLO_5x5" => 420,
        "RANKED_FLEX_SR" => 440,
        _ => -1,
    };
}
```

- [ ] **Step 5: Run to verify pass** — `dotnet test RiftReview.slnx --filter ClimbCalculatorTests`; Expected: PASS (5). Then `dotnet test RiftReview.slnx` (full).

- [ ] **Step 6: Commit**

```bash
git add src/RiftReview.Core/Analysis/ClimbModels.cs src/RiftReview.Core/Analysis/ClimbCalculator.cs tests/RiftReview.Core.Tests/ClimbCalculatorTests.cs
git commit -m "feat(core): ClimbCalculator — ranked streaks + net-LP segments between snapshots

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 3: `ClimbViewModel` + display wrappers

**Files:**
- Create: `src/RiftReview.App/ViewModels/ClimbViewModel.cs` (includes `StandingViewModel`, `FormPip`, `LpSegmentViewModel`)
- Test: `tests/RiftReview.App.Tests/ClimbViewModelTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System;
using System.Linq;
using RiftReview.App.ViewModels;
using RiftReview.Core.Data;
using RiftReview.Core.DataDragon;
using Xunit;

public class ClimbViewModelTests
{
    private static DataDragonClient Dd() => new(new System.Net.Http.HttpClient(), System.IO.Path.GetTempPath());

    private static MatchRow R(long start, bool win, int queue = 420) =>
        new("NA1_" + start, queue, start, 1800, "15.12", 103, "MIDDLE", win,
            5, 3, 7, 200, 70, 100, 8, 145, 100, 0.6, 0.25, 1);

    [Fact]
    public void Load_builds_standing_streak_and_segments()
    {
        using var db = RiftReviewDb.Open("Data Source=:memory:");
        db.SetMeta("puuid", "ME");
        db.UpsertMatch(R(100, true), "{}", "{}");
        db.UpsertMatch(R(150, true), "{}", "{}");
        db.UpsertMatch(R(180, false), "{}", "{}");
        db.InsertLpSnapshot(new LpSnapshot(90,  "RANKED_SOLO_5x5", "GOLD", "IV", 30, 1, 0));
        db.InsertLpSnapshot(new LpSnapshot(200, "RANKED_SOLO_5x5", "GOLD", "III", 10, 2, 1));

        var vm = new ClimbViewModel(db, Dd());
        vm.Load();

        Assert.NotNull(vm.Solo);
        Assert.Equal("Gold III · 10 LP", vm.Solo!.RankText);   // latest snapshot
        Assert.True(vm.HasAnyStanding);
        Assert.Single(vm.SoloSegments);
        Assert.True(vm.HasSoloSegments);
        Assert.Equal(3, vm.RecentForm.Count);
        Assert.False(vm.IsEmpty);
    }

    [Fact]
    public void Empty_db_is_empty()
    {
        using var db = RiftReviewDb.Open("Data Source=:memory:");
        var vm = new ClimbViewModel(db, Dd());
        vm.Load();
        Assert.True(vm.IsEmpty);
        Assert.False(vm.HasAnyStanding);
    }
}
```

- [ ] **Step 2: Run to verify failure** — `dotnet test RiftReview.slnx --filter ClimbViewModelTests`; Expected: FAIL.

- [ ] **Step 3: Create `ClimbViewModel.cs`** (VM + the three small wrappers in the same file)

```csharp
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using RiftReview.Core.Analysis;
using RiftReview.Core.Data;
using RiftReview.Core.DataDragon;

namespace RiftReview.App.ViewModels;

public sealed class StandingViewModel
{
    public StandingViewModel(string queueLabel, LpSnapshot s)
    {
        QueueLabel = queueLabel;
        RankText = RankLadder.Format(s.Tier, s.Division, s.LeaguePoints);
        int total = s.Wins + s.Losses;
        SeasonRecord = $"{s.Wins}W / {s.Losses}L";
        WinRateText = total > 0 ? $"{Math.Round(100.0 * s.Wins / total)}%" : "—";
    }
    public string QueueLabel { get; }
    public string RankText { get; }
    public string SeasonRecord { get; }
    public string WinRateText { get; }
}

public sealed class FormPip { public bool Win { get; init; } }

public sealed class LpSegmentViewModel
{
    public LpSegmentViewModel(LpSegment s)
    {
        WhenRange = $"{Local(s.FromUtc)} → {Local(s.ToUtc)}";
        GamesLabel = s.GamesInWindow == 1 ? "1 game" : $"{s.GamesInWindow} games";
        NetLpText = $"{(s.NetLp >= 0 ? "+" : "−")}{Math.Abs(s.NetLp)} LP";
        IsGain = s.NetLp >= 0;
        RankRange = $"{s.FromLabel} → {s.ToLabel}";
    }
    private static string Local(long utc) => DateTimeOffset.FromUnixTimeSeconds(utc).LocalDateTime.ToString("MMM d");
    public string WhenRange { get; }
    public string GamesLabel { get; }
    public string NetLpText { get; }
    public bool IsGain { get; }
    public string RankRange { get; }
}

public sealed partial class ClimbViewModel : ObservableObject
{
    private readonly RiftReviewDb _db;
    private readonly DataDragonClient _ddragon;

    public ClimbViewModel(RiftReviewDb db, DataDragonClient ddragon)
    {
        _db = db; _ddragon = ddragon;
    }

    [ObservableProperty] private StandingViewModel? _solo;
    [ObservableProperty] private StandingViewModel? _flex;
    [ObservableProperty] private bool _hasAnyStanding;

    [ObservableProperty] private string _streakText = "";
    [ObservableProperty] private bool _streakIsPositive;
    [ObservableProperty] private string _longestStreaksText = "";

    public ObservableCollection<FormPip> RecentForm { get; } = new();
    public ObservableCollection<LpSegmentViewModel> SoloSegments { get; } = new();
    [ObservableProperty] private bool _hasSoloSegments;
    [ObservableProperty] private string _lpHistoryNote = "";
    [ObservableProperty] private bool _isEmpty;

    public async Task InitializeAsync()
    {
        try { await _ddragon.EnsureLoadedAsync(); } catch { /* offline — names not needed here */ }
        Load();
    }

    public void Load()
    {
        var snaps = _db.GetLpSnapshots();
        var ranked = _db.AllMatches(rankedOnly: true);

        var soloSnap = snaps.Where(s => s.QueueType == "RANKED_SOLO_5x5").OrderByDescending(s => s.TakenUtc).FirstOrDefault();
        var flexSnap = snaps.Where(s => s.QueueType == "RANKED_FLEX_SR").OrderByDescending(s => s.TakenUtc).FirstOrDefault();
        Solo = soloSnap is null ? null : new StandingViewModel("Ranked Solo/Duo", soloSnap);
        Flex = flexSnap is null ? null : new StandingViewModel("Ranked Flex", flexSnap);
        HasAnyStanding = Solo is not null || Flex is not null;

        var streak = ClimbCalculator.Streaks(ranked);
        StreakIsPositive = streak.CurrentStreak > 0;
        StreakText = streak.CurrentStreak switch
        {
            0   => ranked.Count == 0 ? "No ranked games yet" : "No active streak",
            > 0 => $"On a {streak.CurrentStreak}-win streak",
            _   => $"{-streak.CurrentStreak}-loss skid",
        };
        LongestStreaksText = $"Best: {streak.LongestWinStreak}W · Worst: {streak.LongestLossStreak}L";
        RecentForm.Clear();
        foreach (var w in streak.RecentForm) RecentForm.Add(new FormPip { Win = w });

        SoloSegments.Clear();
        foreach (var seg in ClimbCalculator.Segments(snaps, ranked, "RANKED_SOLO_5x5"))
            SoloSegments.Add(new LpSegmentViewModel(seg));
        HasSoloSegments = SoloSegments.Count > 0;
        LpHistoryNote = "LP history fills in as you sync — Riot doesn't expose past per-game LP.";

        IsEmpty = snaps.Count == 0 && ranked.Count == 0;
    }
}
```

- [ ] **Step 4: Run to verify pass** — `dotnet test RiftReview.slnx --filter ClimbViewModelTests`; Expected: PASS (2). Then `dotnet test RiftReview.slnx` (full).

- [ ] **Step 5: Commit**

```bash
git add src/RiftReview.App/ViewModels/ClimbViewModel.cs tests/RiftReview.App.Tests/ClimbViewModelTests.cs
git commit -m "feat(app): ClimbViewModel — standings, ranked momentum, LP-segment ledger

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 4: `ClimbView` (page) + nav + DI

**Files:**
- Create: `src/RiftReview.App/Views/ClimbView.xaml` (+ `.xaml.cs`)
- Modify: `src/RiftReview.App/AppShell.xaml` (nav) + `AppShell.xaml.cs` (`--page`)
- Modify: `src/RiftReview.App/App.xaml.cs` (DI)

- [ ] **Step 1: `ClimbView.xaml`** — READ `TrendsView.xaml` + `SessionHealthView.xaml` first for exact idioms. Root `Grid Background="{StaticResource PanelBgBrush}"` (header Auto + body `ScrollViewer`). `UserControl.Resources`: `<BooleanToVisibilityConverter x:Key="BoolToVis"/>` + `conv:BoolToBrushConverter x:Key="WinResultBrush" TrueBrush="{StaticResource WinBrush}" FalseBrush="{StaticResource LossBrush}"/>` (xmlns `conv="clr-namespace:RiftReview.App.Converters"`). Body (vertical StackPanel):
  - Gold "Climb" title (`AccentBrush`, 18, SemiBold).
  - **Standing cards row** (a horizontal `StackPanel` or 2-col `Grid`): two `Border Background="{StaticResource CardBgBrush}"` CornerRadius 6 Padding 16 tiles —
    - Solo tile (`DataContext="{Binding Solo}"`, collapse via self-null `Style` DataTrigger `{Binding}` Value `{x:Null}`): `QueueLabel` (muted small), `RankText` (`AccentBrush`, FontSize 20, SemiBold), `SeasonRecord` + " · " + `WinRateText` (muted).
    - Flex tile (`DataContext="{Binding Flex}"`, same).
    - A muted fallback `TextBlock` "No ranked standing yet — sync while ranked." visible when `HasAnyStanding` is false (`Visibility` via a bool→vis; since BoolToVis maps true→Visible, bind to a `HasAnyStanding` inverse — simplest: bind the fallback's `Visibility` to `HasAnyStanding` with a DataTrigger setting Collapsed when true, OR add the fallback and a DataTrigger on `HasAnyStanding==False`→Visible. Use a `Style` DataTrigger).
  - **Ranked momentum tile** (`Border CardBgBrush`): `StreakText` (FontSize 16, SemiBold; `Foreground` via `Style` DataTrigger on `StreakIsPositive` True→`WinBrush`, else `LossBrush`), `LongestStreaksText` (muted), and a **recent-form strip**: an `ItemsControl ItemsSource="{Binding RecentForm}"` with a horizontal `StackPanel` ItemsPanel; item template = a `Border` Width 14 Height 14 CornerRadius 3 Margin 2, `Background="{Binding Win, Converter={StaticResource WinResultBrush}}"`.
  - **LP history section:** muted "LP HISTORY — RANKED SOLO" header; `LpHistoryNote` (TextMutedBrush, FontStyle Italic, FontSize 11, wrap); then an `ItemsControl ItemsSource="{Binding SoloSegments}"` — each row a `Grid` (2 cols) with left = `WhenRange` (TextPrimary) over `RankRange` (muted small), right = `NetLpText` (FontSize 14, SemiBold; `Foreground` via `Style` DataTrigger on `IsGain` True→`WinBrush` else `LossBrush`); 1px `HairlineBrush` bottom separator. When `HasSoloSegments` is false, a muted "Only one snapshot so far — your next sync starts the ledger." (DataTrigger on `HasSoloSegments==False`).
  - **Empty state** (`Visibility="{Binding IsEmpty, Converter={StaticResource BoolToVis}}"`): centered muted "Sync while ranked to start tracking your climb."

- [ ] **Step 2: `ClimbView.xaml.cs`**

```csharp
using System.Windows.Controls;
using RiftReview.App.ViewModels;

namespace RiftReview.App.Views;

public partial class ClimbView : UserControl
{
    public ClimbView(ClimbViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Loaded += async (_, _) => await vm.InitializeAsync();
    }
}
```

- [ ] **Step 3: Nav** — in `AppShell.xaml`, add after the "Session Health" `NavigationViewItem`:

```xml
<ui:NavigationViewItem Content="Climb"
                       TargetPageType="{x:Type views:ClimbView}">
    <ui:NavigationViewItem.Icon>
        <ui:SymbolIcon Symbol="ArrowTrendingLines24"/>
    </ui:NavigationViewItem.Icon>
</ui:NavigationViewItem>
```

If `ArrowTrendingLines24` fails to compile, try `ChartMultiple24`, then `Trophy24`, then `DataTrending24`. In `AppShell.xaml.cs`, add `"climb" => typeof(ClimbView),` to the `--page` switch.

- [ ] **Step 4: DI** — in `App.xaml.cs` (beside the other transient pages):

```csharp
                s.AddTransient<ViewModels.ClimbViewModel>();
                s.AddTransient<ClimbView>();
```

- [ ] **Step 5: Build + smoke** — `dotnet build RiftReview.slnx -c Debug` (0 warnings; swap the symbol if needed). Then `dotnet test RiftReview.slnx` (full, green).

- [ ] **Step 6: Commit**

```bash
git add src/RiftReview.App/Views/ClimbView.xaml src/RiftReview.App/Views/ClimbView.xaml.cs src/RiftReview.App/AppShell.xaml src/RiftReview.App/AppShell.xaml.cs src/RiftReview.App/App.xaml.cs
git commit -m "feat(app): Climb page + nav + DI

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 5: Demo seeder — LP snapshots

**Files:**
- Modify: `src/RiftReview.App/Demo/DemoSeeder.cs`

The Climb page needs `lp_snapshots` to render standings + a segment ledger. READ `DemoSeeder.cs` first — confirm it currently inserts NO snapshots (M2 added the LP strip but the demo may rely on no snapshots / a single one). The demo's ranked games (queue 420) span `game_start_utc` values; place snapshot `taken_utc`s interleaved so each segment window contains games.

- [ ] **Step 1: Insert a climbing series of Solo snapshots (+ a couple Flex).** After seeding the matches (so the `game_start_utc`s exist), add ~4 Solo snapshots showing a climb and 2 Flex, with `taken_utc` interleaved among the demo Solo `game_start_utc`s so `GamesInWindow` > 0 per segment. Find the demo's `game_start_utc` range (recall the seeder spaces games ~1 day apart and stores `MatchRow.GameStartUtc` in **seconds**). Example (adapt the timestamps to fall *after* clusters of demo games):

```csharp
        // LP snapshots for the Climb page — a Gold IV -> Gold I climb across the demo's timeline.
        long t0 = /* earliest demo game_start_utc */;
        long day = 86_400L;
        db.InsertLpSnapshot(new LpSnapshot(t0 + 2*day,  "RANKED_SOLO_5x5", "GOLD", "IV", 30, 60, 55));
        db.InsertLpSnapshot(new LpSnapshot(t0 + 8*day,  "RANKED_SOLO_5x5", "GOLD", "III", 60, 66, 58));
        db.InsertLpSnapshot(new LpSnapshot(t0 + 15*day, "RANKED_SOLO_5x5", "GOLD", "II", 12, 72, 61));
        db.InsertLpSnapshot(new LpSnapshot(t0 + 23*day, "RANKED_SOLO_5x5", "GOLD", "I", 75, 79, 64));
        db.InsertLpSnapshot(new LpSnapshot(t0 + 10*day, "RANKED_FLEX_SR",  "SILVER", "I", 40, 20, 15));
        db.InsertLpSnapshot(new LpSnapshot(t0 + 22*day, "RANKED_FLEX_SR",  "GOLD", "IV", 5, 24, 18));
```

  Adapt `t0` and the offsets so the snapshot `taken_utc`s actually straddle demo Solo games (so segments report non-zero `GamesInWindow`). The exact climb values are illustrative — keep it a clear upward Solo climb (Gold IV → Gold I) so the segment ledger shows green gains.

- [ ] **Step 2: Build + smoke** — `dotnet build RiftReview.slnx -c Debug` (clean). Optionally `RiftReview.App.exe --seed-demo --page climb` to eyeball standings + a green-gain segment ledger. Then `dotnet test RiftReview.slnx` (full).

- [ ] **Step 3: Commit**

```bash
git add src/RiftReview.App/Demo/DemoSeeder.cs
git commit -m "feat(app): demo seeder adds LP snapshots so the Climb page renders

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 6: Screenshot verification gate (real + demo)

**Files:** none. Reuse `.m5shots/` (create) + `.m2shots/capture.ps1`.

- [ ] **Step 1: Build Debug** — clean.

- [ ] **Step 2: Verify (Sonnet subagent, text verdict only, PNGs NOT loaded to main ctx; HW-accel-off registry set+restored, PrintWindow flag 2):**
  - **Demo** `--seed-demo --page climb`: standings cards (Solo Gold I, Flex) with rank + season record + win%; ranked-momentum tile (streak text + colored, best/worst, recent-form pip strip); LP HISTORY ledger with multiple segments showing **green LP gains** (Gold IV→Gold I climb) + the honesty note; no crash.
  - **Real DB** `--page climb`: standings from his real latest snapshot (the M2 screenshot showed Diamond I · 49 LP — expect a real rank); ranked-momentum from his 408 ranked games; LP ledger from his real (sparse) snapshots — may be a single segment or the "only one snapshot" message; report exactly what renders. Confirm the honesty note shows.
  - Nav rail shows Review / Champions / Trends / Matchups / Session Health / **Climb** / Settings; switching works; no crash.
- [ ] **Step 3: Restore registry; report PASS/FAIL + PNG paths.** Fix any defect in its own commit.

---

## Acceptance criteria (from spec §9)

- `dotnet build` clean (0 warnings); `dotnet test` all green incl. new `RankLadder` + `ClimbCalculator` + `ClimbViewModel` tests.
- No schema change / migration / Riot calls / SyncService change.
- Climb page: current Solo + Flex standing (or clear no-data state); ranked momentum (streak + recent-form strip) from full ranked history; LP-segment ledger (net LP per window, colored, games-in-window) + honesty note.
- `RankLadder.ToPoints` monotonic; `Format` matches the "Gold II · 47 LP" idiom + apex; `TrendsViewModel` reuses `RankLadder.Format`.
- Demo renders standings + a multi-segment green-gain ledger; screenshot-verified (real + demo).
- No secrets; `appsettings.json` placeholders only.

## Hand-back

- LP segments are coarse today (sparse snapshots) — they sharpen as you sync over real time; confirm your next real sync adds a snapshot + a new segment.
- Past per-game LP is unrecoverable (Riot limitation) — confirm the honesty note reads right.
- Apex ladder math (Master+) modeled as a continuous pool above Diamond I — verify if you climb there.
