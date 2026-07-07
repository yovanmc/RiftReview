# RiftReview — North Star (2026-07-07 grilling session)

## What this builds up to be (end-state vision, owner-locked 2026-07-07)
**RiftReview, finished, is the permanent record of the owner's League career and the instrument he reviews it with — eventually with a coaching layer on top.**

Three stages, each gated on the one before it proving out in real use:
1. **The workbench (queued: M11–M17).** One-action post-session sync, then the explorer: lane diffs, head-to-head, timeline causality, and the scrubber stepping all ten players across the map minute-by-minute with event pins. Data-honest to the end — a real number under every verdict, nothing fabricated, no composite scores.
2. **Goals & trend tracking (post-M17, post-usage).** The owner picks focus areas ("improve early warding"); the app tracks the relevant honest numbers across games and shows trajectory. It measures what the owner chose to work on — it still doesn't editorialize.
3. **The coaching layer (far horizon, named deliberately).** The app surfaces patterns itself ("you die between 10–14min in 40% of losses"). This is the auto-callout layer kept un-queued until now — **on record as the ultimate direction**, but it only gets built after stages 1–2 have sustained real usage, and it must inherit the data-honesty identity: pattern claims carry their sample sizes and never dress correlation as instruction.

**Permanent record is an end-state requirement, not a vibe.** LP history and match detail are unrecoverable once missed, so the DB joins Curio-progress in the "irreplaceable personal data" tier: backup rotation, NAS archival, and gap-free capture become features. **Hard dependency this creates: continuous capture and daily-expiring dev keys don't coexist** — the Riot production/personal-product key investigation (currently an unverified idea in the friction milestone) is promoted to prerequisite for the permanent-record ambition; if no longer-lived key exists, the fallback is a sync ritual robust enough that gaps stay rare.

**Never:** composite scores, fabricated/interpolated data, external build-site aggregation, multi-user, commercial anything.

## Path to v-final (rough build outline, 2026-07-07)
Ordering rationale: the queued M11–M17+U1 sequence stands (owner-locked); this outline wraps it with the permanent-record infrastructure the vision added, and stages the two post-workbench layers.

**Phase 0 — Friction + key strategy (first, already locked).**
What: deployed build in `C:\Self Apps\RiftReview\`; one-action sync flow; **resolve the key question** — investigate Riot personal-product registration for a persistent key (unverified whether granted for personal tools); if unavailable, script re-keying to a single prompt. Why: promoted to prerequisite — the permanent-record ambition dies without cheap, reliable sync.

**Phase 1 — Workbench completion: M11 → M12 → M13, then U1 with no gap.** As locked, tripwire armed. Why: the owner's completeness bar for daily use.

**Phase 2 — Permanent-record infrastructure.**
What: scheduled or semi-automatic sync (headless sync mode + Task Scheduler, or a sync-on-launch ritual) so capture is gap-free; gap detection that *warns* when unsynced games approach Riot's history horizon (match-v5 retention window — **unverified, verify before building**; the warning threshold depends on it); DB backup rotation + NAS archival path once the DS423+ exists. Why: LP/match history is unrecoverable — the record is now irreplaceable personal data, same tier as Curio progress. How: reuse the existing sync path; the new work is orchestration + monitoring, not ingestion.

**Phase 3 — Map + scrubber (M15 → M16 → M17).** As roadmapped: M15 opens with position-granularity + map-asset-licensing verification; scrubber v1 = board-state stepper + event pins, bounded by Riot's 60s-frame granularity (accepted). Why after U1: the dream earns its build through demonstrated usage.

**Phase 4 — Goals & trend tracking.**
What: owner-defined focus areas bound to metrics the workbench already computes; trajectory views with min-sample gates (no verdicts on 3 games). Why: cheapest path to "am I improving at the thing I chose" — reuses existing honest numbers, adds no new data source. How: a `focus_area` entity mapping to existing metric queries + a trend strip.

**Phase 5 — The coaching layer (far horizon).**
What: pattern mining over the accumulated archive ("you die 10–14min in 40% of losses, n=32") — surfaced, sample-sized, never prescriptive beyond the statistic. Why last: it's only honest over a large archive, which Phase 2 is quietly building the whole time. Gate: sustained workbench usage + owner explicitly asking for it.

## North Star (operating identity)
A **data-honest, explorer-first post-game self-coach** for an active League player. The owner plays several games a week (confirmed this session), so the premise is alive. Success metric: real games reviewed per week during and after U1 — not milestones shipped.

## Owner decisions locked this session
1. **Premise confirmed:** actively playing, several games/week.
2. **Completeness bar KEPT (owner call, made against the critique):** M11 (lane gold+XP diff) + M12 (head-to-head) + M13 (timeline explorer) ship before the U1 usage window. Recorded honestly: this bar was set 2026-07-03 without ever reviewing a real game on M6–M10 (the real DB's last write, 2026-06-19, predates M9/M10). It is a hypothesis about the owner's own behavior, chosen knowingly.
3. **Mitigation attached to that choice — friction falls first.** Before or alongside M11:
   - **Deploy milestone:** packaged build in `C:\Self Apps\RiftReview\` per the owner's own convention. "Usage" must not mean `dotnet run` from a dev tree.
   - **Key workflow:** one-action sync-day flow. Investigate whether a registered Riot personal-product key outlives the daily dev key (**unverified** — investigation item, not a fact). If not, script the manual re-key to a single prompt.

## Roadmap (in order)
1. Friction milestone (deploy + key workflow) — small, ships first.
2. M11 → M12 → M13 as planned (M11 opens with the per-participant-XP verification already flagged in the roadmap).
3. **U1 starts the week M13 merges — no gap.** Three-week usage window, owner reviews after every session, blockers fixed immediately, everything else logged to the M14 backlog. This sequencing promise is the price of keeping the bar.
4. M14 usage-fed fixes: backlog seeded ONLY by U1 findings (speculative work stays declined, as already decided 07-03).
5. M15–M17 (map foundation, ward map, scrubber) stay behind U1. The scrubber is the dream; it earns its build through demonstrated usage, not enthusiasm. M15 opens with the position-granularity + map-asset-licensing verification as documented.

## The honesty tripwire
**If M11–M13 land and real usage still doesn't happen within the U1 window, the bar was never the blocker.** The next move at that point is NOT M15–M17 — it is a re-grill of the project (is the friction elsewhere? has play interest moved?). Write the answer down before building anything else.

## Hygiene backlog
- CLAUDE.md and older ROADMAP notes still say "No CI" — stale since 2026-07-02; fix on next touch.
- `.m2shots/.m3shots/.m4shots` untracked-vs-documented discrepancy (already flagged for M11).

## Non-goals (standing, reaffirmed)
- Auto-callout coaching layer (possible later, not queued).
- Composite scores, external build aggregators, fabricated sparse data.
- Any commercial/multi-user direction.

## Park criteria
If League play stops for a season, park cleanly (like Lucid) — a self-coaching app without games is moot, and that's a life change, not a project failure. If U1 completes with fewer than ~6 real review sessions, freeze features and treat the tripwire above as triggered.
