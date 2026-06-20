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
