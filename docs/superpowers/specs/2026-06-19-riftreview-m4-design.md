# RiftReview M4 — Tilt Guard / Session Health — Design Spec

**Date:** 2026-06-19
**Milestone:** M4 of the 5-milestone roadmap (M4 = "tilt guard").
**Status:** design locked via owner brainstorming.

---

## 1. Goal

A **reflective, awareness-based tilt guard**: detect tilt patterns in the owner's recent play and surface them so he can decide to take a break — *between* sessions, not during. RiftReview is a **post-game** tool; it cannot hook the live League client to block re-queuing, so the guard is necessarily retrospective.

## 2. Owner-locked design decisions

- **Signals to watch (only these two):**
  1. **Loss streaks** — N consecutive losses within a play session.
  2. **In-session decay** — performance falling game-over-game within a session (deaths climbing, and/or CS@10 / KDA falling).
  - *Explicitly NOT in scope:* session-volume ("too many games today") and late-night-play detection. Do not build them.
- **Surface (both):** a dedicated **Session Health page** (read-only dashboard) **plus** a **warning banner** that appears on other pages when the most-recent session trips a tilt threshold.

## 3. Scope guard

- **Read-only analysis.** NO schema change, NO migration, NO backfill, NO Riot API calls. M4 reads the existing `matches` table only. Schema stays at **v3**.
- Uses the existing derived scalars (`deaths`, `cs_at_10`, KDA from K/D/A) — all **nullable**; the calculator MUST null-guard.
- MVP-first: thresholds are **constants in the pure calculator** (no new Settings controls in M4). Tunables are a future enhancement.

## 4. Core concepts

### 4.1 Play session

A **session** = a maximal run of consecutive games (ordered by `GameStartUtc`) where the idle gap between one game ending and the next starting is below a threshold.

- `GameStartUtc` is **unix seconds**; a game ends at `GameStartUtc + DurationS`.
- Gap between game *i* and *i+1* = `start[i+1] - (start[i] + duration[i])`.
- **`SessionGapSeconds = 3 * 3600` (3 hours).** If the gap ≤ this, same session; if greater, a new session begins.
- Sessions are built from **ALL** games (any queue), because tilt is about the play session regardless of queue. (A session may mix ranked + normals + Arena.) Loss-streak logic uses win/loss, which every game has; in-session-decay metrics null-guard for modes (Arena/ARAM) that lack CS@10.

### 4.2 In-session decay

For a session with ≥ `MinGamesForDecay = 3` games, compare the **first half** vs the **second half** of the session (split chronologically; odd counts put the middle game in neither half, or in the first half — pick first half) for each metric that has data in ≥1 game per half:

- **Deaths** (higher = worse): `secondHalfAvgDeaths - firstHalfAvgDeaths`. Positive & ≥ `DeathsDecayThreshold = 1.5` ⇒ deaths climbing.
- **CS@10** (lower = worse): `firstHalfAvgCs10 - secondHalfAvgCs10`. Positive & ≥ `Cs10DecayThreshold = 8` ⇒ CS falling.
- **KDA** (lower = worse): `firstHalfAvgKda - secondHalfAvgKda`. Positive & ≥ `KdaDecayThreshold = 1.5` ⇒ KDA falling.
- KDA per game = `(Kills + Assists) / max(1, Deaths)`.
- A metric with insufficient data (no non-null values in a half) ⇒ that decay flag is **null/absent** (not "no decay").

"In-session decay present" ⇒ **any** of the three decay flags fired.

### 4.3 Loss streaks (within a session)

- **LongestLossStreak** — max run of consecutive losses anywhere in the session.
- **EndLossStreak** — the trailing run of losses at the session's end (0 if the last game was a win). This is the "ended on a skid" signal that most strongly indicates tilt-now.

### 4.4 Tilt severity

Per session, compute `TiltSeverity ∈ { Calm, Caution, Tilted }` with contributing **reasons** (human-readable strings):

- **Tilted** (red) if **any**:
  - `EndLossStreak ≥ 3`, OR
  - `EndLossStreak ≥ 2` AND in-session decay present.
  - reasons e.g. `"Ended on a 4-loss skid"`, `"Deaths climbing + 2 losses to close"`.
- **Caution** (gold) if not Tilted and **any**:
  - `EndLossStreak == 2`, OR
  - `LongestLossStreak ≥ 3`, OR
  - in-session decay present, OR
  - session win-rate < 0.40 over ≥ 4 games.
  - reasons e.g. `"3-loss skid earlier"`, `"CS falling through the session"`, `"2 / 6 in this session"`.
- **Calm** (green) otherwise. reason `"No tilt signals"` (or the positive `"Ended on a 3-win heater"` if `EndWinStreak ≥ 3`, nice-to-have).

The **most-recent session** drives the banner: banner shows when its severity ≥ Caution.

## 5. Data model (Core, `RiftReview.Core.Analysis`)

`SessionModels.cs`:

```csharp
public enum TiltSeverity { Calm, Caution, Tilted }

public sealed record PlaySession(
    long StartUtc,                  // first game's GameStartUtc (unix seconds)
    long EndUtc,                    // last game's GameStartUtc + DurationS
    int Games,
    int Wins,
    int Losses,
    int LongestLossStreak,
    int EndLossStreak,
    int EndWinStreak,
    double? DeathsDelta,            // 2nd-half minus 1st-half avg deaths (null if insufficient data)
    double? Cs10Delta,             // 1st-half minus 2nd-half avg cs@10 (positive = falling)
    double? KdaDelta,              // 1st-half minus 2nd-half avg kda (positive = falling)
    bool DecayPresent,
    TiltSeverity Severity,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<MatchRow> GamesList);   // chronological oldest->newest
```

`SessionCalculator.cs` (pure static):

```csharp
public static class SessionCalculator
{
    public const int SessionGapSeconds = 3 * 3600;
    public const int MinGamesForDecay = 3;
    public const double DeathsDecayThreshold = 1.5;
    public const int    Cs10DecayThreshold   = 8;
    public const double KdaDecayThreshold     = 1.5;
    public const int    TiltedEndStreak       = 3;
    public const int    LongStreakCaution     = 3;
    public const double CautionWinRate        = 0.40;
    public const int    CautionWinRateMinGames = 4;

    // Newest session first (for display). Each session's GamesList is chronological.
    public static IReadOnlyList<PlaySession> BuildSessions(IReadOnlyList<MatchRow> matches);
}
```

- `BuildSessions` sorts matches ascending by `GameStartUtc`, groups by the gap rule, computes all fields, assigns severity + reasons, then returns the list **newest-session-first**.
- Empty input ⇒ empty list.

## 6. ViewModels (App)

### 6.1 `SessionHealthViewModel` (SINGLETON — like `MainViewModel`)

Shared by the Session Health page **and** the AppShell banner so both reflect one computation.

```csharp
public sealed partial class SessionHealthViewModel : ObservableObject
{
    public SessionHealthViewModel(RiftReviewDb db, DataDragonClient ddragon);

    public ObservableCollection<SessionRowViewModel> Sessions { get; }
    [ObservableProperty] private SessionRowViewModel? _latest;
    [ObservableProperty] private bool _isEmpty;

    // Banner (drives AppShell):
    [ObservableProperty] private bool _bannerVisible;       // latest severity >= Caution
    [ObservableProperty] private string _bannerHeadline;    // e.g. "Tilt check: ended on a 4-loss skid — consider a break."
    [ObservableProperty] private TiltSeverity _bannerSeverity;

    public async Task InitializeAsync();   // EnsureLoadedAsync (names) then Refresh()
    public void Refresh();                 // recompute from _db.AllMatches(rankedOnly:false)
}
```

- Ctor calls `Refresh()` so the **banner is correct at startup** before the user visits the page.
- `Refresh()`: `SessionCalculator.BuildSessions(_db.AllMatches(rankedOnly:false))` → wrap as `SessionRowViewModel`s; set `Latest`, `IsEmpty`; set banner from `Latest` (visible iff `Severity >= Caution`).
- **Banner refresh:** the page's `InitializeAsync` calls `Refresh()`; since the VM is a singleton bound to both the page and the AppShell banner, the banner updates live. (After-sync auto-refresh without a page visit is a documented future enhancement — out of M4 scope to avoid touching MainViewModel's tested ctor.)

### 6.2 `SessionRowViewModel` (display wrapper)

```csharp
public sealed class SessionRowViewModel
{
    public SessionRowViewModel(PlaySession s);
    public string WhenLabel { get; }      // "Jun 18 · 7:40 PM" (local, session start)
    public string Record { get; }         // "2–4"
    public string GamesLabel { get; }     // "6 games"
    public string SeverityLabel { get; }  // "Tilted" / "Caution" / "Calm"
    public TiltSeverity Severity { get; }
    public bool IsTilted { get; }         // Severity == Tilted
    public bool IsCaution { get; }
    public bool IsCalm { get; }
    public string ReasonsText { get; }    // reasons joined " · "
    public string LossStreakLabel { get; }// "4-loss skid to close" or "—"
    public string DecayLabel { get; }     // "Deaths ↑ · CS ↓" composed from the deltas, or "—"
    public IReadOnlyList<MatchupGameViewModel>? Games { get; }  // OPTIONAL: reuse not required; can be a lighter SessionGameViewModel
}
```

(If reusing `MatchupGameViewModel` is awkward, a minimal `SessionGameViewModel { Result, Kda, WhenLocal, Win }` is fine. Keep it light.)

## 7. Views

### 7.1 `SessionHealthView.xaml` (page)

`PanelBgBrush` surface. Top to bottom:
- Gold "Session Health" title (`AccentBrush`, 18, SemiBold).
- **Latest-session hero**: a `Border` `CardBgBrush` tile showing `Latest.WhenLabel`, the big **severity verdict** (colored: Tilted→`LossBrush`, Caution→`AccentBrush`, Calm→`WinBrush`), `Record`, `ReasonsText`. Collapsed when `Latest` is null.
- **Session history list**: an `ItemsControl`/`ListView` of `Sessions` (newest first), each row: `WhenLabel`, `Record`, a severity pill (colored), `ReasonsText` muted. Thin/short sessions still shown.
- **Empty state** (`IsEmpty`): "No games synced yet — play and sync to see your session health."

### 7.2 Banner (in `AppShell.xaml`)

A `Border` at the **top of the content frame** (above the `NavigationView`'s frame, or as a row above it), bound to the singleton `SessionHealthViewModel`:
- `Visibility` ← `BannerVisible` (BoolToVis).
- Background tinted by severity (Tilted = `LossBrush` @ low opacity / a dark red; Caution = `AccentBrush`-tinted). Use a `BoolToBrushConverter` or a small severity→brush converter.
- Text ← `BannerHeadline`, plus a subtle "Open Session Health ›" affordance (clicking navigates to the page via the existing `NavigationService` / `RootNavigation`).
- Wire the banner's DataContext in `AppShell.xaml.cs` by resolving the singleton VM from DI.

## 8. Navigation + DI

- **Nav item** in `AppShell.xaml` after "Matchups": `Content="Session Health" TargetPageType="{x:Type views:SessionHealthView}"`, symbol `ui:SymbolIcon Symbol="..."` — try `Heart24` / `HeartPulse24` / `Pulse24`; fall back to a known-good (`ShieldTask24`, `Alert24`) if it doesn't compile.
- **`--page sessions` (or `sessionhealth`)** case in `AppShell.xaml.cs`'s switch → `typeof(SessionHealthView)`.
- **DI** in `App.xaml.cs`: `s.AddSingleton<ViewModels.SessionHealthViewModel>();` (singleton, beside `MainViewModel`) + `s.AddTransient<SessionHealthView>();` (page). Resolve the singleton VM in the `AppShell` ctor for the banner.

## 9. Demo seeder

Add a clearly **tilted recent session** so the page + banner render under `--seed-demo`: ~5 consecutive games clustered in time (gaps < 3h) ending in a 4-loss skid with deaths climbing and CS@10 falling across the session. Place it as the **most recent** games (largest `GameStartUtc`) so it's the latest session and trips the banner (Tilted). Keep the existing demo games intact (they'll form earlier, calmer sessions). Confirm the seeded session's metrics are non-null (CS@10 etc. set) so decay computes.

## 10. Acceptance criteria

- `dotnet build RiftReview.slnx` clean (0 warnings); `dotnet test RiftReview.slnx` all green incl. new `SessionCalculator` + `SessionHealthViewModel` tests.
- No schema change / migration / backfill / Riot calls.
- Session Health page lists sessions newest-first with W-L, a colored severity verdict, and reasons; the latest-session hero is prominent.
- Loss-streak detection correct (longest + trailing); in-session decay computed from first-vs-second half with null-guarding; severity thresholds per §4.4.
- The warning banner appears on other pages when the latest session is Caution/Tilted, colored by severity, and links to the page; it is hidden when the latest session is Calm.
- Demo (`--seed-demo`) shows a Tilted latest session + the banner; screenshot-verified (real + demo) by a Sonnet subagent (text verdict).
- No secrets; `appsettings.json` placeholders only.

## 11. Hand-back (owner runs locally)

- Does the session grouping (3-hour gap) match how you actually play? (tunable later if not.)
- Do the tilt verdicts feel right on your real history — any session flagged Tilted/Caution that felt fine, or vice versa?
- After-sync banner auto-refresh (without visiting the page) is deferred — confirm whether you want it.
