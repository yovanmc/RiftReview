# RiftReview M7 — Vision + objectives (self-contained plan)

> **Written for Sonnet execution. If something doesn't match what this plan says
> (file paths, signatures, JSON field names, line context), STOP and report rather
> than guess.** This plan was authored after four read-only recon passes + an
> authoritative web verification of the Riot match-v5 timeline event schema.

## Goal

Add a **Vision & objectives** section to the per-match deep-dive (`DeepDiveView`,
embedded in `ReviewView`). It shows, for the reviewed match:

- **Wards placed / cleared / control wards** — *exact* counts from timeline events.
- **Vision proxy** — an **explicitly-labeled estimate** (not Riot's official Vision
  Score, which we can't reconstruct from events alone).
- **Objective participation** — per objective type (Dragons, Rift Herald, Baron,
  Towers, Inhibitors; Void Grubs only if present), "you participated in X of your
  team's Y", from timeline events.

All metrics are computed **on demand** from the already-stored `match_detail.timeline_json`
blob. **No DB schema migration.** (The blob has been persisted since M1; the existing
`DerivedMetricsBackfill` path proves it's immutably re-parseable.)

## Why this is data-honest (acceptance constraint, not optional)

- Ward counts and objective participation are **exact** (every `WARD_PLACED`/`WARD_KILL`/
  `ELITE_MONSTER_KILL`/`BUILDING_KILL` event is real Riot data).
- The **vision proxy** must be labeled in the UI as an estimate with its formula shown,
  and the **exact ward counts** must be shown alongside it. Never present the proxy as
  Riot's Vision Score.
- When my team took **zero** of an objective type, show **"none taken"** — never a
  fabricated `0%` participation. This mirrors the M6 "never-fabricate" guardrail.

## Riot match-v5 timeline event schema (VERIFIED — hard-code these names)

Confirmed across Riot's match-v5 changelog gist, Riot dev-relations issue #160, and
multiple typed community libraries (fightmegg TS, golio, Kinveil Go). JSON is camelCase;
the project deserializes with `PropertyNameCaseInsensitive = true`, so C# PascalCase maps
to camelCase automatically.

| Event | Key fields | Notes |
|-------|-----------|-------|
| `WARD_PLACED` | `creatorId` (placer, **not** participantId), `wardType` | no position field |
| `WARD_KILL` | `killerId` (clearer), `wardType` | `wardType` e.g. `CONTROL_WARD`, `YELLOW_TRINKET`, `SIGHT_WARD`, `BLUE_TRINKET`, `TEEMO_MUSHROOM`, `UNDEFINED` |
| `ELITE_MONSTER_KILL` | `killerId`, `killerTeamId` (credited team), `monsterType`, `monsterSubType` (dragons only), `assistingParticipantIds` | `monsterType` ∈ `DRAGON`,`RIFTHERALD`,`BARON_NASHOR`,`HORDE` |
| `BUILDING_KILL` | `killerId` (may be `0`), `teamId` (**the team that LOST the building**), `buildingType`, `towerType`, `laneType`, `assistingParticipantIds` | **no `killerTeamId`** here; my team destroyed it iff `teamId != myTeamId` |

**Guards (mandatory):**
- Every field except `type`/`timestamp` is effectively optional → all new DTO fields are
  nullable; presence-check before use.
- `killerId == 0` (or null) ⇒ unattributable (minion/structure). `TeamOf(0)` returns null.
- Team invariant: participant 1–5 ⇒ team 100, 6–10 ⇒ team 200 (reliable for ranked SR).
  Use this only as the fallback for `ELITE_MONSTER_KILL` when `killerTeamId` is absent.
- Enum values are **open**: filter by known strings, never `throw` on an unknown value.

## Files this milestone touches

| File | Change |
|------|--------|
| `src/RiftReview.Core/Riot/Dtos/TimelineDtos.cs` | extend `EventDto` with nullable ward/objective fields (defaults `null`) |
| `src/RiftReview.Core/Analysis/AnalysisModels.cs` | add `VisionStats`, `ObjectiveParticipation`, `VisionObjectivesResult` records; add `MyTeamId` to `MatchSummary` |
| `src/RiftReview.Core/Analysis/MatchExtractor.cs` | populate `MyTeamId` from `me.TeamId` |
| `src/RiftReview.Core/Analysis/TimelineExtractor.cs` | add `BuildVisionObjectives(...)` + private `TeamOf(...)` |
| `tests/RiftReview.Core.Tests/Fixtures/vision_objectives_timeline.json` | **new** fixture |
| `tests/RiftReview.Core.Tests/TimelineExtractorTests.cs` | add vision + objective tests |
| `tests/RiftReview.Core.Tests/MatchExtractorTests.cs` | add `MyTeamId` test |
| `src/RiftReview.App/ViewModels/DeepDiveViewModel.cs` | expose `Vision` + `Objectives`; wire in `Load`/`Clear`; add `ObjectiveRowVm` |
| `src/RiftReview.App/Views/DeepDiveView.xaml` | insert compact Vision & objectives section at top |
| `src/RiftReview.App/Demo/DemoSeeder.cs` | inject ward/objective events so the demo deep-dive renders non-zero |
| `.m7shots/run_capture.ps1` | **new** — copy/adapt from `.m6shots/run_capture.ps1` |

---

## Tasks (TDD red→green→commit per task; subagent-driven)

Branch: **`m7-vision-objectives`** off `master`. Commit author = repo default (`yovanmc`),
**NO `--author`**. Trailer on every commit:
`Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.
Build `dotnet build RiftReview.slnx -v minimal`; test `dotnet test RiftReview.slnx`.

### Task 1 — EventDto fields + records + vision counts (TDD)

**1a. Extend `EventDto`** in `src/RiftReview.Core/Riot/Dtos/TimelineDtos.cs`. Current:
```csharp
public sealed record EventDto(string Type, long Timestamp, int? KillerId, int? VictimId);
```
Replace with (new params have defaults so existing positional constructions still compile):
```csharp
public sealed record EventDto(
    string Type, long Timestamp, int? KillerId, int? VictimId,
    int? CreatorId = null,
    string? WardType = null,
    int? KillerTeamId = null,
    string? MonsterType = null,
    string? MonsterSubType = null,
    string? BuildingType = null,
    string? TowerType = null,
    string? LaneType = null,
    int? TeamId = null,
    List<int>? AssistingParticipantIds = null);
```

**1b. Add result records** to `src/RiftReview.Core/Analysis/AnalysisModels.cs`, below the
existing `DeepDive` record, matching house style (sealed records, `IReadOnlyList<T>`):
```csharp
public sealed record VisionStats(
    int WardsPlaced, int WardsCleared, int ControlWardsPlaced, int VisionProxy);

public sealed record ObjectiveParticipation(
    string Label, int Participated, int TeamTotal);

public sealed record VisionObjectivesResult(
    VisionStats Vision, IReadOnlyList<ObjectiveParticipation> Objectives);
```

**1c. New test fixture** `tests/RiftReview.Core.Tests/Fixtures/vision_objectives_timeline.json`
(me = participantId 3, team 100). Use this EXACT content:
```json
{
  "_note": "synthetic vision+objective events for M7 tests; me = participantId 3 (team 100)",
  "metadata": { "matchId": "NA1_M7_TEST", "participants": ["P1","P2","ME","P4","P5","P6","P7","P8","P9","P10"] },
  "info": {
    "frameInterval": 60000,
    "frames": [
      { "timestamp": 0, "participantFrames": {}, "events": [] },
      { "timestamp": 1800000, "participantFrames": {}, "events": [
        { "type": "WARD_PLACED", "timestamp": 120000, "creatorId": 3, "wardType": "YELLOW_TRINKET" },
        { "type": "WARD_PLACED", "timestamp": 300000, "creatorId": 3, "wardType": "CONTROL_WARD" },
        { "type": "WARD_PLACED", "timestamp": 320000, "creatorId": 7, "wardType": "CONTROL_WARD" },
        { "type": "WARD_KILL", "timestamp": 340000, "killerId": 3, "wardType": "SIGHT_WARD" },
        { "type": "WARD_KILL", "timestamp": 360000, "killerId": 8, "wardType": "YELLOW_TRINKET" },
        { "type": "ELITE_MONSTER_KILL", "timestamp": 480000, "killerId": 3, "killerTeamId": 100, "monsterType": "DRAGON", "monsterSubType": "FIRE_DRAGON", "assistingParticipantIds": [1,2] },
        { "type": "ELITE_MONSTER_KILL", "timestamp": 1140000, "killerId": 2, "killerTeamId": 100, "monsterType": "DRAGON", "monsterSubType": "WATER_DRAGON", "assistingParticipantIds": [3,4] },
        { "type": "ELITE_MONSTER_KILL", "timestamp": 1680000, "killerId": 8, "killerTeamId": 200, "monsterType": "DRAGON", "monsterSubType": "AIR_DRAGON" },
        { "type": "ELITE_MONSTER_KILL", "timestamp": 660000, "killerId": 1, "killerTeamId": 100, "monsterType": "RIFTHERALD", "assistingParticipantIds": [3] },
        { "type": "ELITE_MONSTER_KILL", "timestamp": 1500000, "killerId": 7, "killerTeamId": 200, "monsterType": "BARON_NASHOR" },
        { "type": "BUILDING_KILL", "timestamp": 780000, "killerId": 3, "teamId": 200, "buildingType": "TOWER_BUILDING", "towerType": "OUTER_TURRET", "laneType": "MID_LANE" },
        { "type": "BUILDING_KILL", "timestamp": 1020000, "killerId": 6, "teamId": 200, "buildingType": "TOWER_BUILDING", "towerType": "OUTER_TURRET", "laneType": "TOP_LANE" },
        { "type": "BUILDING_KILL", "timestamp": 900000, "killerId": 8, "teamId": 100, "buildingType": "TOWER_BUILDING", "towerType": "OUTER_TURRET", "laneType": "MID_LANE" },
        { "type": "BUILDING_KILL", "timestamp": 1620000, "killerId": 6, "teamId": 200, "buildingType": "INHIBITOR_BUILDING", "laneType": "MID_LANE", "assistingParticipantIds": [3] }
      ] }
    ]
  }
}
```
**Check `tests/RiftReview.Core.Tests/RiftReview.Core.Tests.csproj`:** confirm fixtures are
copied to output. If there's a wildcard like `<Content Include="Fixtures\**\*.json" CopyToOutputDirectory="PreserveNewest" />`
the new file is covered automatically. If fixtures are listed per-file, add an entry for
`vision_objectives_timeline.json` with `CopyToOutputDirectory=PreserveNewest`. If neither
pattern is present, STOP and report.

**1d. RED — add vision test** to `tests/RiftReview.Core.Tests/TimelineExtractorTests.cs`.
Add a fixture loader mirroring the existing `Tl()` helper:
```csharp
private static TimelineDto VoTl() => JsonSerializer.Deserialize<TimelineDto>(
    FixtureLoader.Read("vision_objectives_timeline.json"),
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

[Fact]
public void Vision_counts_my_wards_placed_cleared_and_control()
{
    var r = TimelineExtractor.BuildVisionObjectives(VoTl(), myParticipantId: 3, myTeamId: 100);
    Assert.Equal(2, r.Vision.WardsPlaced);
    Assert.Equal(1, r.Vision.WardsCleared);
    Assert.Equal(1, r.Vision.ControlWardsPlaced);
    Assert.Equal(4, r.Vision.VisionProxy); // 2 placed + 1 cleared + 1 control
}
```

**1e. GREEN — implement `BuildVisionObjectives`** (vision portion is enough to pass 1d, but
implement the whole method now; objectives are tested in Task 2). Add to the existing
`public static class TimelineExtractor` in `src/RiftReview.Core/Analysis/TimelineExtractor.cs`:
```csharp
// Riot match-v5 timeline: participant 1-5 = team 100, 6-10 = team 200 (standard SR).
// participantId 0/null = no participant (minion/structure) => no team.
private static int? TeamOf(int? participantId) =>
    participantId is null or <= 0 ? null : (participantId <= 5 ? 100 : 200);

public static VisionObjectivesResult BuildVisionObjectives(
    TimelineDto tl, int myParticipantId, int myTeamId)
{
    var events = tl.Info.Frames.SelectMany(f => f.Events).ToList();

    // --- Vision (exact counts) ---
    int wardsPlaced  = events.Count(e => e.Type == "WARD_PLACED" && e.CreatorId == myParticipantId);
    int wardsCleared = events.Count(e => e.Type == "WARD_KILL"   && e.KillerId  == myParticipantId);
    int controlWards = events.Count(e => e.Type == "WARD_PLACED" && e.CreatorId == myParticipantId
                                         && e.WardType == "CONTROL_WARD");
    // Proxy: labeled in the UI as an estimate, NOT Riot's Vision Score.
    // wards placed + wards cleared + control wards (control wards effectively count twice).
    int visionProxy = wardsPlaced + wardsCleared + controlWards;
    var vision = new VisionStats(wardsPlaced, wardsCleared, controlWards, visionProxy);

    bool IParticipated(EventDto e) =>
        e.KillerId == myParticipantId ||
        (e.AssistingParticipantIds?.Contains(myParticipantId) ?? false);

    // --- Elite monsters (credited team = killerTeamId, fallback team-of-killer) ---
    var monsters = events.Where(e => e.Type == "ELITE_MONSTER_KILL").ToList();
    ObjectiveParticipation Monster(string monsterType, string label)
    {
        var mine = monsters.Where(e => e.MonsterType == monsterType
                                       && (e.KillerTeamId ?? TeamOf(e.KillerId)) == myTeamId).ToList();
        return new ObjectiveParticipation(label, mine.Count(IParticipated), mine.Count);
    }

    // --- Buildings (teamId = LOSING/owning team; my team destroyed it iff teamId != myTeam) ---
    var buildings = events.Where(e => e.Type == "BUILDING_KILL").ToList();
    ObjectiveParticipation Building(string buildingType, string label)
    {
        var mine = buildings.Where(e => e.BuildingType == buildingType
                                        && e.TeamId is 100 or 200 && e.TeamId != myTeamId).ToList();
        return new ObjectiveParticipation(label, mine.Count(IParticipated), mine.Count);
    }

    var objectives = new List<ObjectiveParticipation>
    {
        Monster("DRAGON", "Dragons"),
        Monster("RIFTHERALD", "Rift Herald"),
        Monster("BARON_NASHOR", "Baron"),
        Building("TOWER_BUILDING", "Towers"),
        Building("INHIBITOR_BUILDING", "Inhibitors"),
    };
    // Void Grubs only if the game had them (newer patches) — avoid noise on older games.
    if (monsters.Any(e => e.MonsterType == "HORDE"))
        objectives.Insert(3, Monster("HORDE", "Void Grubs"));

    return new VisionObjectivesResult(vision, objectives);
}
```
Run `dotnet test RiftReview.slnx`; Task-1 test green. **Commit:**
`feat(core): timeline EventDto vision/objective fields + vision proxy extraction`.

### Task 2 — Objective participation (TDD)

**2a. RED — add objectives test** to `TimelineExtractorTests.cs`:
```csharp
[Fact]
public void Objectives_credit_my_team_only_and_count_my_participation()
{
    var r = TimelineExtractor.BuildVisionObjectives(VoTl(), myParticipantId: 3, myTeamId: 100);
    ObjectiveParticipation O(string label) => r.Objectives.Single(o => o.Label == label);
    Assert.Equal((2, 2), (O("Dragons").Participated, O("Dragons").TeamTotal));        // killed 1, assisted 1; enemy dragon excluded
    Assert.Equal((1, 1), (O("Rift Herald").Participated, O("Rift Herald").TeamTotal));// assisted
    Assert.Equal((0, 0), (O("Baron").Participated, O("Baron").TeamTotal));            // only enemy took baron
    Assert.Equal((1, 2), (O("Towers").Participated, O("Towers").TeamTotal));          // my OWN lost tower (teamId 100) excluded
    Assert.Equal((1, 1), (O("Inhibitors").Participated, O("Inhibitors").TeamTotal));  // assisted
    Assert.DoesNotContain(r.Objectives, o => o.Label == "Void Grubs");               // no HORDE in fixture
}
```
This should already pass against the Task-1 GREEN implementation (the full method was
written there). If it does, that's expected — the test still earns its keep as the
regression guard for the objective-crediting + team-filter logic. If any assertion fails,
fix `BuildVisionObjectives` until green. **Commit:** `test(core): objective participation crediting + team filter`.

### Task 3 — `MatchSummary.MyTeamId` (TDD)

**3a. RED — add test** to `tests/RiftReview.Core.Tests/MatchExtractorTests.cs`, mirroring the
existing Summarize test setup in that file (use its existing match fixture + puuid helper).
First read `tests/RiftReview.Core.Tests/Fixtures/sample_match.json` to find the `teamId` of
the participant whose `puuid` matches the test puuid, and assert that exact value:
```csharp
[Fact]
public void Summarize_sets_my_team_id_from_my_participant()
{
    var s = /* existing pattern: MatchExtractor.Summarize(<sample match>, <test puuid>) */;
    Assert.Equal(/* the teamId (100 or 200) of my participant in sample_match.json */, s.MyTeamId);
}
```

**3b. GREEN.** Add `int MyTeamId` to `MatchSummary` in `AnalysisModels.cs`, positioned
right after `MyParticipantId`:
```csharp
public sealed record MatchSummary(
    int QueueId, long GameStartUtc, int DurationS, string Patch,
    int MyChampionId, string MyTeamPosition, bool Win,
    int Kills, int Deaths, int Assists, int Cs,
    int MyParticipantId, int MyTeamId, int? OpponentParticipantId, int? OpponentChampionId,
    double KillParticipation, double DamageShare);
```
Update the single constructor call in `src/RiftReview.Core/Analysis/MatchExtractor.cs`
(`Summarize`) — insert `me.TeamId` in the matching position:
```csharp
    me.ParticipantId, me.TeamId, opp?.ParticipantId, opp?.ChampionId,
```
(`me` is the `ParticipantDto` found by `myPuuid`; `ParticipantDto` has `int TeamId`.)
Build the whole solution — the only other `MatchSummary` consumers (`SyncService`,
`DemoSeeder`) read named properties via `MatchExtractor.Summarize`, so they're unaffected.
If the build surfaces any other direct `new MatchSummary(` site, fix it. Run tests green.
**Commit:** `feat(core): expose MyTeamId on MatchSummary`.

### Task 4 — Wire into DeepDiveViewModel

Read `src/RiftReview.App/ViewModels/DeepDiveViewModel.cs` fully first.

**4a.** Add observable properties (mirror the existing `[ObservableProperty]` style, e.g.
the `_goldSeries`/`_csSeries` block):
```csharp
[ObservableProperty] private VisionStats _vision = new(0, 0, 0, 0);
[ObservableProperty] private IReadOnlyList<ObjectiveRowVm> _objectives = Array.Empty<ObjectiveRowVm>();
```

**4b.** In `Load(MatchRow selected)`, after the existing `var dd = TimelineExtractor.BuildDeepDive(tl, summary.MyParticipantId, summary.OpponentParticipantId);`
block and its property assignments, add:
```csharp
var vo = TimelineExtractor.BuildVisionObjectives(tl, summary.MyParticipantId, summary.MyTeamId);
Vision = vo.Vision;
Objectives = vo.Objectives.Select(o => new ObjectiveRowVm(
    o.Label,
    o.TeamTotal == 0 ? "none taken" : $"{o.Participated} / {o.TeamTotal}",
    o.TeamTotal == 0 ? "" : ((double)o.Participated / o.TeamTotal).ToString("P0"))).ToList();
```
(`summary` is the `MatchSummary` from `MatchExtractor.Summarize`; `tl` is the deserialized
`TimelineDto` already present in `Load`.)

**4c.** In `Clear()`, reset:
```csharp
Vision = new(0, 0, 0, 0);
Objectives = Array.Empty<ObjectiveRowVm>();
```

**4d.** Add the display record (bottom of the file, in the same `namespace`):
```csharp
public sealed record ObjectiveRowVm(string Label, string Detail, string Percent);
```
Build `dotnet build RiftReview.slnx -v minimal` (no new tests). **Commit:**
`feat(app): deep-dive ViewModel exposes vision + objective participation`.

### Task 5 — DeepDiveView XAML section

Read `src/RiftReview.App/Views/DeepDiveView.xaml` fully. The content is a `Grid` with
`RowDefinitions`; the header is `Grid.Row="0"`, the gold-diff chart `Grid.Row="1"`, CS
chart `Grid.Row="2"`, etc.

**Insert a compact section as the new `Grid.Row="1"` (directly under the header, ABOVE the
charts) so it is always visible in the PrintWindow capture without scrolling.** Add one
`<RowDefinition Height="Auto"/>` and **increment `Grid.Row` by 1 on every element currently
at Row ≥ 1.** Insert this block (uses existing theme resource keys
`CardBgBrush`/`HairlineBrush`/`AccentBrush`/`TextPrimaryBrush`/`TextMutedBrush`):
```xml
<Border Grid.Row="1"
        Background="{StaticResource CardBgBrush}"
        BorderBrush="{StaticResource HairlineBrush}"
        BorderThickness="0,0,0,1"
        Margin="0,0,0,2"
        Padding="12,8">
    <StackPanel>
        <TextBlock Text="Vision &amp; objectives"
                   Foreground="{StaticResource AccentBrush}"
                   FontSize="13" FontWeight="SemiBold" Margin="0,0,0,6"/>
        <StackPanel Orientation="Horizontal" Margin="0,0,0,4">
            <StackPanel Margin="0,0,28,0">
                <TextBlock Text="Wards placed" Foreground="{StaticResource TextMutedBrush}" FontSize="11"/>
                <TextBlock Text="{Binding Vision.WardsPlaced}" Foreground="{StaticResource TextPrimaryBrush}" FontSize="18"/>
            </StackPanel>
            <StackPanel Margin="0,0,28,0">
                <TextBlock Text="Wards cleared" Foreground="{StaticResource TextMutedBrush}" FontSize="11"/>
                <TextBlock Text="{Binding Vision.WardsCleared}" Foreground="{StaticResource TextPrimaryBrush}" FontSize="18"/>
            </StackPanel>
            <StackPanel Margin="0,0,28,0">
                <TextBlock Text="Control wards" Foreground="{StaticResource TextMutedBrush}" FontSize="11"/>
                <TextBlock Text="{Binding Vision.ControlWardsPlaced}" Foreground="{StaticResource TextPrimaryBrush}" FontSize="18"/>
            </StackPanel>
            <StackPanel Margin="0,0,28,0">
                <TextBlock Text="Vision proxy" Foreground="{StaticResource TextMutedBrush}" FontSize="11"/>
                <TextBlock Text="{Binding Vision.VisionProxy}" Foreground="{StaticResource AccentBrush}" FontSize="18"/>
            </StackPanel>
        </StackPanel>
        <TextBlock Text="Vision proxy = wards placed + cleared (control wards count twice). Estimate from timeline events — not Riot's official Vision Score."
                   Foreground="{StaticResource TextMutedBrush}" FontSize="10" TextWrapping="Wrap" Margin="0,0,0,6"/>
        <ItemsControl ItemsSource="{Binding Objectives}">
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <Grid Margin="0,1">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="110"/>
                            <ColumnDefinition Width="70"/>
                            <ColumnDefinition Width="*"/>
                        </Grid.ColumnDefinitions>
                        <TextBlock Grid.Column="0" Text="{Binding Label}" Foreground="{StaticResource TextPrimaryBrush}" FontSize="12"/>
                        <TextBlock Grid.Column="1" Text="{Binding Detail}" Foreground="{StaticResource TextPrimaryBrush}" FontSize="12"/>
                        <TextBlock Grid.Column="2" Text="{Binding Percent}" Foreground="{StaticResource AccentBrush}" FontSize="12"/>
                    </Grid>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
    </StackPanel>
</Border>
```
If the theme resource keys above are not all present in `Themes/Colors.xaml`, STOP and
report (do not invent brush keys). Build `dotnet build RiftReview.slnx -v minimal`.
**Commit:** `feat(app): deep-dive Vision & objectives section`.

### Task 6 — Demo seed events

Read `src/RiftReview.App/Demo/DemoSeeder.cs`. In `BuildGame()`, **after** the existing
`CHAMPION_KILL` death-event loop (which adds to `frameList[fm].Events`), inject vision +
objective events attributed to `pid 3` (ME, team 100). Use the existing `frames` count and
`frameList`. Add a local helper and the events:
```csharp
// M7: vision + objective events (attributed to ME = pid 3, team 100) so the deep-dive
// Vision & objectives section renders with realistic demo data. Minutes <= 25 so they
// survive shorter demo games; the guard drops any that exceed this game's length.
void AddEv(int minute, EventDto ev) { if (minute < frames) frameList[minute].Events.Add(ev); }

foreach (var m in new[] { 2, 5, 9, 14, 20 })
    AddEv(m, new EventDto("WARD_PLACED", 60000L * m, null, null,
        CreatorId: 3, WardType: (m == 9 || m == 14) ? "CONTROL_WARD" : "YELLOW_TRINKET"));
foreach (var m in new[] { 7, 16, 24 })
    AddEv(m, new EventDto("WARD_KILL", 60000L * m, KillerId: 3, VictimId: null, WardType: "SIGHT_WARD"));

AddEv(8,  new EventDto("ELITE_MONSTER_KILL", 60000L * 8,  KillerId: 3, VictimId: null,
    KillerTeamId: 100, MonsterType: "DRAGON", MonsterSubType: "FIRE_DRAGON",
    AssistingParticipantIds: new List<int> { 1, 2 }));
AddEv(19, new EventDto("ELITE_MONSTER_KILL", 60000L * 19, KillerId: 2, VictimId: null,
    KillerTeamId: 100, MonsterType: "DRAGON", MonsterSubType: "WATER_DRAGON",
    AssistingParticipantIds: new List<int> { 3, 4 }));
AddEv(11, new EventDto("ELITE_MONSTER_KILL", 60000L * 11, KillerId: 1, VictimId: null,
    KillerTeamId: 100, MonsterType: "RIFTHERALD", AssistingParticipantIds: new List<int> { 3 }));

AddEv(13, new EventDto("BUILDING_KILL", 60000L * 13, KillerId: 3, VictimId: null,
    TeamId: 200, BuildingType: "TOWER_BUILDING", TowerType: "OUTER_TURRET", LaneType: "MID_LANE"));
AddEv(17, new EventDto("BUILDING_KILL", 60000L * 17, KillerId: 6, VictimId: null,
    TeamId: 200, BuildingType: "TOWER_BUILDING", TowerType: "OUTER_TURRET", LaneType: "TOP_LANE"));
AddEv(22, new EventDto("BUILDING_KILL", 60000L * 22, KillerId: 3, VictimId: null,
    TeamId: 200, BuildingType: "TOWER_BUILDING", TowerType: "INNER_TURRET", LaneType: "MID_LANE",
    AssistingParticipantIds: new List<int> { 2 }));
AddEv(15, new EventDto("BUILDING_KILL", 60000L * 15, KillerId: 8, VictimId: null,
    TeamId: 100, BuildingType: "TOWER_BUILDING", TowerType: "OUTER_TURRET", LaneType: "MID_LANE"));
```
If `BuildGame` exposes the per-minute frame list / `frames` count under different local
names, adapt to the names actually in the file (the death-event loop directly above shows
the correct names). **Expected first-match demo numbers:** Wards placed 5, cleared 3,
control 2, proxy 10; Dragons 2/2; Rift Herald 1/1; Baron none taken; Towers 2/3;
Inhibitors none taken. Build. **Commit:** `feat(app): seed demo timeline with vision + objective events`.

### Task 7 — Screenshot harness + capture + verify

**7a.** Copy `.m6shots/run_capture.ps1` → `.m7shots/run_capture.ps1`. Edit it to: write
output PNGs into `.m7shots/` (replace `.m6shots` path refs); keep the `--page review`
launch + the DisableHWAcceleration set/restore + the `.m2shots/Capturer/out/Capturer.exe`
PrintWindow capture + the UIAutomation `SelectionItemPattern.Select()` of the first match
ListItem; produce `.m7shots/deepdive.png`. (The trends capture is not needed for M7; you
may drop it or keep it.) Keep `.m7shots/*.png` gitignored (match how `.m6shots` PNGs are
ignored — check the repo `.gitignore`; add `.m7shots/*.png` if the existing rule is
per-folder).

**7b.** Build the Debug exe (`dotnet build RiftReview.slnx -v minimal` builds it; the exe
is `src/RiftReview.App/bin/Debug/net10.0-windows/RiftReview.App.exe`). Run
`.m7shots/run_capture.ps1`. Confirm `.m7shots/deepdive.png` was produced.

**7c. Verdict via Sonnet subagent (text only — never load the PNG into the controller).**
The controller dispatches a subagent to Read `.m7shots/deepdive.png` and return a text
verdict against acceptance criteria below.

**Commit** the harness script (not the PNG): `chore(app): M7 screenshot harness (.m7shots)`.

---

## Verification / definition of done

- `dotnet build RiftReview.slnx -v minimal` clean.
- `dotnet test RiftReview.slnx` all green (M2–M6 suite + the new Task-1/2/3 tests).
- Screenshot subagent verdict = **PASS**: `.m7shots/deepdive.png` shows the **Vision &
  objectives** section with non-zero **Wards placed/cleared/Control wards** and a **Vision
  proxy**, the proxy caption clearly labels it an estimate (not Riot Vision Score), and the
  objective rows show participation (e.g. Dragons 2/2 (100%), Towers 2/3 (67%)) with
  "none taken" where the team took zero — themed black-glass + Hextech Gold, no clipping or
  text overlap.
- PR → `gh pr checks <#> --watch` green → `--merge --delete-branch` from `master`.

## Risks / notes

- **Screenshot visibility:** the new section is placed at the TOP of the deep-dive
  specifically so PrintWindow (which only renders visible content) captures it without
  scrolling. If after capture it's still below the fold, the harness or layout is wrong —
  STOP and report rather than shipping an empty capture.
- **No schema migration:** everything reads the existing `timeline_json` blob on demand.
  Do **not** add tables or bump schema version.
- **JSON round-trip:** demo serializes `EventDto` (PascalCase) and the deep-dive
  deserializes with `PropertyNameCaseInsensitive = true`, so demo data and real Riot
  camelCase both parse. Do not add a naming policy.
- **Enum openness:** filters compare to known strings; unknown ward/monster types simply
  don't match — never throw.
