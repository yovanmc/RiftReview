# RiftReview M4 — Tilt Guard / Session Health — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A reflective tilt guard — a **Session Health** page that groups the owner's games into play sessions and flags tilt (loss streaks + in-session performance decay), plus a **warning banner** on other pages when the latest session is on tilt.

**Architecture:** A pure `SessionCalculator` groups `MatchRow`s into time-gapped sessions and computes loss-streak + first-vs-second-half decay + a `TiltSeverity` verdict — **no schema change, no migration, no Riot calls**. A **singleton** `SessionHealthViewModel` (shared by the page and the AppShell banner) renders sessions newest-first and drives the banner. Mirrors the `ChampTrendCalculator`/`TrendsViewModel` and `MainViewModel`-singleton patterns.

**Tech Stack:** C#/.NET 10, WPF + WPF-UI 4.3, Microsoft.Data.Sqlite, CommunityToolkit.Mvvm, xUnit.

**Design spec:** `docs/superpowers/specs/2026-06-19-riftreview-m4-design.md`.

---

## Build-time facts (from a fresh dossier — trust these)

- `MatchRow` (`src/RiftReview.Core/Data/MatchRow.cs`) positional order: `MatchId, QueueId, GameStartUtc(long, UNIX SECONDS), DurationS, Patch, MyChampionId, MyTeamPosition, Win, Kills, Deaths, Assists, Cs, CsAt10(int?), GoldDiffAt15(int?), OpponentParticipantId(int?), OpponentChampionId(int?), SyncedAt, KillParticipation(double?)=null, DamageShare(double?)=null, DeathsPre15(int?)=null`.
- `GameStartUtc` is **unix seconds**; a game ends at `GameStartUtc + DurationS`.
- `RiftReviewDb.AllMatches(bool rankedOnly)` returns all stored matches; `LatestSchemaVersion = 3` (NO change in M4).
- App test classes have **NO namespace**; in-memory DB via `RiftReviewDb.Open("Data Source=:memory:")`; `DataDragonClient` test ctor = `new(new HttpClient(), Path.GetTempPath())` (names fall back to placeholders). `db.UpsertMatch(row, "{}", "{}")` seeds a row with no-op blobs.
- Theme brushes (`Themes/Colors.xaml`): `WindowBgBrush #0A0A0C`, `PanelBgBrush #16161A`, `CardBgBrush #1E1E24`, `HairlineBrush #2A2A30`, `AccentBrush #C8AA6E` (gold), `TextPrimaryBrush #ECECEC`, `TextMutedBrush #9A9A9A`, `WinBrush #4CC38A` (green), `LossBrush #E24A4A` (red). NO warning brush — use `AccentBrush` (gold) for Caution.
- Converters: only custom one is `conv:BoolToBrushConverter` (`Converters/BoolToBrushConverter.cs`, props `TrueBrush`/`FalseBrush`); `BooleanToVisibilityConverter` is the WPF built-in, declared inline keyed `BoolToVis`. (For 3-state severity coloring use XAML **DataTriggers** on `IsTilted`/`IsCaution`/`IsCalm` bools — do NOT add a new converter.)
- `AppShell.xaml` root is `<Grid><ui:NavigationView x:Name="RootNavigation">…</ui:NavigationView></Grid>`; xmlns `ui` + `views`; menu items end after "Matchups"; Settings is a footer item. `AppShell.xaml.cs` ctor is `AppShell(IServiceProvider sp, NavigationService nav)` with an `OnLoaded` `--page` switch.
- `App.xaml.cs`: pages registered as paired `AddTransient<ViewModels.XxxViewModel>()` + `AddTransient<XxxView>()`; `MainViewModel` is `AddSingleton`. `RootNavigation.Navigate(Type, object?)` is the nav API.
- Commit author is repo default `yovanmc`; **plain `git commit`, never `--author`**; append `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>` to every commit. 0 build warnings is a hard gate.

---

## Task 1: `SessionModels` + `SessionCalculator` (pure)

**Files:**
- Create: `src/RiftReview.Core/Analysis/SessionModels.cs`
- Create: `src/RiftReview.Core/Analysis/SessionCalculator.cs`
- Test: `tests/RiftReview.Core.Tests/SessionCalculatorTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using RiftReview.Core.Analysis;
using RiftReview.Core.Data;
using Xunit;

public class SessionCalculatorTests
{
    // helper: a game at unix-second `start`, fixed 1800s duration, given win + deaths + cs10.
    private static MatchRow G(long start, bool win, int deaths = 3, int? cs10 = 70) =>
        new("NA1_" + start, 420, start, 1800, "15.12", 103, "MIDDLE", win,
            5, deaths, 7, 200, cs10, 100, 8, 145, 100, 0.6, 0.25, 1);

    private const long H = 3600;

    [Fact]
    public void Groups_games_into_sessions_by_time_gap()
    {
        // 3 games ~40min apart = one session; then a 5h gap; then 2 games = second session.
        var games = new List<MatchRow>
        {
            G(0, true), G(2400, true), G(4800, false),          // session A (gaps ~40min)
            G(4800 + 5*H, false), G(4800 + 5*H + 2400, false),  // session B after 5h gap
        };
        var sessions = SessionCalculator.BuildSessions(games);
        Assert.Equal(2, sessions.Count);
        // newest first
        Assert.Equal(2, sessions[0].Games);
        Assert.Equal(3, sessions[1].Games);
    }

    [Fact]
    public void Detects_trailing_loss_streak_and_marks_tilted()
    {
        // one session, ends on 4 straight losses
        var games = new List<MatchRow>
        {
            G(0, true), G(2400, false), G(4800, false), G(7200, false), G(9600, false),
        };
        var s = SessionCalculator.BuildSessions(games)[0];
        Assert.Equal(4, s.EndLossStreak);
        Assert.Equal(4, s.LongestLossStreak);
        Assert.Equal(TiltSeverity.Tilted, s.Severity);
        Assert.Contains(s.Reasons, r => r.Contains("skid"));
    }

    [Fact]
    public void Detects_in_session_deaths_decay()
    {
        // 4 games, deaths climb 1,2 -> 7,9 across halves; mixed enough W/L to avoid streak-driven tilt
        var games = new List<MatchRow>
        {
            G(0, true, deaths: 1), G(2400, false, deaths: 2),
            G(4800, true, deaths: 7), G(7200, false, deaths: 9),
        };
        var s = SessionCalculator.BuildSessions(games)[0];
        Assert.True(s.DecayPresent);
        Assert.True(s.DeathsDelta > 0);
        Assert.True(s.Severity >= TiltSeverity.Caution);   // enum order Calm<Caution<Tilted
        Assert.Contains(s.Reasons, r => r.Contains("deaths", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Null_cs10_does_not_crash_and_cs_delta_is_null()
    {
        var games = new List<MatchRow>
        {
            G(0, true, cs10: null), G(2400, true, cs10: null), G(4800, true, cs10: null),
        };
        var s = SessionCalculator.BuildSessions(games)[0];
        Assert.Null(s.Cs10Delta);
    }

    [Fact]
    public void All_wins_is_calm()
    {
        var games = new List<MatchRow> { G(0, true), G(2400, true), G(4800, true) };
        var s = SessionCalculator.BuildSessions(games)[0];
        Assert.Equal(TiltSeverity.Calm, s.Severity);
    }

    [Fact]
    public void Empty_input_is_empty()
    {
        Assert.Empty(SessionCalculator.BuildSessions(new List<MatchRow>()));
    }
}
```

- [ ] **Step 2: Run to verify failure** — `dotnet test RiftReview.slnx --filter SessionCalculatorTests`; Expected: FAIL (types missing).

- [ ] **Step 3: Create `SessionModels.cs`**

```csharp
using RiftReview.Core.Data;

namespace RiftReview.Core.Analysis;

public enum TiltSeverity { Calm, Caution, Tilted }

// One play session: a run of consecutive games with small idle gaps, plus tilt analysis.
public sealed record PlaySession(
    long StartUtc,
    long EndUtc,
    int Games,
    int Wins,
    int Losses,
    int LongestLossStreak,
    int EndLossStreak,
    int EndWinStreak,
    double? DeathsDelta,   // 2nd-half avg deaths minus 1st-half (positive = climbing/worse)
    double? Cs10Delta,    // 1st-half avg cs@10 minus 2nd-half (positive = falling/worse)
    double? KdaDelta,     // 1st-half avg kda minus 2nd-half (positive = falling/worse)
    bool DecayPresent,
    TiltSeverity Severity,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<MatchRow> GamesList);   // chronological, oldest -> newest
```

- [ ] **Step 4: Create `SessionCalculator.cs`**

```csharp
using RiftReview.Core.Data;

namespace RiftReview.Core.Analysis;

// Pure: group a player's games into time-gapped sessions and score each for tilt.
// No DB, no I/O, deterministic. Reads only MatchRow fields (null-guards the derived scalars).
public static class SessionCalculator
{
    public const int    SessionGapSeconds     = 3 * 3600;  // >3h idle => new session
    public const int    MinGamesForDecay       = 3;
    public const double DeathsDecayThreshold   = 1.5;
    public const int    Cs10DecayThreshold     = 8;
    public const double KdaDecayThreshold       = 1.5;
    public const int    TiltedEndStreak         = 3;
    public const int    LongStreakCaution       = 3;
    public const double CautionWinRate          = 0.40;
    public const int    CautionWinRateMinGames  = 4;

    public static IReadOnlyList<PlaySession> BuildSessions(IReadOnlyList<MatchRow> matches)
    {
        if (matches.Count == 0) return Array.Empty<PlaySession>();
        var ordered = matches.OrderBy(m => m.GameStartUtc).ToList();
        var sessions = new List<PlaySession>();
        var current = new List<MatchRow> { ordered[0] };
        for (int i = 1; i < ordered.Count; i++)
        {
            var prev = ordered[i - 1];
            long gap = ordered[i].GameStartUtc - (prev.GameStartUtc + prev.DurationS);
            if (gap <= SessionGapSeconds) current.Add(ordered[i]);
            else { sessions.Add(Build(current)); current = new List<MatchRow> { ordered[i] }; }
        }
        sessions.Add(Build(current));
        sessions.Reverse();   // newest session first for display
        return sessions;
    }

    private static PlaySession Build(List<MatchRow> games)
    {
        int n = games.Count;
        int wins = games.Count(g => g.Win);
        int losses = n - wins;

        int longest = 0, run = 0;
        foreach (var g in games) { if (!g.Win) { run++; longest = Math.Max(longest, run); } else run = 0; }
        int endLoss = 0; for (int i = n - 1; i >= 0 && !games[i].Win; i--) endLoss++;
        int endWin  = 0; for (int i = n - 1; i >= 0 &&  games[i].Win; i--) endWin++;

        double? deathsDelta = null, cs10Delta = null, kdaDelta = null;
        if (n >= MinGamesForDecay)
        {
            int half = n / 2;
            var first  = games.Take(half).ToList();
            var second = games.Skip(n - half).ToList();
            deathsDelta = Avg(second, g => g.Deaths) - Avg(first, g => g.Deaths);
            kdaDelta    = AvgKda(first) - AvgKda(second);
            double? cF = AvgN(first, g => g.CsAt10), cS = AvgN(second, g => g.CsAt10);
            cs10Delta   = (cF.HasValue && cS.HasValue) ? cF - cS : null;
        }

        bool deathsDecay = deathsDelta is double dd && dd >= DeathsDecayThreshold;
        bool csDecay     = cs10Delta  is double cd && cd >= Cs10DecayThreshold;
        bool kdaDecay    = kdaDelta    is double kd && kd >= KdaDecayThreshold;
        bool decay = deathsDecay || csDecay || kdaDecay;

        double wr = wins / (double)n;
        TiltSeverity sev =
            (endLoss >= TiltedEndStreak || (endLoss >= 2 && decay)) ? TiltSeverity.Tilted
          : (endLoss == 2 || longest >= LongStreakCaution || decay
             || (n >= CautionWinRateMinGames && wr < CautionWinRate)) ? TiltSeverity.Caution
          : TiltSeverity.Calm;

        var reasons = new List<string>();
        if (endLoss >= 2) reasons.Add($"{endLoss}-loss skid to close");
        else if (longest >= LongStreakCaution) reasons.Add($"{longest}-loss skid earlier");
        if (deathsDecay) reasons.Add("deaths climbing");
        if (csDecay)     reasons.Add("CS@10 falling");
        if (kdaDecay)    reasons.Add("KDA falling");
        if (n >= CautionWinRateMinGames && wr < CautionWinRate) reasons.Add($"{wins}/{n} in this session");
        if (reasons.Count == 0) reasons.Add(endWin >= 3 ? $"{endWin}-win heater" : "no tilt signals");

        return new PlaySession(
            StartUtc: games[0].GameStartUtc,
            EndUtc: games[^1].GameStartUtc + games[^1].DurationS,
            Games: n, Wins: wins, Losses: losses,
            LongestLossStreak: longest, EndLossStreak: endLoss, EndWinStreak: endWin,
            DeathsDelta: deathsDelta, Cs10Delta: cs10Delta, KdaDelta: kdaDelta,
            DecayPresent: decay, Severity: sev, Reasons: reasons, GamesList: games);
    }

    private static double Avg(IEnumerable<MatchRow> gs, Func<MatchRow, int> sel) => gs.Average(sel);
    private static double AvgKda(IEnumerable<MatchRow> gs) =>
        gs.Average(g => (g.Kills + g.Assists) / (double)Math.Max(1, g.Deaths));
    private static double? AvgN(IReadOnlyList<MatchRow> gs, Func<MatchRow, int?> sel)
    {
        var v = gs.Where(g => sel(g).HasValue).Select(g => (double)sel(g)!.Value).ToList();
        return v.Count == 0 ? (double?)null : v.Average();
    }
}
```

- [ ] **Step 5: Run to verify pass** — `dotnet test RiftReview.slnx --filter SessionCalculatorTests`; Expected: PASS (6). Then `dotnet test RiftReview.slnx` (full).

- [ ] **Step 6: Commit**

```bash
git add src/RiftReview.Core/Analysis/SessionModels.cs src/RiftReview.Core/Analysis/SessionCalculator.cs tests/RiftReview.Core.Tests/SessionCalculatorTests.cs
git commit -m "feat(core): SessionCalculator — group games into sessions + tilt scoring

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 2: `SessionRowViewModel` + `SessionHealthViewModel` (singleton)

**Files:**
- Create: `src/RiftReview.App/ViewModels/SessionRowViewModel.cs`
- Create: `src/RiftReview.App/ViewModels/SessionHealthViewModel.cs`
- Test: `tests/RiftReview.App.Tests/SessionHealthViewModelTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System;
using System.Linq;
using RiftReview.App.ViewModels;
using RiftReview.Core.Data;
using RiftReview.Core.DataDragon;
using Xunit;

public class SessionHealthViewModelTests
{
    private static DataDragonClient Dd() => new(new System.Net.Http.HttpClient(), System.IO.Path.GetTempPath());

    private static MatchRow G(long start, bool win, int deaths = 3) =>
        new("NA1_" + start, 420, start, 1800, "15.12", 103, "MIDDLE", win,
            5, deaths, 7, 200, 70, 100, 8, 145, 100, 0.6, 0.25, 1);

    [Fact]
    public void Builds_sessions_and_banner_when_latest_is_tilted()
    {
        using var db = RiftReviewDb.Open("Data Source=:memory:");
        db.SetMeta("puuid", "ME");
        // latest session = 4 losses to close (tilted)
        db.UpsertMatch(G(0, true), "{}", "{}");
        db.UpsertMatch(G(2400, false), "{}", "{}");
        db.UpsertMatch(G(4800, false), "{}", "{}");
        db.UpsertMatch(G(7200, false), "{}", "{}");
        db.UpsertMatch(G(9600, false), "{}", "{}");

        var vm = new SessionHealthViewModel(db, Dd());   // ctor calls Refresh()

        Assert.False(vm.IsEmpty);
        Assert.NotNull(vm.Latest);
        Assert.True(vm.BannerVisible);
        Assert.Equal(RiftReview.Core.Analysis.TiltSeverity.Tilted, vm.BannerSeverity);
        Assert.True(vm.Latest!.IsTilted);
    }

    [Fact]
    public void Calm_latest_hides_banner()
    {
        using var db = RiftReviewDb.Open("Data Source=:memory:");
        db.SetMeta("puuid", "ME");
        db.UpsertMatch(G(0, true), "{}", "{}");
        db.UpsertMatch(G(2400, true), "{}", "{}");
        db.UpsertMatch(G(4800, true), "{}", "{}");

        var vm = new SessionHealthViewModel(db, Dd());
        Assert.False(vm.BannerVisible);
        Assert.True(vm.Latest!.IsCalm);
    }

    [Fact]
    public void Empty_db_is_empty_no_banner()
    {
        using var db = RiftReviewDb.Open("Data Source=:memory:");
        var vm = new SessionHealthViewModel(db, Dd());
        Assert.True(vm.IsEmpty);
        Assert.False(vm.BannerVisible);
        Assert.Null(vm.Latest);
    }
}
```

- [ ] **Step 2: Run to verify failure** — `dotnet test RiftReview.slnx --filter SessionHealthViewModelTests`; Expected: FAIL.

- [ ] **Step 3: Create `SessionRowViewModel.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using RiftReview.Core.Analysis;

namespace RiftReview.App.ViewModels;

// Display wrapper over a PlaySession: labels, severity flags, reasons.
public sealed class SessionRowViewModel
{
    private readonly PlaySession _s;
    public SessionRowViewModel(PlaySession s)
    {
        _s = s;
        Games = s.GamesList.Select(g => new SessionGameViewModel(g)).ToList();
    }

    public string WhenLabel => DateTimeOffset.FromUnixTimeSeconds(_s.StartUtc).LocalDateTime.ToString("MMM d · h:mm tt");
    public string Record => $"{_s.Wins}–{_s.Losses}";          // en dash
    public string GamesLabel => _s.Games == 1 ? "1 game" : $"{_s.Games} games";
    public TiltSeverity Severity => _s.Severity;
    public bool IsTilted  => _s.Severity == TiltSeverity.Tilted;
    public bool IsCaution => _s.Severity == TiltSeverity.Caution;
    public bool IsCalm    => _s.Severity == TiltSeverity.Calm;
    public string SeverityLabel => _s.Severity switch
    {
        TiltSeverity.Tilted  => "Tilted",
        TiltSeverity.Caution => "Caution",
        _                    => "Calm",
    };
    public string ReasonsText => string.Join(" · ", _s.Reasons);   // middle dot
    public IReadOnlyList<SessionGameViewModel> Games { get; }
}

// Minimal per-game wrapper for the session detail list.
public sealed class SessionGameViewModel
{
    private readonly RiftReview.Core.Data.MatchRow _g;
    public SessionGameViewModel(RiftReview.Core.Data.MatchRow g) => _g = g;
    public bool Win => _g.Win;
    public string Result => _g.Win ? "Win" : "Loss";
    public string Kda => $"{_g.Kills}/{_g.Deaths}/{_g.Assists}";
    public string WhenLocal => DateTimeOffset.FromUnixTimeSeconds(_g.GameStartUtc).LocalDateTime.ToString("h:mm tt");
}
```

- [ ] **Step 4: Create `SessionHealthViewModel.cs`** (SINGLETON; ctor computes so the banner is correct at startup)

```csharp
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using RiftReview.Core.Analysis;
using RiftReview.Core.Data;
using RiftReview.Core.DataDragon;

namespace RiftReview.App.ViewModels;

public sealed partial class SessionHealthViewModel : ObservableObject
{
    private readonly RiftReviewDb _db;
    private readonly DataDragonClient _ddragon;

    public SessionHealthViewModel(RiftReviewDb db, DataDragonClient ddragon)
    {
        _db = db; _ddragon = ddragon;
        Refresh();   // compute now so the banner is right before the page is ever opened
    }

    public ObservableCollection<SessionRowViewModel> Sessions { get; } = new();
    [ObservableProperty] private SessionRowViewModel? _latest;
    [ObservableProperty] private bool _isEmpty;

    [ObservableProperty] private bool _bannerVisible;
    [ObservableProperty] private string _bannerHeadline = "";
    [ObservableProperty] private TiltSeverity _bannerSeverity;

    public async Task InitializeAsync()
    {
        try { await _ddragon.EnsureLoadedAsync(); } catch { /* offline — names not needed here */ }
        Refresh();
    }

    public void Refresh()
    {
        var sessions = SessionCalculator.BuildSessions(_db.AllMatches(rankedOnly: false));
        Sessions.Clear();
        foreach (var s in sessions) Sessions.Add(new SessionRowViewModel(s));
        Latest = Sessions.FirstOrDefault();
        IsEmpty = Sessions.Count == 0;

        if (Latest is { } l && l.Severity >= TiltSeverity.Caution)
        {
            BannerVisible = true;
            BannerSeverity = l.Severity;
            BannerHeadline = l.IsTilted
                ? $"Tilt check: {l.ReasonsText}. Consider stepping away."
                : $"Heads up: {l.ReasonsText}.";
        }
        else { BannerVisible = false; BannerSeverity = TiltSeverity.Calm; BannerHeadline = ""; }
    }
}
```

- [ ] **Step 5: Run to verify pass** — `dotnet test RiftReview.slnx --filter SessionHealthViewModelTests`; Expected: PASS (3). Then `dotnet test RiftReview.slnx` (full).

- [ ] **Step 6: Commit**

```bash
git add src/RiftReview.App/ViewModels/SessionRowViewModel.cs src/RiftReview.App/ViewModels/SessionHealthViewModel.cs tests/RiftReview.App.Tests/SessionHealthViewModelTests.cs
git commit -m "feat(app): SessionHealthViewModel (singleton) + session row wrappers + banner state

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 3: `SessionHealthView` (page) + nav + DI

**Files:**
- Create: `src/RiftReview.App/Views/SessionHealthView.xaml` (+ `.xaml.cs`)
- Modify: `src/RiftReview.App/AppShell.xaml` (nav item) + `AppShell.xaml.cs` (`--page` switch)
- Modify: `src/RiftReview.App/App.xaml.cs` (DI)

- [ ] **Step 1: `SessionHealthView.xaml`** — READ `TrendsView.xaml` + `MatchupsView.xaml` first for exact xmlns/converter idioms. Build on a `PanelBgBrush` root `Grid` (rows: header Auto, body *):
  - Declare `UserControl.Resources`: `<BooleanToVisibilityConverter x:Key="BoolToVis"/>` and a `conv:BoolToBrushConverter x:Key="WinResultBrush" TrueBrush="{StaticResource WinBrush}" FalseBrush="{StaticResource LossBrush}"/>` (xmlns `conv="clr-namespace:RiftReview.App.Converters"`).
  - **Header:** gold "Session Health" `TextBlock` (`AccentBrush`, FontSize 18, SemiBold).
  - **Latest-session hero** (a `Border` `Background="{StaticResource CardBgBrush}"`, CornerRadius 6, Padding 16, Margin 0,0,0,16, `DataContext="{Binding Latest}"`, collapse via a `Style` DataTrigger when `{Binding Latest}` is `{x:Null}`):
    - `WhenLabel` (muted, small) + a big `SeverityLabel` `TextBlock` (FontSize 22 SemiBold) whose `Foreground` is set by DataTriggers: `IsTilted`→`LossBrush`, `IsCaution`→`AccentBrush`, `IsCalm`→`WinBrush` (default `TextPrimaryBrush`).
    - `Record` + `GamesLabel` on one line (muted).
    - `ReasonsText` (TextPrimary, wrap).
  - **Session history list:** muted header "RECENT SESSIONS", then a `ListView`/`ItemsControl` bound to `Sessions` (`Background=Transparent`, `BorderThickness=0`). Each item: a `DockPanel`/`Grid` row with `WhenLabel` (left, TextPrimary), `Record` + `GamesLabel` (muted), and a **severity pill** (`Border` CornerRadius 8, small, with `SeverityLabel` text; pill `Background` via the same `IsTilted`/`IsCaution`/`IsCalm` DataTriggers — Tilted `LossBrush`, Caution `AccentBrush`, Calm `WinBrush`, text on the pill `#0A0A0C` for contrast), and `ReasonsText` muted below. Add a 1px bottom `HairlineBrush` separator per row.
  - **Empty state** (`Visibility` ← `IsEmpty` via `BoolToVis`): a centered muted `TextBlock` "No games synced yet — play and sync to see your session health."

- [ ] **Step 2: `SessionHealthView.xaml.cs`**

```csharp
using System.Windows.Controls;
using RiftReview.App.ViewModels;

namespace RiftReview.App.Views;

public partial class SessionHealthView : UserControl
{
    public SessionHealthView(SessionHealthViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Loaded += async (_, _) => await vm.InitializeAsync();
    }
}
```

- [ ] **Step 3: Nav item** — in `AppShell.xaml`, add after the "Matchups" `NavigationViewItem` (before `</ui:NavigationView.MenuItems>`):

```xml
<ui:NavigationViewItem Content="Session Health"
                       TargetPageType="{x:Type views:SessionHealthView}">
    <ui:NavigationViewItem.Icon>
        <ui:SymbolIcon Symbol="HeartPulse24"/>
    </ui:NavigationViewItem.Icon>
</ui:NavigationViewItem>
```

If `HeartPulse24` does not compile, try `Pulse24`, then `Heart24`, then `ShieldTask24` (same fallback approach M3 used). In `AppShell.xaml.cs`, extend the `--page` switch with `"sessions" => typeof(SessionHealthView),` (next to the other cases).

- [ ] **Step 4: DI** — in `App.xaml.cs` `ConfigureServices`: register the VM as a **singleton** (beside `MainViewModel`) and the view transient:

```csharp
                s.AddSingleton<ViewModels.SessionHealthViewModel>();
```
```csharp
                s.AddTransient<SessionHealthView>();
```

- [ ] **Step 5: Build + smoke** — `dotnet build RiftReview.slnx -c Debug` (0 warnings; swap the symbol if needed). Then `dotnet test RiftReview.slnx` (full, green).

- [ ] **Step 6: Commit**

```bash
git add src/RiftReview.App/Views/SessionHealthView.xaml src/RiftReview.App/Views/SessionHealthView.xaml.cs src/RiftReview.App/AppShell.xaml src/RiftReview.App/AppShell.xaml.cs src/RiftReview.App/App.xaml.cs
git commit -m "feat(app): Session Health page + nav + DI

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 4: Tilt banner in `AppShell`

**Files:**
- Modify: `src/RiftReview.App/AppShell.xaml`
- Modify: `src/RiftReview.App/AppShell.xaml.cs`

- [ ] **Step 1: Restructure the root `Grid` for a banner row.** In `AppShell.xaml`, change `<Grid>` (the root inside `ui:FluentWindow`) to have two rows and put the existing `NavigationView` in row 1 (do NOT alter the NavigationView's internals — just add `Grid.Row="1"`). Add the banner as row 0:

```xml
<Grid>
    <Grid.RowDefinitions>
        <RowDefinition Height="Auto"/>
        <RowDefinition Height="*"/>
    </Grid.RowDefinitions>

    <!-- Tilt banner: collapses to zero height when not on tilt; spans the top. -->
    <Border x:Name="TiltBanner" Grid.Row="0"
            DataContext="{x:Null}"
            Padding="16,10" Margin="0"
            Visibility="Collapsed"
            BorderThickness="0,0,0,1"
            BorderBrush="{StaticResource HairlineBrush}">
        <Border.Resources>
            <SolidColorBrush x:Key="TiltTintBrush"    Color="#33E24A4A"/>
            <SolidColorBrush x:Key="CautionTintBrush" Color="#33C8AA6E"/>
        </Border.Resources>
        <Grid>
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>
            <TextBlock x:Name="TiltBannerText" Grid.Column="0"
                       VerticalAlignment="Center"
                       Foreground="{StaticResource TextPrimaryBrush}"
                       FontSize="13" TextWrapping="Wrap"/>
            <TextBlock Grid.Column="1" Text="Open Session Health ›"
                       VerticalAlignment="Center" Margin="16,0,0,0"
                       Foreground="{StaticResource AccentBrush}" FontSize="12"/>
        </Grid>
    </Border>

    <ui:NavigationView x:Name="RootNavigation" Grid.Row="1"
                       PaneDisplayMode="LeftFluent"
                       IsPaneOpen="False">
        ... (unchanged) ...
    </ui:NavigationView>
</Grid>
```

(The `›` is the literal "›" character — use the actual character in XAML.)

- [ ] **Step 2: Wire the banner in `AppShell.xaml.cs`.** The ctor already takes `(IServiceProvider sp, NavigationService nav)`. Resolve the singleton `SessionHealthViewModel` from `sp`, bind the banner's visibility/text/background to it, and make the banner navigate to the page on click. Add at the end of the ctor (after `Loaded += OnLoaded;`):

```csharp
        var health = sp.GetRequiredService<RiftReview.App.ViewModels.SessionHealthViewModel>();
        health.PropertyChanged += (_, _) => UpdateTiltBanner(health);
        UpdateTiltBanner(health);
        TiltBanner.MouseLeftButtonUp += (_, _) =>
            RootNavigation.Navigate(typeof(RiftReview.App.Views.SessionHealthView), null);
```

Add the helper method + usings (`using Microsoft.Extensions.DependencyInjection;`, `using System.Windows.Media;`, `using RiftReview.Core.Analysis;`):

```csharp
    private void UpdateTiltBanner(RiftReview.App.ViewModels.SessionHealthViewModel h)
    {
        TiltBanner.Visibility = h.BannerVisible ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        TiltBannerText.Text = h.BannerHeadline;
        var key = h.BannerSeverity == TiltSeverity.Tilted ? "TiltTintBrush" : "CautionTintBrush";
        TiltBanner.Background = (Brush)TiltBanner.Resources[key];
    }
```

(Code-behind binding avoids a 3-state converter and keeps the themed `NavigationView` untouched. `PropertyChanged` keeps the banner live when the page calls `Refresh()`.)

- [ ] **Step 3: Build + smoke** — `dotnet build RiftReview.slnx -c Debug` (0 warnings). `dotnet test RiftReview.slnx` (full, green — no test changes expected).

- [ ] **Step 4: Commit**

```bash
git add src/RiftReview.App/AppShell.xaml src/RiftReview.App/AppShell.xaml.cs
git commit -m "feat(app): cross-page tilt banner driven by the singleton SessionHealthViewModel

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 5: Demo seeder — a tilted latest session

**Files:**
- Modify: `src/RiftReview.App/Demo/DemoSeeder.cs`

The demo must show a **Tilted** latest session so the page + banner render under `--seed-demo`. READ `DemoSeeder.cs` first (M2/M3 left champ 103 = 24 games, champ 157 = 8, plus one-offs; games are spaced `baseCreation - i*86_400_000L` ms apart = **1 day apart**, so today every demo game is its OWN session — that alone wouldn't show a multi-game session).

- [ ] **Step 1: Append a clustered, tilted recent session.** After the existing seed loop, add ~5 games that are the **most recent** (largest `game_start_utc`) and **clustered < 3h apart** from each other, ending on a 4-loss skid with deaths climbing + CS@10 falling across the session. The session's FIRST game must start **> 3h after the previous newest demo game** so the gap rule treats it as a *distinct, latest* session (the existing demo games are ~1 day apart, so e.g. first tilted game = `newestExistingStart + 6h`, then +40 min each, works cleanly). Build them via the existing `BuildGame`/`MatchExtractor.Summarize` path (so the row scalars populate) OR, if simpler, write `MatchRow`s directly via `db.UpsertMatch(row, matchJson, tlJson)` using the same blob-builder the seeder already uses. Concretely the 5 games (oldest→newest within the session), spaced ~40 min apart:
  - g1 Win, deaths 2, cs10 80
  - g2 Loss, deaths 4, cs10 76
  - g3 Loss, deaths 6, cs10 70
  - g4 Loss, deaths 8, cs10 64
  - g5 Loss, deaths 10, cs10 58
  This yields EndLossStreak = 4 → **Tilted**, plus deaths climbing + CS falling. Use champ 103, queue 420, position MIDDLE, a valid opponent champ id, and ensure `cs_at_10`/`deaths` land on the stored row (if going through `BuildGame`, pass overrides like M3's `overrideWin`; if writing `MatchRow` directly, set the positional scalars).

- [ ] **Step 2: Confirm timestamps.** The session games must have the **largest** `game_start_utc` in the DB (so they form the *latest* session) and be < `SessionGapSeconds` (3h) apart from each other but **> 3h** after the previous newest demo game (so they're a distinct session). Remember `MatchRow.GameStartUtc` is **unix seconds**; the existing demo uses ms for `gameCreation` in the DTO but the stored `MatchRow.GameStartUtc` is seconds — match whatever the seeder already stores on the row.

- [ ] **Step 3: Build + smoke** — `dotnet build RiftReview.slnx -c Debug` (clean). Optionally `RiftReview.App.exe --seed-demo --page sessions` to eyeball a Tilted latest session + the banner. Then `dotnet test RiftReview.slnx` (full).

- [ ] **Step 4: Commit**

```bash
git add src/RiftReview.App/Demo/DemoSeeder.cs
git commit -m "feat(app): demo seeder adds a tilted latest session for Session Health

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 6: Screenshot verification gate (real + demo)

**Files:** none (verification only). Reuse `.m4shots/` (create dir) + the `.m2shots/capture.ps1` PrintWindow plumbing.

- [ ] **Step 1: Build Debug** — `dotnet build RiftReview.slnx -c Debug`; clean.

- [ ] **Step 2: Verify (Sonnet subagent, text verdict only, PNGs NOT loaded to main ctx).** Set `HKCU\Software\Microsoft\Avalon.Graphics\DisableHWAcceleration=1` (remember+restore), PrintWindow flag 2:
  - **Real DB** `--page sessions`: Session Health page renders — gold title, latest-session hero with a colored severity verdict, RECENT SESSIONS list (newest first) with W-L + severity pills + reasons. If the owner's real recent games happen to be Calm, the hero shows Calm (valid) and the banner is hidden — report which.
  - **Demo** `--seed-demo --page sessions`: latest session is **Tilted** (red verdict, "4-loss skid" reason); navigate to another page (e.g. `--seed-demo --page review`) and confirm the **tilt banner** is visible at the top, tinted red, with the headline + "Open Session Health ›".
  - Nav rail shows Review / Champions / Trends / Matchups / **Session Health** / Settings; switching works; no crash.
- [ ] **Step 3: Restore registry; report PASS/FAIL + PNG paths.** No code change → no commit. Fix any defect in its own commit.

---

## Acceptance criteria (from spec §10)

- `dotnet build` clean (0 warnings); `dotnet test` all green incl. new `SessionCalculator` + `SessionHealthViewModel` tests.
- No schema change / migration / backfill / Riot calls.
- Session Health page lists sessions newest-first (W-L, colored severity, reasons); latest-session hero prominent.
- Loss-streak (longest + trailing) + first-vs-second-half decay correct with null-guarding; severity per spec §4.4.
- Cross-page banner shows on Caution/Tilted latest session, colored by severity, links to the page; hidden when Calm.
- Demo shows a Tilted latest session + banner; screenshot-verified (real + demo).
- No secrets; `appsettings.json` placeholders only.

## Hand-back

- Does the 3-hour session gap match how you actually play?
- Do the tilt verdicts feel right on your real history?
- After-sync banner auto-refresh (without visiting the page) is deferred — want it?
