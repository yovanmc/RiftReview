# RiftReview map

## Purpose
Where each part of RiftReview lives, so a task opens the right files first.
Rules and commands live in CLAUDE.md, the end state in NORTHSTAR.md, open work in ROADMAP.md.

## Entry points
- `RiftReview.slnx` - solution: both src projects and both test projects
- `src/RiftReview.App/App.xaml.cs` - exe start: `--seed-demo` switch, DB path, DI registrations
- `src/RiftReview.App/AppShell.xaml.cs` - nav shell; `OnLoaded` reads `--page <name>` for captures
- `src/RiftReview.App/Demo/DemoSeeder.cs` - synthetic matches, timelines and LP snapshots for `--seed-demo`
- `src/RiftReview.Core/Sync/SyncService.cs` - pulls matches and timelines from the Riot API into SQLite
- `.github/workflows/ci.yml` - CI: restore, build `-warnaserror`, test on windows-latest

## Modules
- `src/RiftReview.Core/` - no-WPF logic: `Analysis/` (`*Calculator`, `*Extractor`, `*Models`), `Data/` (`RiftReviewDb`, rank baseline table), `Riot/` (client, rate limiter, routing, `Dtos/`), `DataDragon/`, `Sync/`, `Configuration/`
- `src/RiftReview.App/` - WPF-UI app: `Views/`, `ViewModels/`, `Controls/` (`LineChart`, `Sparkline`), `Converters/`, `Services/NavigationService.cs`, `Themes/`, `Properties/PublishProfiles/`
- `tests/RiftReview.Core.Tests/` - one `<Class>Tests.cs` per Core class; `Fixtures/` holds match and timeline JSON
- `tests/RiftReview.App.Tests/` - view model, navigation and chart tests
- `.m7shots/` - capture script: deep-dive vision and objectives
- `.m8shots/` - capture scripts: deep-dive swing band, plus a `_tall` variant
- `.m9shots/` - capture script: Champions page Best Build panel
- `.m10shots/` - capture scripts: deep-dive By game phase card, plus a `_tall` variant

## Change routes
- To add a page, start in `src/RiftReview.App/Views/` and `ViewModels/`, register it in `App.xaml.cs`, add nav in `AppShell.xaml` and `AppShell.xaml.cs`, seed it in `Demo/DemoSeeder.cs`
- To add a metric, start in `src/RiftReview.Core/Analysis/` (calculator plus its `*Models.cs`), tests in `tests/RiftReview.Core.Tests/`
- To read new timeline events, start in `src/RiftReview.Core/Riot/Dtos/TimelineDtos.cs`, then `Analysis/TimelineExtractor.cs` and `AnalysisModels.cs`, fixtures in `tests/RiftReview.Core.Tests/Fixtures/`
- To change a deep-dive card, start in `src/RiftReview.App/ViewModels/DeepDiveViewModel.cs` and `Views/DeepDiveView.xaml`, then `Demo/DemoSeeder.cs` so the demo shows it
- To change Riot API calls, start in `src/RiftReview.Core/Riot/IRiotApiClient.cs`, `RiotApiClient.cs` and `Riot/Dtos/`, tests in `tests/RiftReview.Core.Tests/RiotApiClientTests.cs`; all four fakes in `SyncServiceTests.cs` implement the interface too
- To change item data, start in `src/RiftReview.Core/DataDragon/`, tests in `DataDragonClientTests.cs` and `ItemCatalogParserTests.cs`
- To change rank baselines, start in `src/RiftReview.Core/Data/rank-baselines.json` and `RankBaselineLoader.cs`, then `Analysis/RankBaselineProvider.cs`
- To change the schema, start in `src/RiftReview.Core/Data/RiftReviewDb.cs` (`LatestSchemaVersion`), with `Sync/DerivedMetricsBackfill.cs` for derived columns
- To change a chart, start in `src/RiftReview.App/Controls/`, tests in `tests/RiftReview.App.Tests/LineChartTests.cs`
- To add a capture, copy `.m10shots/run_capture.ps1` into a new `.m<N>shots/` folder, point its `$shots` path and scenario at the new folder, and add its PNG ignore line to `.gitignore`

## Skip
- `bin/` - and `obj/`, any depth
- `.m10shots/*.png` - capture output in every `.m<N>shots/` folder, gitignored
- `.scratch/` - working files

## Docs
- `docs/MAP.md` - this file
- `docs/SCREENSHOTS.md` - screenshot verification harness: launch flags, capture scripts, capturer
