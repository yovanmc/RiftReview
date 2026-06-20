# RiftReview M6 — "You vs Your Rank" on the Graphs — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to
> implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.
> **Written for Sonnet execution; if something doesn't match the described code shape, STOP and
> report rather than guess.** Recon of the current code is embedded inline per task; if a file's
> actual signature differs from what's quoted, STOP.

**Goal:** Render a rank-relative baseline (you vs your current rank) on the deep-dive CS-pace
chart and the Trends sparklines, with a Rank⇄Own toggle and a "Compare vs <tier>" selector —
backed by a sparse, owner-editable, never-fabricate seed table.

**Architecture:** Pure baseline table + provider in `RiftReview.Core` (fully unit-tested); a
flat baseline line added to the `Sparkline` and `LineChart` controls; `TrendsViewModel` /
`MetricTrendViewModel` / `DeepDiveViewModel` resolve the current tier and feed baselines + deltas
into the UI. No schema/migration/Riot changes.

**Tech Stack:** C#/.NET 10, WPF + WPF-UI, CommunityToolkit.Mvvm, System.Text.Json, xUnit.

**Conventions:** build `dotnet build RiftReview.slnx -v minimal`; test `dotnet test
RiftReview.slnx`; demo `dotnet run --project src/RiftReview.App -- --seed-demo`. Commit author
is the repo default (`yovanmc`); every commit ends with:
`Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`. NO `--author`. No secrets; only
`appsettings.json` placeholders.

**Canonical roles:** Riot `TeamPosition` is `TOP | JUNGLE | MIDDLE | BOTTOM | UTILITY`. We map to
`TOP | JUNGLE | MID | ADC | SUPPORT`. **Before Task 2, grep the DB layer to confirm what string
`MatchRow.MyTeamPosition` actually stores** (`rg -n "TeamPosition" src/RiftReview.Core`). If it
stores already-shortened values (e.g. "MID"), adjust `CanonicalRole` accordingly and note it in
the commit. If it stores something unexpected, STOP and report.

---

### Task 1: `RankBaselineTable` + `RankBaselineMeta` models

**Files:**
- Create: `src/RiftReview.Core/Data/RankBaselineTable.cs`
- Test: `tests/RiftReview.Core.Tests/RankBaselineTableTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using RiftReview.Core.Data;
using Xunit;

public class RankBaselineTableTests
{
    [Fact]
    public void Table_exposes_meta_and_nested_cells()
    {
        var cells = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>>>
        {
            ["MID"] = new Dictionary<string, IReadOnlyDictionary<string, double>>
            {
                ["GOLD"] = new Dictionary<string, double> { ["cs10"] = 60.0 }
            }
        };
        var table = new RankBaselineTable(new RankBaselineMeta("src", "16.12", true), cells);

        Assert.True(table.Meta.Approximate);
        Assert.Equal(60.0, table.Cells["MID"]["GOLD"]["cs10"]);
    }
}
```

- [ ] **Step 2: Run test to verify it fails** — `dotnet test RiftReview.slnx` → FAIL (type missing).

- [ ] **Step 3: Implement**

```csharp
namespace RiftReview.Core.Data;

public sealed record RankBaselineMeta(string Source, string Patch, bool Approximate);

public sealed record RankBaselineTable(
    RankBaselineMeta Meta,
    // role -> tier -> metricKey -> value
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>>> Cells);
```

- [ ] **Step 4: Run test to verify it passes** — `dotnet test RiftReview.slnx`.

- [ ] **Step 5: Commit**

```bash
git add src/RiftReview.Core/Data/RankBaselineTable.cs tests/RiftReview.Core.Tests/RankBaselineTableTests.cs
git commit -m "feat(core): RankBaselineTable model

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: `RankBaselineProvider` — canonicalize, normalize, resolve

**Files:**
- Create: `src/RiftReview.Core/Analysis/RankBaselineProvider.cs`
- Test: `tests/RiftReview.Core.Tests/RankBaselineProviderTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Collections.Generic;
using RiftReview.Core.Analysis;
using RiftReview.Core.Data;
using Xunit;

public class RankBaselineProviderTests
{
    private static RankBaselineTable Table() => new(
        new RankBaselineMeta("seed", "16.12", true),
        new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>>>
        {
            ["MID"]     = new Dictionary<string, IReadOnlyDictionary<string, double>>
                          { ["GOLD"] = new Dictionary<string, double> { ["cs10"] = 60.0 } },
            ["SUPPORT"] = new Dictionary<string, IReadOnlyDictionary<string, double>>
                          { ["GOLD"] = new Dictionary<string, double>() }, // CS intentionally absent
        });

    [Theory]
    [InlineData("MIDDLE", "MID")]
    [InlineData("BOTTOM", "ADC")]
    [InlineData("UTILITY", "SUPPORT")]
    [InlineData("TOP", "TOP")]
    [InlineData("JUNGLE", "JUNGLE")]
    public void CanonicalRole_maps_riot_positions(string raw, string expected)
        => Assert.Equal(expected, RankBaselineProvider.CanonicalRole(raw));

    [Theory]
    [InlineData("GOLD", "GOLD")]
    [InlineData("MASTER", "DIAMOND")]       // apex reuses the highest seeded row
    [InlineData("GRANDMASTER", "DIAMOND")]
    [InlineData("CHALLENGER", "DIAMOND")]
    public void NormalizeTier_collapses_apex(string raw, string expected)
        => Assert.Equal(expected, RankBaselineProvider.NormalizeTier(raw));

    [Fact]
    public void Resolve_returns_seeded_value() =>
        Assert.Equal(60.0, RankBaselineProvider.Resolve(Table(), "MIDDLE", "GOLD", "cs10"));

    [Fact]
    public void Resolve_returns_null_for_absent_metric() =>   // never fabricate
        Assert.Null(RankBaselineProvider.Resolve(Table(), "UTILITY", "GOLD", "cs10"));

    [Fact]
    public void Resolve_returns_null_for_unranked_tier() =>
        Assert.Null(RankBaselineProvider.Resolve(Table(), "MIDDLE", "UNRANKED", "cs10"));
}
```

- [ ] **Step 2: Run to verify FAIL.**

- [ ] **Step 3: Implement**

```csharp
using RiftReview.Core.Data;

namespace RiftReview.Core.Analysis;

public static class RankBaselineProvider
{
    private static readonly string[] NonApexHighToLow =
        { "DIAMOND", "EMERALD", "PLATINUM", "GOLD", "SILVER", "BRONZE", "IRON" };

    public static string CanonicalRole(string teamPosition) => (teamPosition ?? "").ToUpperInvariant() switch
    {
        "MIDDLE" or "MID" => "MID",
        "BOTTOM" or "BOT" or "ADC" => "ADC",
        "UTILITY" or "SUPPORT" or "SUPP" => "SUPPORT",
        "TOP" => "TOP",
        "JUNGLE" or "JNG" or "JUNG" => "JUNGLE",
        var other => other, // unknown passthrough; Resolve will miss -> null (caller handles)
    };

    public static string NormalizeTier(string tier) => (tier ?? "").ToUpperInvariant() switch
    {
        "MASTER" or "GRANDMASTER" or "CHALLENGER" => "DIAMOND",
        var t => t,
    };

    public static double? Resolve(RankBaselineTable table, string teamPosition, string tier, string metricKey)
    {
        var role = CanonicalRole(teamPosition);
        var normTier = NormalizeTier(tier);
        if (table.Cells.TryGetValue(role, out var byTier)
            && byTier.TryGetValue(normTier, out var byMetric)
            && byMetric.TryGetValue(metricKey, out var v))
            return v;
        return null;
    }
}
```

- [ ] **Step 4: Run to verify PASS.**

- [ ] **Step 5: Commit** (`feat(core): RankBaselineProvider — canonicalize/normalize/resolve` + trailer).

---

### Task 3: Seed JSON resource + loader

**Files:**
- Create: `src/RiftReview.Core/Data/rank-baselines.json`
- Create: `src/RiftReview.Core/Data/RankBaselineLoader.cs`
- Modify: `src/RiftReview.Core/RiftReview.Core.csproj` (embed the json)
- Test: `tests/RiftReview.Core.Tests/RankBaselineLoaderTests.cs`

- [ ] **Step 1: Create the seed JSON** (`src/RiftReview.Core/Data/rank-baselines.json`). Replace
  `PATCH` with the current patch string at build time is NOT required — ship the literal below;
  the owner edits it later.

```json
{
  "meta": { "source": "community CS/min heuristics", "patch": "16.12", "approximate": true },
  "cells": {
    "TOP":    { "IRON": {"cs10":45,"csPerMin":4.5}, "BRONZE": {"cs10":50,"csPerMin":5.0}, "SILVER": {"cs10":55,"csPerMin":5.5}, "GOLD": {"cs10":60,"csPerMin":6.0}, "PLATINUM": {"cs10":65,"csPerMin":6.5}, "EMERALD": {"cs10":68,"csPerMin":6.8}, "DIAMOND": {"cs10":72,"csPerMin":7.2} },
    "MID":    { "IRON": {"cs10":45,"csPerMin":4.5}, "BRONZE": {"cs10":50,"csPerMin":5.0}, "SILVER": {"cs10":55,"csPerMin":5.5}, "GOLD": {"cs10":60,"csPerMin":6.0}, "PLATINUM": {"cs10":65,"csPerMin":6.5}, "EMERALD": {"cs10":68,"csPerMin":6.8}, "DIAMOND": {"cs10":72,"csPerMin":7.2} },
    "ADC":    { "IRON": {"cs10":45,"csPerMin":4.5}, "BRONZE": {"cs10":50,"csPerMin":5.0}, "SILVER": {"cs10":55,"csPerMin":5.5}, "GOLD": {"cs10":60,"csPerMin":6.0}, "PLATINUM": {"cs10":65,"csPerMin":6.5}, "EMERALD": {"cs10":68,"csPerMin":6.8}, "DIAMOND": {"cs10":72,"csPerMin":7.2} },
    "JUNGLE": { "IRON": {"cs10":40,"csPerMin":4.0}, "BRONZE": {"cs10":45,"csPerMin":4.5}, "SILVER": {"cs10":50,"csPerMin":5.0}, "GOLD": {"cs10":55,"csPerMin":5.5}, "PLATINUM": {"cs10":58,"csPerMin":5.8}, "EMERALD": {"cs10":60,"csPerMin":6.0}, "DIAMOND": {"cs10":64,"csPerMin":6.4} },
    "SUPPORT": { "IRON": {}, "BRONZE": {}, "SILVER": {}, "GOLD": {}, "PLATINUM": {}, "EMERALD": {}, "DIAMOND": {} }
  }
}
```

- [ ] **Step 2: Embed it.** In `src/RiftReview.Core/RiftReview.Core.csproj`, inside an
  `<ItemGroup>`:

```xml
<EmbeddedResource Include="Data/rank-baselines.json" />
```

(If the csproj uses default-globbing that already includes it, this is harmless; if a build
error about duplicate items appears, remove the explicit line. STOP and report if unclear.)

- [ ] **Step 3: Write the failing loader test**

```csharp
using RiftReview.Core.Data;
using Xunit;

public class RankBaselineLoaderTests
{
    [Fact]
    public void Loads_embedded_seed_with_gold_mid_cs10()
    {
        var table = RankBaselineLoader.Load();
        Assert.True(table.Meta.Approximate);
        Assert.Equal(60.0, table.Cells["MID"]["GOLD"]["cs10"]);
        Assert.False(table.Cells["SUPPORT"]["GOLD"].ContainsKey("cs10")); // support CS intentionally absent
    }
}
```

- [ ] **Step 4: Run to verify FAIL.**

- [ ] **Step 5: Implement the loader**

```csharp
using System.Reflection;
using System.Text.Json;

namespace RiftReview.Core.Data;

public static class RankBaselineLoader
{
    private static RankBaselineTable? _cached;

    public static RankBaselineTable Load()
    {
        if (_cached is not null) return _cached;

        var asm = typeof(RankBaselineLoader).Assembly;
        // Resource name ends with "Data.rank-baselines.json" regardless of root namespace.
        var resName = asm.GetManifestResourceNames()
            .Single(n => n.EndsWith("rank-baselines.json", System.StringComparison.Ordinal));
        using var stream = asm.GetManifestResourceStream(resName)!;
        using var doc = JsonDocument.Parse(stream);

        var root = doc.RootElement;
        var m = root.GetProperty("meta");
        var meta = new RankBaselineMeta(
            m.GetProperty("source").GetString() ?? "",
            m.GetProperty("patch").GetString() ?? "",
            m.GetProperty("approximate").GetBoolean());

        var cells = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>>>();
        foreach (var role in root.GetProperty("cells").EnumerateObject())
        {
            var byTier = new Dictionary<string, IReadOnlyDictionary<string, double>>();
            foreach (var tier in role.Value.EnumerateObject())
            {
                var byMetric = new Dictionary<string, double>();
                foreach (var metric in tier.Value.EnumerateObject())
                    byMetric[metric.Name] = metric.Value.GetDouble();
                byTier[tier.Name] = byMetric;
            }
            cells[role.Name] = byTier;
        }
        _cached = new RankBaselineTable(meta, cells);
        return _cached;
    }
}
```

- [ ] **Step 6: Run to verify PASS.** If the resource name `.Single(...)` throws, list
  `asm.GetManifestResourceNames()` in the failure and STOP — the embed didn't take.

- [ ] **Step 7: Commit** (`feat(core): embed rank-baseline seed table + loader` + trailer).

---

### Task 4: Add a flat baseline line to the `Sparkline` control

**Files:**
- Modify: `src/RiftReview.App/Controls/Sparkline.cs`

Recon: `Sparkline` renders ONE series (`Values` double?[] preferred over legacy `Points` int?[]),
scaling to the series min/max in `OnRender`. We add an optional flat baseline.

- [ ] **Step 1: Add dependency properties** (mirror the existing `Values`/`Points` DP pattern in
  the file — same `Register` + `AffectsRender` style):

```csharp
public static readonly DependencyProperty BaselineProperty = DependencyProperty.Register(
    nameof(Baseline), typeof(double?), typeof(Sparkline),
    new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

public double? Baseline
{
    get => (double?)GetValue(BaselineProperty);
    set => SetValue(BaselineProperty, value);
}

public static readonly DependencyProperty BaselineBrushProperty = DependencyProperty.Register(
    nameof(BaselineBrush), typeof(Brush), typeof(Sparkline),
    new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

public Brush? BaselineBrush
{
    get => (Brush?)GetValue(BaselineBrushProperty);
    set => SetValue(BaselineBrushProperty, value);
}
```

- [ ] **Step 2: Render the baseline in `OnRender`.** After the series polyline is drawn, and
  using the SAME `min`/`max`/`height`/`width` scaling the existing code computes for the series,
  add (guard against divide-by-zero exactly as the existing series code does):

```csharp
if (Baseline is double b && BaselineBrush is Brush bb && max > min)
{
    double y = height - (b - min) / (max - min) * height;
    if (y >= 0 && y <= height) // only draw if the baseline is within the rendered band
    {
        var pen = new Pen(bb, 1) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
        pen.Freeze();
        dc.DrawLine(pen, new Point(0, y), new Point(width, y));
    }
}
```

**IMPORTANT:** reuse the variable names the existing `OnRender` already computes for series
scaling. If they differ (e.g. the code calls them `lo`/`hi`/`w`/`h`), adapt to those — do NOT
introduce a second, inconsistent scaling or the baseline will not line up with the series. If the
min/max is computed only over the series excluding the baseline, that's correct and intended
(baseline may sit above/below; the `y >= 0 && y <= height` guard hides it when off-band).

- [ ] **Step 3: Verify build** — `dotnet build RiftReview.slnx -v minimal` (no unit test for a
  drawing control; visual proof comes from the Task 10 screenshot gate).

- [ ] **Step 4: Commit** (`feat(app): Sparkline optional flat baseline line` + trailer).

---

### Task 5: `MetricTrendViewModel` — baseline fields

**Files:**
- Modify: `src/RiftReview.App/ViewModels/MetricTrendViewModel.cs`
- Test: `tests/RiftReview.App.Tests/MetricTrendViewModelTests.cs` (create if the App test project
  exists; if there is NO App test project, SKIP the test file and rely on the VM-level test in
  Task 6 / screenshot gate — note that in the commit).

Recon: `MetricTrendViewModel` currently exposes `Key, DisplayName, Current, Verdict, Delta,
IsGood, IsBad, Series`. It is constructed from a `MetricTrend` core record. We add baseline
display fields **set by the parent VM** (Task 6 computes the values; this task just holds them).

- [ ] **Step 1: Add properties.** If the class is a CommunityToolkit `ObservableObject`, use
  `[ObservableProperty]` backing fields; if it is a plain immutable VM built in a ctor, add ctor
  params / settable inits to match the file's existing style (mirror how `Current`/`Delta` are
  set). Add:

```csharp
// direction-aware comparison vs the active baseline (rank or own); null when no baseline.
public bool   HasBaseline    { get; set; }
public double? BaselineValue { get; set; }
public string BaselineLabel  { get; set; } = "";   // e.g. "Gold avg" / "Your avg"
public string DeltaVsBaseline{ get; set; } = "";   // "+0.8" / "−1.2", signed, formatted to the metric's unit
public bool   BaselineIsGood { get; set; }         // colors the badge; respects metric direction (Dir)
public bool   BaselineIsBad  { get; set; }
```

- [ ] **Step 2: (If App test project exists) failing test** — assert the fields round-trip:

```csharp
var vm = /* construct as the file's ctor requires */;
vm.HasBaseline = true; vm.BaselineLabel = "Gold avg"; vm.DeltaVsBaseline = "+0.8"; vm.BaselineIsGood = true;
Assert.True(vm.HasBaseline);
Assert.Equal("+0.8", vm.DeltaVsBaseline);
```

- [ ] **Step 3: Build/run to green.**

- [ ] **Step 4: Commit** (`feat(app): MetricTrendViewModel baseline fields` + trailer).

---

### Task 6: `TrendsViewModel` — tier resolution, compare mode, baseline wiring

**Files:**
- Modify: `src/RiftReview.App/ViewModels/TrendsViewModel.cs`
- Test: `tests/RiftReview.Core.Tests/...` is wrong project; put pure delta-formatting logic in a
  **Core** helper so it can be unit-tested. Create:
  `src/RiftReview.Core/Analysis/BaselineDelta.cs` + `tests/RiftReview.Core.Tests/BaselineDeltaTests.cs`.

The signed, direction-aware delta + good/bad classification is pure → put it in Core and test it.

- [ ] **Step 1: Failing test for the pure delta helper**

```csharp
using RiftReview.Core.Analysis;
using Xunit;

public class BaselineDeltaTests
{
    // Dir = +1 means "higher is better" (cs10, kp, kda); Dir = -1 means "lower is better" (deaths).
    [Fact]
    public void Higher_is_better_above_baseline_is_good()
    {
        var d = BaselineDelta.Compute(current: 6.8, baseline: 6.0, dir: +1, unit: "");
        Assert.Equal("+0.8", d.Text);
        Assert.True(d.IsGood);
        Assert.False(d.IsBad);
    }

    [Fact]
    public void Lower_is_better_above_baseline_is_bad()
    {
        var d = BaselineDelta.Compute(current: 6.0, baseline: 4.0, dir: -1, unit: "");
        Assert.Equal("+2.0", d.Text);   // deaths above the rank avg
        Assert.False(d.IsGood);
        Assert.True(d.IsBad);
    }

    [Fact]
    public void Percent_unit_scales_and_signs()
    {
        var d = BaselineDelta.Compute(current: 0.55, baseline: 0.50, dir: +1, unit: "%");
        Assert.Equal("+5%", d.Text);    // fractions rendered as whole-percent points
    }
}
```

- [ ] **Step 2: Run to verify FAIL.**

- [ ] **Step 3: Implement the pure helper**

```csharp
namespace RiftReview.Core.Analysis;

public readonly record struct BaselineDeltaResult(string Text, bool IsGood, bool IsBad);

public static class BaselineDelta
{
    public static BaselineDeltaResult Compute(double current, double baseline, int dir, string unit)
    {
        double raw = current - baseline;
        bool isPct = unit == "%";
        double shown = isPct ? raw * 100.0 : raw;
        string mag = isPct ? $"{System.Math.Abs(shown):0}%" : $"{System.Math.Abs(shown):0.0}";
        string sign = shown >= 0 ? "+" : "−"; // − U+2212
        string text = sign + mag;

        bool better = dir >= 0 ? raw > 0 : raw < 0;
        bool worse  = dir >= 0 ? raw < 0 : raw > 0;
        return new BaselineDeltaResult(text, better, worse);
    }
}
```

- [ ] **Step 4: Run to verify PASS.**

- [ ] **Step 5: Wire `TrendsViewModel`.** Add (mirroring its existing `[ObservableProperty]`
  style — recon shows it already injects `RiftReviewDb _db`):

```csharp
public enum CompareMode { Rank, Own }

// new observable state
[ObservableProperty] private CompareMode _compareMode = CompareMode.Rank;
[ObservableProperty] private string _compareTier = "GOLD";   // set from current rank in Load()
[ObservableProperty] private bool _rankSelectorVisible;      // false when unranked
[ObservableProperty] private string _baselineProvenance = "";
public IReadOnlyList<string> Tiers { get; } =
    new[] { "IRON","BRONZE","SILVER","GOLD","PLATINUM","EMERALD","DIAMOND","MASTER","GRANDMASTER","CHALLENGER" };
```

- [ ] **Step 6: Resolve current tier in `Load()`** (recon path):

```csharp
var snaps = _db.GetLpSnapshots();
var solo = snaps.Where(s => s.QueueType == "RANKED_SOLO_5x5")
                .OrderByDescending(s => s.TakenUtc).FirstOrDefault();
if (solo != null) { CompareTier = solo.Tier; RankSelectorVisible = true; }
else { CompareMode = CompareMode.Own; RankSelectorVisible = false; }

var table = RiftReview.Core.Data.RankBaselineLoader.Load();
BaselineProvenance = $"baseline: {table.Meta.Source} · patch {table.Meta.Patch}" +
                     (table.Meta.Approximate ? " · approximate" : "");
```

- [ ] **Step 7: Per metric, set baseline fields.** Where the loop builds each
  `MetricTrendViewModel` from a `MetricTrend`, after constructing it, compute the baseline. The
  metric's direction (`Dir`) and unit live on the Core `Def` — recon shows `ChampTrendCalculator`
  has them but they may not flow to the VM. **If `MetricTrend` does not already carry `Dir`/`Unit`,
  add them to the `MetricTrend` record and populate from `Def` (small Core change; STOP and report
  if that ripples further than the record + its construction).** Then:

```csharp
// metricVm is the MetricTrendViewModel just built; mt is the core MetricTrend; champRole is the
// dominant role for this champion's games (use the same role the Trends view already groups by;
// if none is readily available, use the most frequent MyTeamPosition across the champ's games).
double? rankBaseline = RankBaselineProvider.Resolve(table, champRole, CompareTier, mt.Key);
double? ownBaseline  = mt.Series.Where(v => v.HasValue).Select(v => v!.Value).DefaultIfEmpty().Average();
if (mt.Series.All(v => !v.HasValue)) ownBaseline = null;

double? active = CompareMode == CompareMode.Rank ? rankBaseline : ownBaseline;
if (active is double baseVal && mt.CurrentValue is double cur)  // mt must expose the raw current double
{
    var d = BaselineDelta.Compute(cur, baseVal, mt.Dir, mt.Unit);
    metricVm.HasBaseline = true;
    metricVm.BaselineValue = baseVal;
    metricVm.BaselineLabel = CompareMode == CompareMode.Rank ? $"{Cap(CompareTier)} avg" : "Your avg";
    metricVm.DeltaVsBaseline = d.Text;
    metricVm.BaselineIsGood = d.IsGood;
    metricVm.BaselineIsBad = d.IsBad;
}
else
{
    metricVm.HasBaseline = false; // rank cell absent (e.g. support cs10) -> no fabricated number
}
```

`Cap` = title-case the all-caps tier (a one-liner; or reuse `RankLadder.Format`-style casing).
If `MetricTrend` lacks a raw `CurrentValue` double (only a formatted string), add it to the record
(populate from the same selector the calculator already uses). STOP and report if that is not
contained to `MetricTrend` + `ChampTrendCalculator`.

- [ ] **Step 8: Recompute on toggle/tier change.** Add `partial void OnCompareModeChanged(...)`
  and `partial void OnCompareTierChanged(...)` that call the existing `Load()` (or a lighter
  `RecomputeBaselines()` if `Load()` is expensive — recon shows `Load()` re-reads champ games, so
  a dedicated recompute over the already-built `Metrics` is preferable; if simpler to re-`Load()`,
  that's acceptable for v1).

- [ ] **Step 9: Feed the sparkline baseline.** Ensure `MetricTrendViewModel.BaselineValue` is what
  the XAML binds the `Sparkline.Baseline` to (Task 7).

- [ ] **Step 10: Build + test green; commit** (`feat(app): Trends rank baseline + compare mode +
  delta` + trailer). Include the `BaselineDelta` core file/test in the same or a prior commit.

---

### Task 7: `TrendsView.xaml` — baseline line, delta badge, toggle, selector, provenance

**Files:**
- Modify: `src/RiftReview.App/Views/TrendsView.xaml`

- [ ] **Step 1: Sparkline baseline binding.** On the existing `<controls:Sparkline .../>` in the
  metric template, add:

```xml
Baseline="{Binding BaselineValue}"
BaselineBrush="{StaticResource HairlineBrush}"
```

- [ ] **Step 2: Delta badge.** Next to the metric's `Current` text, add a `TextBlock` bound to
  `DeltaVsBaseline`, collapsed when `HasBaseline` is false, colored by `BaselineIsGood`/`IsBad`
  (reuse the existing WinBrush/LossBrush + a DataTrigger pattern already used for verdict colors in
  this view — mirror it; do NOT invent a new brush). Append `BaselineLabel` muted, e.g.
  `+0.8  Gold avg`.

- [ ] **Step 3: Compare toggle + tier selector** in the view header (above the metric list):

```xml
<StackPanel Orientation="Horizontal" Margin="0,0,0,8">
  <!-- Rank / Own toggle: two RadioButtons styled as a segmented control, or a simple ToggleButton.
       Bind to CompareMode via a converter, OR expose two bool props (IsRankMode/IsOwnMode) on the VM
       and bind RadioButton.IsChecked to them. Mirror any existing toggle in Settings/Trends. -->
  <ComboBox ItemsSource="{Binding Tiers}" SelectedItem="{Binding CompareTier}"
            Visibility="{Binding RankSelectorVisible, Converter={StaticResource BoolToVisibility}}"
            Width="140" Margin="12,0,0,0"/>
</StackPanel>
```

If there is no existing `BoolToVisibility` converter, use the one WPF-UI/app already references
(grep the other views; mirror exactly). If the VM uses the `CompareMode` enum directly, add
`IsRankMode`/`IsOwnMode` convenience bools on the VM rather than authoring an enum converter.

- [ ] **Step 4: Provenance label** (muted, italic, small) bound to `BaselineProvenance`, placed
  under the toggle row; collapse when empty.

- [ ] **Step 5: Build** `dotnet build RiftReview.slnx -v minimal`. Visual proof in Task 10.

- [ ] **Step 6: Commit** (`feat(app): TrendsView rank-baseline UI (line, delta, toggle, selector)`
  + trailer).

---

### Task 8: Deep-dive CS-pace rank-baseline overlay

**Files:**
- Modify: `src/RiftReview.App/ViewModels/DeepDiveViewModel.cs`
- Modify (if a toggle is surfaced): `src/RiftReview.App/Views/DeepDiveView.xaml` (or wherever the
  deep-dive chart lives — grep for `CsSeries`).

Recon: `DeepDiveViewModel` builds `CsSeries = { (dd.CsPerMinute, CsLineBrush), (csBaseline,
BaselineBrush, Dashed:true) }` where `csBaseline` is the own-trailing average curve. We add a
**rank** flat line when resolvable.

- [ ] **Step 1: Resolve the rank CS/min for this match's role+current tier.** In the method that
  builds `CsSeries`, after computing `csBaseline`:

```csharp
var table = RiftReview.Core.Data.RankBaselineLoader.Load();
// current tier from latest solo snapshot (same pattern as TrendsViewModel)
var solo = _db.GetLpSnapshots().Where(s => s.QueueType == "RANKED_SOLO_5x5")
              .OrderByDescending(s => s.TakenUtc).FirstOrDefault();
double? rankCsPerMin = solo == null ? null
    : RankBaselineProvider.Resolve(table, match.MyTeamPosition, solo.Tier, "csPerMin");
```

- [ ] **Step 2: Add a flat rank series spanning the chart's minute range** (same length as
  `dd.CsPerMinute`, every point = `rankCsPerMin`), with a distinct dashed brush (e.g. the gold
  `AccentBrush`), appended to `CsSeries` only when `rankCsPerMin.HasValue`:

```csharp
if (rankCsPerMin is double r)
{
    var pts = dd.CsPerMinute.Select((p, i) => new ChartPoint(p.X, r)).ToList();
    CsSeries.Add(new ChartSeries(pts, AccentBrush, Dashed: true));
}
```

Match the actual `ChartPoint`/`ChartSeries` shapes recon quoted (`ChartSeries(IReadOnlyList<ChartPoint>
Points, Brush Stroke, bool Dashed)`); if `CsSeries` is an immutable array built in one
expression, rebuild it to include the optional third series. STOP and report if `ChartPoint`'s
constructor differs from `(x, y)`.

- [ ] **Step 3: (Optional, low-risk) brush.** If `AccentBrush` isn't already a field in the VM,
  reuse whatever brush the view exposes for gold accents, or add a `static readonly` brush mirroring
  the existing `BaselineBrush`/`LaneBrush` definitions in this file. Keep it visually distinct from
  the existing own-trailing dashed baseline.

- [ ] **Step 4: Build green. Commit** (`feat(app): deep-dive CS-pace rank-baseline overlay` +
  trailer).

(Compare-mode toggle on the deep-dive is optional for M6 — the rank line simply renders in
addition to the existing own-trailing line, both visible, distinctly colored + a tiny legend if
the view already has one. Do NOT remove the existing own-trailing baseline.)

---

### Task 9: Demo coverage

**Files:**
- Modify: `src/RiftReview.App/Demo/DemoSeeder.cs`

- [ ] **Step 1:** Confirm the demo's latest **solo** `LpSnapshot` tier is a seeded tier (GOLD).
  Recon: M5 added Gold-range solo snapshots. Grep `InsertLpSnapshot`/`RANKED_SOLO_5x5` in the
  seeder. If the latest solo snapshot is already GOLD, **no change** — note "demo already covers
  Gold rank overlay" in the commit. If it is apex/other, leave it (apex normalizes to DIAMOND seed,
  which also renders) — only act if the latest solo snapshot is UNRANKED/absent, in which case add
  one GOLD solo snapshot at the newest timestamp.

- [ ] **Step 2:** Ensure the demo's trendable champion plays a **laner** role (TOP/MID/ADC/JUNGLE)
  so a `cs10` rank baseline resolves (support would correctly show no rank line). If the demo
  trend champ is a support, add/confirm a laner trend champ exists. (Recon Task 12 of M2 added a
  trendable champ — verify its role.)

- [ ] **Step 3:** Build; run `--seed-demo` once to confirm no crash. Commit if changed
  (`chore(app): demo covers Gold rank baseline overlay` + trailer); otherwise skip.

---

### Task 10: Screenshot verification gate (real + demo) — subagent text verdict

**Files:** none (verification only). Uses `.m2shots/` harness (recon §8).

- [ ] **Step 1:** Build Release/Debug as the harness expects; launch the demo and capture the
  **Trends** page and a **deep-dive** with the CS-pace chart, in **Rank** mode, via
  `.m2shots/run_capture.ps1` (adapt the existing script; if it lacks a Trends/deep-dive step, add
  capture calls mirroring the M2/M5 pattern). Apply the known PrintWindow + DisableHWAcceleration
  workaround the prior gates used.

- [ ] **Step 2: Dispatch a Sonnet subagent** to Read the captured PNGs and return a **TEXT
  verdict** (PASS/FAIL + specifics + absolute PNG paths). Acceptance the subagent must check:
  - Trends sparklines show a **dashed baseline line** distinct from the series.
  - Each applicable metric shows a **signed delta badge** ("+0.8 Gold avg"), green/red by
    direction; metrics with no rank cell (support cs10 / kda / deaths / kp / dmg until seeded) show
    **no rank line and no fabricated number**.
  - A **Rank ⇄ Own** toggle and a **Compare vs <tier>** selector are visible; the provenance label
    ("baseline: … · approximate") is visible.
  - The deep-dive CS-pace chart shows the **rank baseline line** (distinct from the own-trailing
    dashed baseline; both present).
  - **Window chrome present** (min/max/close at top-right) — regression guard from the M4→fix
    title-bar lesson.
  - **Do NOT load PNGs into the controller context** — the subagent views them; the controller acts
    on the text verdict only. Surface an image to the user only if they explicitly ask "show me".

- [ ] **Step 3:** If FAIL, diagnose from the verdict, fix the offending view/VM, re-capture. If
  PASS, proceed to finish-the-branch.

- [ ] **Step 4 (finish):** push the M6 branch → open PR → `sleep 20` → `gh pr checks <PR#>
  --watch` → merge `--merge --delete-branch` from master → sync master. Update `ROADMAP.md` (flip
  M6 → ✅ Merged with PR #, one-line summary; add any gotchas to the decision log). Commit + push.

---

## Self-review notes (author)

- **Spec coverage:** items 1 (Task 3 seed+loader), 2 (Task 8), 3 (Tasks 4+7), 4 (Tasks 6+7), 5
  (mean-delta only — Tasks 6/7; true percentile deferred per spec §4), 6 (Tasks 6/7 tier
  selector + auto-current), 7 (audit — already measured; documented, no code), 8 (WR stays 50% —
  spec §2, so WR simply has no rank-table cell → own/50% anchor; no special code), 9 (Tasks 6/8
  Own toggle) all map to tasks.
- **Never-fabricate** is enforced structurally: absent cell → `Resolve` returns null → `HasBaseline
  = false` → no line, no badge (Tasks 2, 6, 10 acceptance).
- **Type consistency:** `RankBaselineProvider.Resolve(table, teamPosition, tier, metricKey)`,
  `BaselineDelta.Compute(current, baseline, dir, unit)`, `RankBaselineLoader.Load()` used
  identically across tasks. `MetricTrend` may need `Dir`/`Unit`/`CurrentValue` added (Task 6 Step
  7) — flagged as a contained Core change with a STOP guard.
- **Risk:** the only non-trivial unknowns are (a) the exact `MetricTrend`→VM data flow (whether
  raw current + Dir/Unit are available) and (b) `Sparkline.OnRender`'s scaling variable names —
  both have explicit STOP-and-report guards.
