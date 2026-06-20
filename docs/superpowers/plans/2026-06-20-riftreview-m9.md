# RiftReview M9 — Build analysis + discipline

> **Written for Sonnet execution.** Each task is bite-sized with exact files, complete code, and a
> verification step. **If something in the repo does not match what this plan describes, STOP and
> report rather than guessing.** Follow the repo conventions in `ROADMAP.md` (build
> `dotnet build RiftReview.slnx -v minimal`, test `dotnet test RiftReview.slnx`, commit author =
> repo default `yovanmc` with NO `--author`, trailer `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`).

## Goal (what M9 delivers)

Three things, all inside the **local-only / data-honest / never-fabricate** identity:

1. **Own-best-build per champ (item 17)** — a "Best build" panel on the **Champions** page showing,
   for each of your **currently-practiced champions**, the **completed items you build most often**
   in your own games of that champ (in its dominant role), each annotated with its **own win rate**
   and **sample size** (`Liandry's Torment — 7 games · 71% WR`). The signal is **100% your own
   matches**; Data Dragon supplies only item **names** + the **"is this a completed item" filter**.
   No external/recommended builds (item 16 is an explicit non-goal — see Task 10).
2. **Number-under-every-verdict audit (item 18)** — a documented audit of every qualitative verdict in
   the app confirming each shows the number behind it, **plus the one code fix** the audit surfaces
   (Session Health decay reasons get their delta numbers).
3. **No-composite-score non-goal (item 19)** — enshrined in the spec + ROADMAP: RiftReview will never
   compute a single composite "RiftScore". The decomposed per-metric + number-under-every-verdict
   approach **is** the architecture.

**Out of scope (deferred):** the optional timeline mini-score (item 20) → a later milestone. Do not
build it here.

## Design decisions (owner-approved 2026-06-20)

- **Run mode:** autonomous (plan + build in one session).
- **Item metadata source:** **fetch Data Dragon item.json** (Riot's own official static dictionary,
  the same source already used for champion names). This is NOT a third-party build aggregator, so it
  honors the data-honest identity. Win-rate signal stays own-games-only.
- **Placement:** **per-champ aggregate on the Champions page.** MVP = render the best build on the
  **"Currently Practicing"** cards (bounded to ≤3 champs → bounded timeline I/O). A selectable
  all-champions build view is a future extension, explicitly out of scope here.
- **Build signal (assumption):** for a champ in its **dominant role**, gather all your matches; for
  each **completed** item compute `games` (matches whose purchase stream contains it, counted once per
  game), `wins`, `winRate = wins/games`; the "best build" = the **top 6 by `games`** (a full build is
  6 items), each shown with `winRate` + `games`. Order by `games` desc, then `winRate` desc. If the
  champ has fewer than **3 games**, show "not enough games yet" — never fabricate a build from one
  game. Build **order** is a possible future extension, not in this MVP.

## Architecture (keep Core PURE and testable)

```
Core (no WPF, unit-tested):
  DataDragon/ItemCatalogParser.cs   NEW  pure: parse item.json string -> (names, completedIds)
  DataDragon/DataDragonClient.cs    EDIT add item.json fetch (mirrors champion.json) + ItemName/IsCompleted
  Riot/Dtos/TimelineDtos.cs         EDIT add Info.Participants (puuid->participantId), additive/nullable
  Analysis/BuildModels.cs           NEW  MatchBuild, BuildItemStat, ChampionBestBuild records
  Analysis/BuildExtractor.cs        NEW  pure: TimelineDto + myPid + completedIds -> ordered completed itemIds
  Analysis/BuildAnalyzer.cs         NEW  pure: IReadOnlyList<MatchBuild> -> ChampionBestBuild
  Analysis/SessionCalculator.cs     EDIT decay reason strings gain delta numbers (the audit fix)

App (WPF):
  ViewModels/ChampPoolViewModel.cs  EDIT load best builds for practicing champs (async, off-UI-thread)
  ViewModels/BestBuildViewModel.cs  NEW  per-champ build rows for binding
  Views/ChampPoolView.xaml          EDIT render best build on practicing cards + empty/offline states
  (Demo seeder)                     EDIT ensure a practicing champ has completed-item purchases

Docs:
  docs/superpowers/specs/2026-06-20-riftreview-verdict-audit.md   NEW audit table + no-composite-score
  ROADMAP.md                        EDIT Non-goals section + decision-log entry

Harness:
  .m9shots/                         NEW clone .m8shots, page=champions; PNGs gitignored, scripts committed
```

**Why this seam:** Core does zero HTTP except the existing `DataDragonClient`. The build math
(`BuildExtractor`, `BuildAnalyzer`) is pure and unit-tested with fake timelines/builds — no network, no
DB. The App layer does the I/O orchestration (load each match's timeline, resolve my participant id,
extract items, feed the analyzer, map ids→names).

---

## Task 0 — Branch + baseline

```
cd "C:\Agent Projects\RiftReview"
git checkout master
git pull
git checkout -b m9-build-analysis
dotnet test RiftReview.slnx
```
**Expected:** clean build, all tests green (M8 baseline suite = **133**). Record the number; M9 must end
higher with everything green. If baseline is not green, STOP and report.

---

## Task 1 — Core: item.json parsing (pure) + catalog

First make the completed-item predicate **testable without network** by isolating parsing into a pure
static class, then wire it into `DataDragonClient`.

### 1a. `src/RiftReview.Core/DataDragon/ItemCatalogParser.cs` (NEW)

```csharp
using System.Text.Json;

namespace RiftReview.Core.DataDragon;

/// <summary>
/// Pure parser for a Data Dragon item.json document. Produces an itemId-&gt;name map and the set of
/// itemIds that count as "completed build items" (legendaries / support items) on Summoner's Rift.
/// Validated against live item.json 16.12.1 (706 items -&gt; 115 completed). The predicate is
/// STRUCTURAL, not version-pinned.
/// </summary>
public static class ItemCatalogParser
{
    public sealed record Catalog(
        IReadOnlyDictionary<int, string> Names,
        IReadOnlySet<int> CompletedItemIds);

    /// <summary>Parse a raw item.json string. Returns empty maps on malformed input (never throws).</summary>
    public static Catalog Parse(string itemJson)
    {
        var names = new Dictionary<int, string>();
        var completed = new HashSet<int>();
        try
        {
            using var doc = JsonDocument.Parse(itemJson);
            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Object)
                return new Catalog(names, completed);

            foreach (var entry in data.EnumerateObject())
            {
                var idStr = entry.Name;                    // itemId as a string key
                if (!int.TryParse(idStr, out var id)) continue;
                var it = entry.Value;

                var name = it.TryGetProperty("name", out var n) ? (n.GetString() ?? "") : "";
                if (name.Length > 0) names[id] = StripTags(name);

                if (IsCompleted(idStr, it)) completed.Add(id);
            }
        }
        catch (JsonException) { /* malformed -> empty catalog (offline/garbage tolerant) */ }
        return new Catalog(names, completed);
    }

    // Drop 4 figured rules validated against the live file:
    //   idStr.Length <= 4        -> drop 6-digit Arena/alt-mode variant ids (e.g. 323040)
    //   maps["11"] == true       -> Summoner's Rift only
    //   gold.total >= 2000       -> legendary price floor (cleanly separates legendaries from
    //                               components AND boots; highest non-legendary terminal item is 1250)
    //   not Consumable/Trinket/Boots tag
    //   into absent or empty     -> terminal item (every finished legendary has no `into`)
    // NOTE: do NOT gate on gold.purchasable -- transform results (Muramana/Seraph's/Fimbulwinter) are
    // purchasable:false yet are real finished items; we want their PRECURSOR (which IS terminal+>=2000)
    // to count, and the predicate already does that.
    private static bool IsCompleted(string idStr, JsonElement it)
    {
        if (idStr.Length > 4) return false;

        if (!it.TryGetProperty("maps", out var maps) ||
            !maps.TryGetProperty("11", out var sr) || sr.ValueKind != JsonValueKind.True)
            return false;

        if (!it.TryGetProperty("gold", out var gold) ||
            !gold.TryGetProperty("total", out var total) ||
            total.GetInt32() < 2000)
            return false;

        if (it.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array)
            foreach (var t in tags.EnumerateArray())
            {
                var s = t.GetString();
                if (s is "Consumable" or "Trinket" or "Boots") return false;
            }

        // `into` is present only when non-empty -> a present non-empty `into` means it builds further.
        if (it.TryGetProperty("into", out var into) &&
            into.ValueKind == JsonValueKind.Array &&
            into.GetArrayLength() > 0)
            return false;

        return true;
    }

    // A few item names carry markup (e.g. Gangplank's "<rarityLegendary>...</rarityLegendary>").
    private static string StripTags(string s)
    {
        if (s.IndexOf('<') < 0) return s;
        var sb = new System.Text.StringBuilder(s.Length);
        bool inTag = false;
        foreach (var c in s)
        {
            if (c == '<') inTag = true;
            else if (c == '>') inTag = false;
            else if (!inTag) sb.Append(c);
        }
        return sb.ToString().Trim();
    }
}
```

### 1b. Extend `DataDragonClient` (EDIT `src/RiftReview.Core/DataDragon/DataDragonClient.cs`)

Read the file first. It has: `const string Base = "https://ddragon.leagueoflegends.com"`, a `Version`
property (set from `versions.json[0]` in `EnsureLoadedAsync`), `_http`, `_cacheDir`, a private
`GetChampionJsonAsync(string version, CancellationToken)` that is **disk-first then network, caching to
`{_cacheDir}/{version}/champion.json`**, a `_names` dictionary, and `public string ChampionName(int)`.

Mirror that for items. Add these members (adapt to the file's exact style/field names):

```csharp
// --- new fields, beside _names ---
private IReadOnlyDictionary<int, string> _itemNames = new Dictionary<int, string>();
private IReadOnlySet<int> _completedItems = new HashSet<int>();

// --- inside EnsureLoadedAsync, AFTER champions are loaded and `Version` is known, ---
// --- wrapped in its own try/catch so an item.json failure can't break champion names: ---
try
{
    var itemJson = await GetItemJsonAsync(Version, ct);
    var catalog = ItemCatalogParser.Parse(itemJson);
    _itemNames = catalog.Names;
    _completedItems = catalog.CompletedItemIds;
}
catch { /* offline / no cache -> item lookups degrade to fallbacks; champion names still work */ }

// --- new private fetch, mirroring GetChampionJsonAsync (disk-first, network fallback, cache write) ---
private async Task<string> GetItemJsonAsync(string version, CancellationToken ct)
{
    var path = Path.Combine(_cacheDir, version, "item.json");
    try { if (File.Exists(path)) return await File.ReadAllTextAsync(path, ct); } catch { }
    var json = await _http.GetStringAsync($"{Base}/cdn/{version}/data/en_US/item.json", ct);
    try
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, json, ct);
    }
    catch { }
    return json;
}

// --- new public lookups, mirroring ChampionName ---
public string ItemName(int itemId) =>
    _itemNames.TryGetValue(itemId, out var n) ? n : $"Item {itemId}";

public bool IsCompletedItem(int itemId) => _completedItems.Contains(itemId);

public IReadOnlySet<int> CompletedItemIds => _completedItems;

/// <summary>True once item data has loaded (false offline-with-no-cache).</summary>
public bool HasItemData => _completedItems.Count > 0;
```

> Match the file's real `using`s (`System.IO`, `System.Threading`), the exact `GetChampionJsonAsync`
> signature, and the exact `EnsureLoadedAsync` structure. If `GetChampionJsonAsync` differs materially
> from the description above, STOP and report before editing.

### 1c. Unit test (NEW `tests/.../ItemCatalogParserTests.cs` — match the existing Core test project path/namespace)

Find the existing Core test project (look for `*.Tests` referencing `RiftReview.Core`; the M8 causality
tests live there). Add:

```csharp
using RiftReview.Core.DataDragon;
using Xunit;   // or the framework the repo already uses -- match it

public class ItemCatalogParserTests
{
    // Minimal fixture: one legendary, one boots, one component, one consumable, one trinket,
    // one 6-digit mode-variant. Mirrors the real item.json field shapes.
    private const string Fixture = """
    {
      "type":"item","version":"test",
      "data":{
        "3157":{"name":"Zhonya's Hourglass","gold":{"total":3250,"purchasable":true},
                "from":["1058","2420"],"depth":3,"tags":["Armor","SpellDamage"],
                "maps":{"11":true,"12":true}},
        "3158":{"name":"Ionian Boots of Lucidity","gold":{"total":900,"purchasable":true},
                "into":["3171"],"tags":["Boots","CooldownReduction"],"maps":{"11":true}},
        "1028":{"name":"Ruby Crystal","gold":{"total":400,"purchasable":true},
                "into":["3068","1011"],"tags":["Health"],"maps":{"11":true}},
        "2003":{"name":"Health Potion","gold":{"total":50,"purchasable":true},
                "tags":["Consumable","Lane"],"maps":{"11":true}},
        "3340":{"name":"Stealth Ward","gold":{"total":0,"purchasable":true},
                "tags":["Trinket","Vision"],"maps":{"11":true}},
        "323040":{"name":"Seraph's (Arena)","gold":{"total":2900,"purchasable":true},
                "tags":["Mana"],"maps":{"30":true}}
      }
    }
    """;

    [Fact]
    public void Parse_keeps_only_completed_sr_legendaries()
    {
        var cat = ItemCatalogParser.Parse(Fixture);
        Assert.Contains(3157, cat.CompletedItemIds);          // legendary -> kept
        Assert.DoesNotContain(3158, cat.CompletedItemIds);    // boots -> dropped (tag + into)
        Assert.DoesNotContain(1028, cat.CompletedItemIds);    // component -> dropped (into + price)
        Assert.DoesNotContain(2003, cat.CompletedItemIds);    // consumable -> dropped
        Assert.DoesNotContain(3340, cat.CompletedItemIds);    // trinket -> dropped
        Assert.DoesNotContain(323040, cat.CompletedItemIds);  // 6-digit mode variant -> dropped
    }

    [Fact]
    public void Parse_maps_names_and_is_malformed_tolerant()
    {
        var cat = ItemCatalogParser.Parse(Fixture);
        Assert.Equal("Zhonya's Hourglass", cat.Names[3157]);
        Assert.Empty(ItemCatalogParser.Parse("not json").CompletedItemIds);  // never throws
    }
}
```

**Verify:** `dotnet test RiftReview.slnx` — new tests green, nothing else broken.

---

## Task 2 — Core: resolve "me" from the timeline (additive DTO)

Read `src/RiftReview.Core/Riot/Dtos/TimelineDtos.cs`. The match-v5 timeline blob (stored since M1)
contains `info.participants` (a list of `{participantId, puuid}`) — it is re-parseable with **no schema
migration**. If `TimelineDto`'s `Info` does **not** already model `Participants`, add it (additive,
nullable — same backward-compat discipline as the M7/M8 `EventDto` additions):

```csharp
// On the timeline Info record/class, append (nullable, defaulted -> existing constructions unaffected):
public sealed record TimelineParticipantDto(int ParticipantId, string? Puuid);

// ...and on InfoDto, add a nullable property/positional-with-default:
List<TimelineParticipantDto>? Participants = null
```

> If `Info` is already populated with participants under a different name, use that and skip the add.
> Deserialization is case-insensitive (`PropertyNameCaseInsensitive = true`), so `participantId`/`puuid`
> map automatically.

**Verify:** `dotnet build RiftReview.slnx -v minimal` clean.

---

## Task 3 — Core: BuildModels + BuildExtractor (pure) + tests

### 3a. `src/RiftReview.Core/Analysis/BuildModels.cs` (NEW)

```csharp
namespace RiftReview.Core.Analysis;

/// <summary>One game's completed-item build for a champ in a role (items already filtered + deduped).</summary>
public sealed record MatchBuild(int ChampionId, string Role, bool Win, IReadOnlyList<int> CompletedItems);

/// <summary>Per-item aggregate across a champ's games. WinRate is always paired with Games (sample size).</summary>
public sealed record BuildItemStat(int ItemId, int Games, int Wins, double WinRate);

/// <summary>A champ's best build = top completed items by frequency, each with its own WR + n.</summary>
public sealed record ChampionBestBuild(
    int ChampionId, string Role, int TotalGames, IReadOnlyList<BuildItemStat> Items);
```

### 3b. `src/RiftReview.Core/Analysis/BuildExtractor.cs` (NEW)

```csharp
using RiftReview.Core.Riot.Dtos;   // match the real namespace of TimelineDto/EventDto

namespace RiftReview.Core.Analysis;

public static class BuildExtractor
{
    /// <summary>
    /// My completed-item purchases from one timeline, in first-purchase order, deduped (an item counts
    /// once even if re-bought). Only ids present in <paramref name="completedItemIds"/> are kept, so
    /// components/consumables/trinkets fall away. Pure: no DB, no network.
    /// </summary>
    public static IReadOnlyList<int> CompletedItemsPurchased(
        TimelineDto tl, int myParticipantId, IReadOnlySet<int> completedItemIds)
    {
        var result = new List<int>();
        var seen = new HashSet<int>();
        var frames = tl?.Info?.Frames;
        if (frames is null) return result;

        foreach (var f in frames)
        {
            var events = f?.Events;
            if (events is null) continue;
            foreach (var e in events)
            {
                if (e.Type != "ITEM_PURCHASED") continue;
                if (e.ParticipantId != myParticipantId) continue;
                if (e.ItemId is not int id) continue;
                if (!completedItemIds.Contains(id)) continue;
                if (seen.Add(id)) result.Add(id);   // first-purchase order, deduped
            }
        }
        return result;
    }

    /// <summary>Resolve my participantId from the timeline's participants list via my puuid (or null).</summary>
    public static int? MyParticipantId(TimelineDto tl, string puuid)
    {
        var ps = tl?.Info?.Participants;
        if (ps is null || string.IsNullOrEmpty(puuid)) return null;
        foreach (var p in ps)
            if (string.Equals(p.Puuid, puuid, StringComparison.OrdinalIgnoreCase))
                return p.ParticipantId;
        return null;
    }
}
```

> Adjust `e.Type`/`e.ParticipantId`/`e.ItemId`/`tl.Info.Frames`/`f.Events` to the real member names
> confirmed in Task 1/2 recon. From the M8 digest these are exactly `EventDto.Type`,
> `EventDto.ParticipantId`, `EventDto.ItemId`, and `tl.Info.Frames.SelectMany(f => f.Events)`.

### 3c. Tests (NEW `BuildExtractorTests.cs`)

Construct a tiny `TimelineDto` in-memory (mirror how the M8 causality tests build fixture timelines —
read those first for the exact constructor shapes) with frames whose events include:
- `ITEM_PURCHASED` for me (pid 3) of a completed id (e.g. 6655) then a component id (e.g. 1028) then a
  consumable id (2003) then another completed id (3089), plus a re-buy of 6655;
- an `ITEM_PURCHASED` for an enemy (pid 7) of a completed id (must be ignored).

Assert `CompletedItemsPurchased(tl, 3, {6655,3089,3157,...})` returns exactly `[6655, 3089]` (order
preserved, component/consumable/enemy/dupes excluded). Assert `MyParticipantId(tl, "myPuuid")` returns 3.

**Verify:** `dotnet test RiftReview.slnx` green.

---

## Task 4 — Core: BuildAnalyzer (pure) + tests

### 4a. `src/RiftReview.Core/Analysis/BuildAnalyzer.cs` (NEW)

```csharp
namespace RiftReview.Core.Analysis;

public static class BuildAnalyzer
{
    /// <summary>
    /// Aggregate a champ's per-game builds into its best build: per completed item, how many of the
    /// champ's games contained it (once per game) and the win rate of those games. Top <paramref
    /// name="topN"/> by Games, then WinRate. Pure. Caller passes only this champ+role's matches.
    /// </summary>
    public static ChampionBestBuild Analyze(
        int championId, string role, IReadOnlyList<MatchBuild> matches, int topN = 6)
    {
        var games = new Dictionary<int, int>();
        var wins  = new Dictionary<int, int>();
        foreach (var m in matches)
            foreach (var id in m.CompletedItems.Distinct())   // defensive: already deduped upstream
            {
                games[id] = games.GetValueOrDefault(id) + 1;
                if (m.Win) wins[id] = wins.GetValueOrDefault(id) + 1;
            }

        var items = games.Keys
            .Select(id =>
            {
                int g = games[id], w = wins.GetValueOrDefault(id);
                return new BuildItemStat(id, g, w, g == 0 ? 0 : (double)w / g);
            })
            .OrderByDescending(s => s.Games)
            .ThenByDescending(s => s.WinRate)
            .ThenBy(s => s.ItemId)              // stable tie-break for deterministic tests
            .Take(topN)
            .ToList();

        return new ChampionBestBuild(championId, role, matches.Count, items);
    }
}
```

### 4b. Tests (NEW `BuildAnalyzerTests.cs`)

Feed e.g. 4 `MatchBuild`s for champ 103 role "MIDDLE":
- g1 win `[6655, 3157]`, g2 win `[6655, 3089]`, g3 loss `[6655, 3157]`, g4 win `[3089]`.
Assert: `TotalGames == 4`; item 6655 → Games 3, Wins 2, WinRate ≈ 0.667; item 3157 → Games 2, Wins 1,
WinRate 0.5; item 3089 → Games 2, Wins 2, WinRate 1.0; ordering = `[6655, 3089, 3157]` (3089 before
3157: equal Games=2 but higher WinRate). Assert `topN` truncation with a small N.

**Verify:** `dotnet test RiftReview.slnx` green.

---

## Task 5 — App: best-build loading wired into the Champions page

### 5a. `src/RiftReview.App/ViewModels/BestBuildViewModel.cs` (NEW)

A bindable per-item row + a small container. Match the repo's MVVM toolkit (the other VMs use
`CommunityToolkit.Mvvm` `[ObservableObject]`/`ObservableProperty` — mirror them).

```csharp
namespace RiftReview.App.ViewModels;

public sealed class BuildItemRow
{
    public string ItemName { get; init; } = "";
    public int Games { get; init; }
    public double WinRate { get; init; }
    // "7 games · 71% WR" — number ALWAYS shown beside the item (number-under-every-verdict discipline)
    public string Caption => $"{Games} game{(Games == 1 ? "" : "s")} · {WinRate:P0} WR";
}

public sealed class BestBuildViewModel
{
    public int ChampionId { get; init; }
    public string RoleLabel { get; init; } = "";
    public int TotalGames { get; init; }
    public IReadOnlyList<BuildItemRow> Items { get; init; } = System.Array.Empty<BuildItemRow>();

    public bool HasBuild => Items.Count > 0;
    // honest empty states:
    public bool NotEnoughGames { get; init; }   // <3 games
    public bool ItemDataUnavailable { get; init; } // offline, no item.json cache
    public string EmptyMessage =>
        ItemDataUnavailable ? "Item data unavailable — connect once to load item names."
        : NotEnoughGames    ? "Not enough games yet to show a build."
        : "No completed items recorded yet.";
    public string Summary => $"Best build · {RoleLabel} · {TotalGames} game{(TotalGames == 1 ? "" : "s")}";
}
```

### 5b. Build loader in `ChampPoolViewModel` (EDIT)

Read `ChampPoolViewModel.cs` and the `ChampCardViewModel`/`ChampStat` shapes. The page already computes
`pool = ChampPoolCalculator.Build(_db.AllMatches(RankedOnly))` and exposes `Practicing`
(`ChampCardViewModel`) for the top champs (each `ChampStat` carries `ChampionId` + `DominantRole`).

Add an **async, off-UI-thread** pass that computes a `BestBuildViewModel` for each practicing champ and
attaches it. Concretely:

1. Obtain the player's **puuid** the same way `DeepDiveViewModel` does (read `DeepDiveViewModel` — it
   has a `puuid` it reads from settings/account; replicate that source in `ChampPoolViewModel`,
   injecting the same dependency if needed). If puuid is unavailable, skip build loading (leave builds
   null) — do not crash.
2. Add a method:

```csharp
private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

private BestBuildViewModel BuildFor(int championId, string role, string puuid)
{
    var roleLabel = string.IsNullOrEmpty(role) ? "" : role;
    if (!_ddragon.HasItemData)
        return new BestBuildViewModel { ChampionId = championId, RoleLabel = roleLabel,
            ItemDataUnavailable = true };

    var matches = _db.AllMatches(RankedOnly)
        .Where(m => m.MyChampionId == championId &&
                    (string.IsNullOrEmpty(role) || m.MyTeamPosition == role))
        .ToList();

    var builds = new List<MatchBuild>();
    foreach (var m in matches)
    {
        var tlJson = _db.GetTimelineJson(m.MatchId);
        if (tlJson is null) continue;
        TimelineDto tl;
        try { tl = JsonSerializer.Deserialize<TimelineDto>(tlJson, Json)!; } catch { continue; }
        var pid = BuildExtractor.MyParticipantId(tl, puuid);
        if (pid is null) continue;
        var items = BuildExtractor.CompletedItemsPurchased(tl, pid.Value, _ddragon.CompletedItemIds);
        builds.Add(new MatchBuild(championId, roleLabel, m.Win, items));
    }

    var best = BuildAnalyzer.Analyze(championId, roleLabel, builds);
    if (best.TotalGames < 3)
        return new BestBuildViewModel { ChampionId = championId, RoleLabel = roleLabel,
            TotalGames = best.TotalGames, NotEnoughGames = true };

    var rows = best.Items
        .Select(s => new BuildItemRow { ItemName = _ddragon.ItemName(s.ItemId),
            Games = s.Games, WinRate = s.WinRate })
        .ToList();
    return new BestBuildViewModel { ChampionId = championId, RoleLabel = roleLabel,
        TotalGames = best.TotalGames, Items = rows };
}
```

3. In `InitializeAsync` (which already awaits `_ddragon.EnsureLoadedAsync()`), after the pool is built
   and item data is loaded, compute builds for the practicing champs **on a background thread** and
   assign them back on the UI thread:

```csharp
var puuid = /* same source DeepDiveViewModel uses */;
if (!string.IsNullOrEmpty(puuid))
{
    var practicing = Practicing.ToList();   // snapshot
    var built = await Task.Run(() => practicing
        .Select(card => (card, vm: BuildFor(card.ChampionId, card.DominantRole, puuid)))
        .ToList());
    foreach (var (card, vm) in built) card.BestBuild = vm;   // ChampCardViewModel gets a BestBuild prop
}
```

4. Add `public BestBuildViewModel? BestBuild { get; set; }` (or an `[ObservableProperty]`) to
   `ChampCardViewModel`. If `ChampCardViewModel` is immutable, change the property to settable or pass
   the build through its constructor — keep the change surgical.

> Mirror `DeepDiveViewModel`'s exact timeline-load idiom (`_db.GetTimelineJson` →
> `JsonSerializer.Deserialize<TimelineDto>` with case-insensitive options). Confirm `ChampStat` exposes
> `DominantRole` (the digest says it does); if the property name differs, use the real one.

**Verify:** `dotnet build RiftReview.slnx -v minimal` clean.

---

## Task 6 — App: render best build on the practicing cards

EDIT `src/RiftReview.App/Views/ChampPoolView.xaml`. In the "Currently Practicing" card template (the
`ChampCardViewModel` `DataTemplate`), add a "Best build" block bound to `BestBuild`. Keep it inside the
card so it sits **above the fold** (top of page) for the screenshot harness. Sketch (adapt to the file's
existing brushes/styles — reuse the same `TextBlock` styles already in the card):

```xml
<!-- inside the practicing card, below the existing stats -->
<StackPanel Margin="0,8,0,0" Visibility="{Binding BestBuild, Converter={StaticResource NullToCollapsed}}">
  <TextBlock Text="{Binding BestBuild.Summary}" FontWeight="SemiBold" Opacity="0.85"/>

  <!-- has-build state -->
  <ItemsControl ItemsSource="{Binding BestBuild.Items}"
                Visibility="{Binding BestBuild.HasBuild, Converter={StaticResource BoolToVisible}}">
    <ItemsControl.ItemTemplate>
      <DataTemplate>
        <Grid Margin="0,2,0,0">
          <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/>
          </Grid.ColumnDefinitions>
          <TextBlock Grid.Column="0" Text="{Binding ItemName}" TextTrimming="CharacterEllipsis"/>
          <TextBlock Grid.Column="1" Text="{Binding Caption}" Opacity="0.7" Margin="8,0,0,0"/>
        </Grid>
      </DataTemplate>
    </ItemsControl.ItemTemplate>
  </ItemsControl>

  <!-- empty/insufficient/offline state -->
  <TextBlock Text="{Binding BestBuild.EmptyMessage}" Opacity="0.6"
             Visibility="{Binding BestBuild.HasBuild, Converter={StaticResource InverseBoolToVisible}}"/>
</StackPanel>
```

If the listed converters don't already exist in the app's resources, use whichever null/bool→visibility
converters the codebase already provides (grep the other views), or add minimal ones consistent with
existing converters. Do not invent a new theming approach — reuse the black-glass + Hextech-gold styles.

**Verify:** `dotnet build RiftReview.slnx -v minimal` clean. (Visual check happens in Task 11.)

---

## Task 7 — Demo seeder: a practicing champ with a real build

The screenshot gate runs `--seed-demo`. The build panel only renders if a **practicing** champ
(≥2 recent games per `ChampPoolCalculator`'s `minPracticeGames`) has timelines containing
**completed-item** `ITEM_PURCHASED` events. M8 already added synthetic recalls (player pid=3, backs at
min 2/8/14/20) to the demo timelines — extend those purchases to include a consistent **completed-item
core** so the aggregate is non-empty and meaningful.

1. Find the demo seeder (grep for `--seed-demo` / the M8 "synthetic recalls" code that emits
   `ITEM_PURCHASED` events; the M8 decision log says the seeder emits backs for pid 3).
2. Ensure the demo's **most-played champ** has **≥3 games** (mix of wins/losses) whose timelines each
   include `ITEM_PURCHASED` events for a core of real completed ids that pass the predicate, e.g.
   **6655 (Luden's Echo), 3157 (Zhonya's Hourglass), 3089 (Rabadon's Deathcap)** — present in most
   games, so the panel shows e.g. `Luden's Echo — 4 games · 75% WR`. Vary one item across games so the
   per-item win rates differ (proves the WR column is real, not constant). Keep the existing recall
   cadence intact (don't break M8 back-timing — these are additional purchases at sensible timestamps).
3. The item NAMES require item.json; the screenshot run is online, so names resolve. (If the seeded ids
   were arbitrary before, switch them to the real completed ids above.)

> This mirrors the M8 gotcha ("demo seeder must emit synthetic recalls or back-timing renders empty").
> The same applies to builds.

**Verify:** `dotnet run --project src/RiftReview.App -- --seed-demo` launches; navigate to Champions;
the practicing card shows a "Best build" list with item names + `N games · X% WR`. (Automated capture in
Task 11.)

---

## Task 8 — Audit fix: numbers under Session Health verdicts

The only verdict in the app lacking its number is Session Health's **decay reasons**. The deltas already
exist on `PlaySession` (`DeathsDelta`, `Cs10Delta`, `KdaDelta`). EDIT
`src/RiftReview.Core/Analysis/SessionCalculator.cs` reason-string construction so each decay reason
carries its number (streak + WR reasons already do):

```csharp
// before:  reasons.Add("deaths climbing");
// after:
if (deathsDecay) reasons.Add($"deaths climbing (+{deathsDelta:0.0}/game)");
if (csDecay)     reasons.Add($"CS@10 falling (-{cs10Delta:0})");
if (kdaDecay)    reasons.Add($"KDA falling (-{kdaDelta:0.0})");
```

> Use the actual local variable names from the file (`deathsDelta`/`cs10Delta`/`kdaDelta` per the
> recon). The sign in the displayed text should read naturally (deaths climbing = positive delta;
> CS@10/KDA falling = show the drop magnitude). If a delta can be negative-of-expected, format the
> magnitude with `Math.Abs(...)` so the caption always reads sensibly. Keep the existing thresholds and
> severity logic unchanged — only the reason **strings** gain numbers.

**Tests:** find the existing `SessionCalculator` tests (M4). Update/extend assertions so a tilted/caution
session's `Reasons` now include the numeric form (e.g. assert a reason contains `"deaths climbing (+"`).
Do not weaken existing severity assertions.

**Verify:** `dotnet test RiftReview.slnx` green.

---

## Task 9 — Docs: verdict audit + no-composite-score non-goal

### 9a. `docs/superpowers/specs/2026-06-20-riftreview-verdict-audit.md` (NEW)

```markdown
# RiftReview — Verdict audit (M9, item 18) + no-composite-score non-goal (item 19)

## Principle
Every qualitative verdict the app shows must display the number that justifies it. A label with no
number is the failure mode this audit exists to prevent ("the score told me nothing").

## Audit (every verdict surface, 2026-06-20)
| Verdict surface | Where produced | Number shown beside it | Status |
|---|---|---|---|
| Trends per-metric (Improving/Steady/Declining/Building) | ChampTrendCalculator / MetricTrendViewModel | Current value + signed Δ (+ rank Δ when present) | OK |
| Matchups (favorable/unfavorable coloring) | MatchupRowViewModel | Win% + game count beside the colored row | OK |
| Deep-dive swing ("in your favor"/"against you") | TimelineExtractor.BuildCausality / DeepDiveViewModel | Gold Δ (e.g. +1,850g) + window time | OK |
| Deep-dive vision/objectives | TimelineExtractor.BuildVisionObjectives | Exact ward counts + "3/4 · 75%" | OK |
| Session Health tilt (Tilted/Caution/Calm) | SessionCalculator | Record (W–L) + streak length; **decay reasons now carry deltas** (M9 fix) | OK (fixed in M9) |
| Best build (M9) | BuildAnalyzer | Each item shows `N games · X% WR`; <3 games => "not enough games" | OK |

**M9 change:** Session Health decay reasons gained their numbers
(`deaths climbing (+1.8/game)`, `CS@10 falling (-9)`, `KDA falling (-1.6)`).

## Non-goal: no single composite "RiftScore" (item 19)
RiftReview will **never** reduce a game or a player to one opaque composite rating. Rationale: a single
number answers "am I good?" but not "what do I fix"; even op.gg labels its OP Score beta and warns it
"may occasionally produce inaccurate results". The decomposed, per-metric, **number-under-every-verdict**
design IS the product. Any future "score-like" feature must decompose into named, individually-numbered
components (see the deferred item 20 timeline mini-score: actionable per-interval deltas, never a single
flavor grade).
```

### 9b. ROADMAP.md — add a Non-goals section (EDIT)

Add (near the Definition/Decision-log) a short **## Non-goals** section capturing: no single composite
score; no external/recommended-build or live-overlay/draft-scouting integration; never-fabricate
(sparse baselines, own-games-only builds). One or two lines each. (The decision-log entry in Task 12
records the M9 shipment.)

**Verify:** docs render (markdown); no code impact.

---

## Task 10 — `.m9shots/` screenshot harness

Clone `.m8shots/` → `.m9shots/`, retargeted to the Champions page (the practicing card with the build
panel is at the **top** of the page, so the default window capture frames it — no tall variant or
list-selection needed, unlike the deep-dive harnesses).

1. Copy the `.m8shots` scripts. Change the launch page to `champions`:
   `--seed-demo --page champions` (the `--page <review|champions|trends|matchups|sessions|climb|settings>`
   hook is read in `AppShell.OnLoaded`).
2. Keep the proven mechanics: set
   `HKCU:\Software\Microsoft\Avalon.Graphics\DisableHWAcceleration=1` before launch and **restore after**;
   capture via `.m2shots/Capturer/out/Capturer.exe` (PrintWindow `PW_RENDERFULLCONTENT`).
3. Remove the deep-dive UIAutomation `SelectionItemPattern.Select()` step (not needed — the build panel
   renders on load). Allow the app a moment for the async build load to populate before capture (poll or
   a short settle delay — match how `.m8shots` waits).
4. `.gitignore` the PNGs (`.m9shots/*.png`); **commit the scripts**.

**Verify (screenshot subagent — text verdict only, do NOT load PNGs into the controller):** run the
harness, then dispatch a Sonnet subagent to Read the produced PNG(s) and return a PASS/FAIL text verdict
against these acceptance criteria, plus the absolute PNG paths it viewed:
- The Champions page shows a practicing champion card.
- The card shows a "Best build" section with a champion-appropriate header (`Best build · <ROLE> · N games`).
- At least two item **names** are listed (not "Item 1234"), each with a `N games · X% WR` caption.
- No overlapping/clipped text; black-glass + Hextech-gold theme intact; window caption buttons present.

---

## Task 11 — Full verification + PR + merge + ROADMAP update

1. `dotnet test RiftReview.slnx` — **all green**, suite count **> 133** (record the new number).
2. `dotnet build RiftReview.slnx -v minimal` — clean.
3. Confirm the screenshot subagent returned **PASS** (Task 10). If FAIL, fix and re-verify before
   proceeding (offer to show the PNG only if the owner asks).
4. Commit in logical chunks with the standard trailer, push `m9-build-analysis`.
5. Open a PR (`gh pr create`). **This repo has no CI** (no `.github/workflows`) — the merge gate is the
   local `dotnet test` pass + the screenshot subagent PASS, same as M6/M7/M8. (Run `gh pr checks <#>
   --watch` only if checks actually exist; otherwise skip straight to merge.)
6. Merge from master: `gh pr merge <#> --merge --delete-branch`; sync master.
7. **Update `ROADMAP.md`:** flip the M9 row to `✅ Merged` with the PR # and a one-line summary; add a
   decision-log entry capturing: item-source = Data Dragon item.json (Riot's own dict, completed-item
   predicate `len≤4 & maps11 & total≥2000 & !Consumable/Trinket/Boots & into-empty`, validated 16.12.1
   → 115 items); builds computed on-demand from `timeline_json` (**no migration**, M7/M8 pattern);
   `info.participants` now modeled on `TimelineDto` to resolve "me" without loading match_json;
   transform items (Muramana/Seraph's/Fimbulwinter) shown as their purchased precursor by design;
   best build placed on practicing cards (≤3 champs, async off-UI load); Session Health decay reasons
   gained numbers; no-composite-score enshrined; suite = <new count>.
8. Commit + push the ROADMAP update.

## Acceptance criteria (definition of done)
- [ ] Champions page practicing cards show own-best-build per champ: top completed items by frequency,
      each with `N games · X% WR`; `<3 games` → honest "not enough games"; offline-no-cache → honest
      "item data unavailable".
- [ ] Build signal is 100% own-games; item.json supplies only names + the completed-item filter; no
      external/recommended-build data anywhere.
- [ ] Session Health decay reasons display their delta numbers; verdict audit doc exists; no-composite-
      score non-goal enshrined in spec + ROADMAP.
- [ ] No DB schema migration; `TimelineDto`/`EventDto` changes are additive/nullable.
- [ ] All tests green (suite > 133); screenshot subagent PASS; PR merged; ROADMAP row = ✅.

## Risk gates (STOP and report rather than guess)
- `GetChampionJsonAsync` / `EnsureLoadedAsync` shape differs from Task 1b → STOP before editing.
- `TimelineDto.Info` can't be extended with `Participants`, or the stored blobs lack `info.participants`
  → fall back to the `match_json` + `MatchExtractor.Summarize(match, puuid)` idiom (as DeepDiveViewModel
  does) to get `MyParticipantId`; note it in the decision log.
- The player puuid isn't reachable from `ChampPoolViewModel` → inject the same dependency
  `DeepDiveViewModel` uses; if that's non-trivial, STOP and report.
- Practicing-card build load is visibly slow (many large timelines) → confirm it runs off the UI thread
  (`Task.Run`) with a settle/loading state; if still slow, reduce to the single top practicing champ and
  note the cap in the decision log (no silent truncation).
```

