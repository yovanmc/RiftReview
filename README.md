# RiftReview

[![CI](https://github.com/yovanmc/RiftReview/actions/workflows/ci.yml/badge.svg)](https://github.com/yovanmc/RiftReview/actions/workflows/ci.yml)

Personal, single-user, read-only, non-commercial Windows desktop app for self-coaching at League of Legends. It pulls **your own** match history from the official Riot API into a local SQLite store and shows a post-game review: a single-game deep-dive (gold differential vs lane & enemy team with death markers, CS/min vs your own same-role baseline) plus a cross-game trend strip (W/L, deaths, CS@10, gold-diff@15).

Built with **.NET 10 / WPF / WPF-UI / CommunityToolkit.Mvvm / Microsoft.Data.Sqlite**. Black-glass theme + Hextech Gold accent.

> Not affiliated with or endorsed by Riot Games. Uses the official Riot API under a personal development key and respects the Riot API terms (single-user, read-only, non-commercial).

## Setup (your machine)

1. Get a Riot API key from https://developer.riotgames.com (personal **dev keys expire ~daily**).
2. Set your secrets via .NET User Secrets — **never commit these**; `appsettings.json` ships placeholders only:

   ```
   cd src/RiftReview.App
   dotnet user-secrets set "Riot:ApiKey" "RGAPI-..."
   dotnet user-secrets set "Riot:RiotId" "GameName#TAG"
   dotnet user-secrets set "Riot:Platform" "na1"
   ```

   Platform examples: `na1`, `euw1`, `eun1`, `kr`, `br1`, `jp1`, … — the regional route (americas/europe/asia) is derived automatically.

## Run

```
dotnet run --project src/RiftReview.App
```

Press **Sync** to resolve your Riot ID → PUUID and pull your last ~20 matches (incremental — already-stored matches are skipped). Select a match in the left rail to open its deep-dive.

### Demo mode (no key needed)

```
dotnet run --project src/RiftReview.App -- --seed-demo
```

Loads clearly-synthetic sample data into a throwaway demo DB so you can see the UI without a key. This is **not real match data**.

## Data & privacy

- **Read-only** — the app never writes to your Riot account.
- **Local only** — SQLite at `%LOCALAPPDATA%\RiftReview\riftreview.db`; Data Dragon cache under `%LOCALAPPDATA%\RiftReview\ddragon\`.
- Single-user.

## Manual live-verification checklist (run locally — NOT verifiable in CI)

The offline logic (routing, rate limiting, extraction math, sync, DB) is covered by unit tests, and the UI renders from synthetic data via `--seed-demo`. The one thing that can only be verified on your machine with a real key + real network is the **live Riot pull**. After setting your secrets:

- [ ] Launch the app; the rail shows the empty state ("No matches yet…").
- [ ] Press **Sync**. Your recent matches populate the rail (champion names, role, W/L, KDA).
- [ ] The trend strip shows W/L, Deaths, CS@10, Gold@15 across the games.
- [ ] Select a match — the deep-dive renders: gold differential (two lines + ✕ death markers) and CS/min (your curve + dashed same-role baseline once you have ≥3 same-role games).
- [ ] Press **Sync** again immediately — it reports mostly "already stored" (incremental skip).
- [ ] Set a bad key (`dotnet user-secrets set "Riot:ApiKey" "RGAPI-bogus"`) and Sync — confirm a friendly "key looks expired or invalid" message, not a crash.
- [ ] Restore your real key — sync works again.
- [ ] (Optional) Many rapid syncs — confirm rate-limit backoff messaging.

## Build & test

```
dotnet build RiftReview.slnx
dotnet test RiftReview.slnx
```

## Architecture

- **`RiftReview.Core`** — Riot API client (regional/platform routing split + sliding-window rate limiter), Data Dragon client (champion names, disk-cached), SQLite store (versioned `user_version` migrations), extraction math (gold lines, CS pace, deaths, scalars), incremental sync service. No WPF; fully unit-tested offline.
- **`RiftReview.App`** — WPF + WPF-UI MVVM, hand-rolled `LineChart` control, demo seeder.

## Use

Personal project, not for commercial use. Respects the Riot Games API terms of service.

## How this was built

This repo was designed, specified, and reviewed by me, and implemented through my multi-agent development workflow: AI subagents execute written plans, with adversarial review gates (plan critique, code review, test verification) between phases. Every architectural decision is mine, and the process is left visible in the history and `docs/superpowers/` on purpose.

The productized form of that workflow is [backend-harness](https://github.com/yovanmc/backend-harness). If you're evaluating my work: ask me why the rate limiter keeps a 3-second safety margin under Riot's 120-second window — I'll defend the design from first principles.
