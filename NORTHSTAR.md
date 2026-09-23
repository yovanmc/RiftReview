# RiftReview — North Star

## What this builds up to be (end-state vision)
**RiftReview, finished, is the permanent record of Yovan's League career and the instrument he reviews it with — eventually with a coaching layer on top.**

Three stages, each gated on the one before it proving out in real use:
1. **The workbench.** One-action post-session sync and a post-game card, then the explorer (M11 to M17): lane diffs, head-to-head, timeline causality, and the scrubber stepping all ten players across the map minute-by-minute with event pins. Data-honest to the end — a real number under every verdict, nothing fabricated, no composite scores.
2. **Goals & trend tracking (after the workbench, post-usage).** Yovan picks focus areas ("improve early warding"); the app tracks the relevant honest numbers across games and shows trajectory. It measures what Yovan chose to work on — it still doesn't editorialize.
3. **The coaching layer (far horizon, named deliberately).** The app surfaces patterns itself ("you die between 10–14min in 40% of losses"). This is the auto-callout layer, **on record as the ultimate direction**, but it only gets built after stages 1–2 have sustained real usage, and it must inherit the data-honesty identity: pattern claims carry their sample sizes and never dress correlation as instruction.

**Permanent record is an end-state requirement, not a vibe.** LP history and match detail are unrecoverable once missed, so the DB joins Curio-progress in the "irreplaceable personal data" tier: backup rotation, NAS archival, and gap-free capture become features. **Hard dependency this creates: continuous capture and daily-expiring dev keys don't coexist** — the Riot production/personal-product key investigation (an unverified item in the F0 friction milestone) is a prerequisite for the permanent-record ambition; if no longer-lived key exists, the fallback is a sync ritual robust enough that gaps stay rare.

**Never:** composite scores, fabricated/interpolated data, external build-site aggregation, multi-user, commercial anything.

## Path to v-final (rough build outline)
Ordering rationale: usage comes before the explorer. This outline wraps the roadmap order with the permanent-record infrastructure and stages the two post-workbench layers.

**Phase 0 — Friction + key strategy (F0).**
What: deployed build in `C:\Self Apps\RiftReview\`; one-action sync flow; **resolve the key question** — investigate Riot personal-product registration for a persistent key (unverified whether granted for personal tools); if unavailable, script re-keying to a single prompt. Why: a prerequisite, because the permanent-record ambition dies without cheap, reliable sync.

**Phase 1: a usable review, then U1.** R0 real-game test, B5 deep-dive load, B3a DB safety and the REC post-game card on manual sync, then the U1 usage window. Tripwire armed. Why: real usage decides what the workbench needs next. After U1, its log picks between M11 to M13 (lane diff, head-to-head, timeline explorer), a phone card and the map/scrubber arc.

**Phase 2 — Permanent-record infrastructure.**
What: scheduled or semi-automatic sync (headless sync mode + Task Scheduler, or a sync-on-launch ritual) so capture is gap-free; gap detection that *warns* when unsynced games approach Riot's history horizon (match-v5 retention window — **unverified, verify before building**; the warning threshold depends on it); DB backup rotation + NAS archival path (B3b), with the B1 capture service if U1 shows capture gaps. Why: LP/match history is unrecoverable — the record is now irreplaceable personal data, same tier as Curio progress. How: reuse the existing sync path; the new work is orchestration + monitoring, not ingestion.

**Phase 3 — Map + scrubber (M15 → M16 → M17).** M15 opens with position-granularity + map-asset-licensing verification; scrubber v1 = board-state stepper + event pins, bounded by Riot's 60s-frame granularity (accepted). Why after U1: the dream earns its build through demonstrated usage.

**Phase 4 — Goals & trend tracking.**
What: user-defined focus areas bound to metrics the workbench already computes; trajectory views with min-sample gates (no verdicts on 3 games). Why: cheapest path to "am I improving at the thing I chose" — reuses existing honest numbers, adds no new data source. How: a `focus_area` entity mapping to existing metric queries + a trend strip.

**Phase 5 — The coaching layer (far horizon).**
What: pattern mining over the accumulated archive ("you die 10–14min in 40% of losses, n=32") — surfaced, sample-sized, never prescriptive beyond the statistic. Why last: it's only honest over a large archive, which Phase 2 is quietly building the whole time. Gate: sustained workbench usage + Yovan explicitly asking for it.

## North Star (operating identity)
A **data-honest, explorer-first post-game self-coach** for an active League player. Yovan plays several games a week, so the premise is alive. **Status: PARKED**, planning only, no build. Success metric: real games reviewed per week during and after U1 — not milestones shipped.

## Decisions
1. **Premise confirmed:** actively playing, several games/week.
2. **Usage before the explorer:** M11 (lane gold+XP diff), M12 (head-to-head) and M13 (timeline explorer) are off the pre-usage path. R0, F0, B5, B3a and REC ship before the U1 usage window, and the U1 log decides what follows.
3. **Friction falls first (F0):**
   - **Deploy milestone:** packaged build in `C:\Self Apps\RiftReview\` per Yovan's own convention. "Usage" must not mean `dotnet run` from a dev tree.
   - **Key workflow:** one-action sync-day flow. Investigate whether a registered Riot personal-product key outlives the daily dev key (**unverified** — investigation item, not a fact). If not, script the manual re-key to a single prompt.

## Roadmap (in order)
1. R0 real-game test: review one real game on the Debug build before any code.
2. F0 friction milestone (deploy + key workflow), then B5 deep-dive load, B3a DB safety and the REC post-game card.
3. **U1 usage window.** Three weeks, a review after every session, blockers fixed immediately, everything else logged to the M14 backlog. B4 (MCP read surface) may run alongside it.
4. M14 usage-fed fixes: backlog seeded ONLY by U1 findings (speculative work stays declined).
5. The U1 log then picks between M11 to M13, a phone card and M15 to M17 (map foundation, ward map, scrubber). The scrubber is the dream. It earns its build through demonstrated usage, not enthusiasm. M11 opens with the per-participant-XP verification and M15 with the position-granularity + map-asset-licensing verification.

## The honesty tripwire
**If the pre-U1 rows ship and real usage still does not happen within the U1 window, friction was never the blocker.** The next move at that point is NOT M11 to M17. It is a re-examination of the project (is the friction elsewhere? has play interest moved?). Write the answer down before building anything else.

## Hygiene backlog
- `.m2shots` to `.m6shots` are untracked while CLAUDE.md documents the harness (roadmap row H).

## Non-goals (standing, reaffirmed)
- Auto-callout coaching layer (possible later, not queued).
- Composite scores, external build aggregators, fabricated sparse data.
- Any commercial/multi-user direction.

## Park criteria
If League play stops for a season, park cleanly (like Lucid) — a self-coaching app without games is moot, and that's a life change, not a project failure. If U1 completes with fewer than ~6 real review sessions, freeze features and treat the tripwire above as triggered.
