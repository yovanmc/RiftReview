# RiftReview — Competitive Research & Product Direction

**Date:** 2026-06-20
**Scope:** How the major League of Legends analytics/coaching platforms approach player
performance analysis and improvement — and what RiftReview should (and should not) learn from
them, given its local-only / post-game / Riot-API-only / data-honest constraints.
**Method:** Two `deep-research` passes (fan-out web search → fetch → 3-vote adversarial
verification → synthesis). Pass 1 = whole landscape (capped at 20 agents). Pass 2 = the
under-covered platforms + community sentiment (106 agents, full 3-vote verification).

> **Confidence legend:** ✅ **Verified** (survived adversarial verification, cited) ·
> ◐ **Background** (author's own knowledge, NOT verified this round — treat as uncertain) ·
> ❔ **Unverified / gap** (targeted but produced no surviving claim).

---

## 0. The honest method finding (read this first)

The verification harness **verifies first-party product mechanics extremely well** (op.gg help
docs, mobalytics.gg/gpi, u.gg FAQ, lolalytics site copy, Porofessor's Overwolf listing all earned
unanimous 3-0 votes) but **systematically cannot verify community sentiment or blog-described
platform behavior** to a 2/3-refute bar — those sources have no authoritative primary and the
adversarial votes kill them. Across *both* passes, **League of Graphs, Blitz.gg, Deeplol, and the
entire "what real players say" dimension produced zero surviving claims.** That is a method ceiling,
not a budget problem: a third harness pass would not fix it. The honest tool for community
sentiment is direct reading of r/summonerschool threads, summarized as *anecdotal*.

---

## 1. Verified findings (cited)

### Scoring philosophy
- ✅ **Two camps: decomposed skill-axes vs single composite.** Mobalytics **GPI** scores 8 named
  axes — Fighting, Farming, Vision, Aggression, Toughness, Teamplay, Consistency, Versatility
  ([mobalytics.gg/gpi](https://mobalytics.gg/gpi/)). OP.GG's **OP Score** is a single 0–10 per-game
  rating that the vendor itself calls beta and *"may occasionally produce inaccurate results"*
  ([op.gg help](https://help.op.gg/hc/en-us/articles/31088715328665-OP-Score-explained)).
  **Lesson:** the decomposition answers "*what* do I fix"; the single number does not — and even
  its maker won't fully stand behind it. RiftReview's 8-metric Trends view is structurally the GPI
  decomposition, on the right side of this.
- ✅ **Rank-relative is the industry-standard normalization.** GPI is 0–100 anchored to rank tiers
  and tier-capped (rank-relative comparative scoring, *not* a strict global percentile)
  ([mobalytics.gg/gpi](https://mobalytics.gg/gpi/), happysmurf.com).
- ✅ **"Machine learning" in GPI is unverifiable vendor marketing** — only that they assert it and
  that scoring is population/rank-relative is confirmed; the model is never published. **ML is not
  required** to deliver the value; population-relative baselining captures the substance without the
  opacity.

### Stats methodology (the most transferable lesson)
- ✅ **lolalytics measures its baseline per rank bracket instead of assuming 50%:** verbatim
  *"Average Win Rate for Emerald+ = 51.77%"* and *"Different tier brackets will have different
  average win rates so we now provide the average … we include 100% of a bracket's champions played
  and never include champions that don't belong in the bracket"*
  ([lolalytics.com](https://lolalytics.com/), >20M games/patch in Emerald+). This **measured-not-50%,
  full-inclusion** stance is the single most directly transferable methodological idea.
- ✅ **lolalytics is purely a descriptive champion-level reference** — no summoner lookup, no
  coaching ([lolalytics.com](https://lolalytics.com/), wombocombo). Complementary to op.gg/u.gg,
  not a self-coach.
- ✅ **u.gg's "Recommended Build" is an optimal-vs-popular hybrid:** *"the most frequent build that
  holds a higher win rate than the baseline win rate for your selected champion"*
  ([u.gg/faq](https://u.gg/faq), [build page](https://u.gg/lol/champions/brand/build)). Not pure
  popularity (a win-rate gate), not pure max-WR. Exposes per-item/per-rune win rates as a
  justification layer.

### Per-game / live
- ✅ **op.gg timeline OP Score is descriptive, not prescriptive:** recalculated every 5 min (SR) /
  3 min (ARAM); its 14 keywords (Tenacity, Unstoppable, …) are *"based on each graph's shape"* —
  flavor labels, not coaching
  ([op.gg help](https://help.op.gg/hc/en-us/articles/38185639004569-What-do-the-timeline-OP-Score-keywords-mean)).
- ✅ **dpm.lol's verified distinctive features are matchup videos + a Head-to-Head player
  comparison** ([esports.gg](https://esports.gg/news/league-of-legends/how-dpm-lol-is-transforming-lol-analytics/)),
  *not* the per-game DPM analytics its name implies (those went unverified — likely exist, just
  not covered).
- ✅ **Porofessor = live-lobby scouting:** current-champ WR/matches/KDA for all 10 players, smurf
  detection, loss-streak/tilt flags, autofill, counterpick/lane tips — all *before* the game
  ([Overwolf listing](https://www.overwolf.com/app/trebonius-porofessor.gg)). **Structurally
  uncopyable** by a post-game Riot-API-only tool → reference-only. (Riot's 2026 Streamer Mode can
  now disable it.)

### Riot API constraints (load-bearing for RiftReview)
- ✅ **No historical per-game LP.** league-v4 serves current standing only; *"if you want to know
  how much LP a summoner got during a game, you need to track LP before and after"*
  ([darkintaqt.com](https://darkintaqt.com/blog/league-v4-summoner),
  [developer.riotgames.com/apis](https://developer.riotgames.com/apis), HisShoes/RiotAPI). RiftReview's
  Climb snapshot-diff approach is the correct and only feasible method.
- ✅ **match-v5 timeline is the sole time-series source:** per-minute `participantFrames`
  (gold/xp/CS/position) + `events[]` (`CHAMPION_KILL`, `WARD_PLACED`, `ELITE_MONSTER_KILL`, item
  purchases, building kills)
  ([riot-watcher docs](https://riot-watcher.readthedocs.io/en/latest/riotwatcher/LeagueOfLegends/MatchApiV5.html)).
  RiftReview already pulls this; the **ward + objective events are not yet surfaced** → in-constraint
  feature surface with no new data dependency.

### Refuted (do NOT rely on — these were actively killed)
- ❌ u.gg's default build filter is **not** confirmed to be Plat+ (0-3).
- ❌ u.gg does **not** verifiably surface per-rank "you vs your rank" percentile benchmarking on
  profiles (0-3, refuted *four times*). **This corrects an earlier author claim.**
- ❌ OP Score is **not** confirmed to lack per-rank/role normalization (1-2).
- ❌ League of Graphs' "Power Circle" is **not** confirmed to be rank-relative (0-3) — its
  normalization (global vs per-rank) is genuinely unresolved.
- ❌ lolalytics significance-testing / per-stat sample display, and its matchup difficulty
  thresholds — both refuted (0-3).

---

## 2. Per-platform summary

| Platform | What it surfaces | Improvement layer | Distinctive | Confidence |
|---|---|---|---|---|
| **op.gg** | match history, OP Score 0–10 + badges, champ stats, tier lists, live game, multisearch | mostly descriptive; aggregate build/rune pages | ubiquity + OP Score | ✅ (OP Score) / ◐ (rest) |
| **Mobalytics** | GPI 8-axis, post-game strengths/weaknesses, champ tips, builds | most coaching-oriented; explicit weakness→action | GPI decomposition | ✅ |
| **u.gg** | tier lists, **build/rune optimizer**, matchups/counters | build optimization is the actionable core | optimal-vs-popular hybrid build | ✅ |
| **lolalytics** | massive-sample champ/matchup WR by rank | champion reference, not personal coaching | sample size + measured per-bracket baseline | ✅ |
| **dpm.lol** | per-game damage/gold/DPM, head-to-head, matchup videos | analytical/descriptive | matchup videos + head-to-head | ✅ (features) |
| **Porofessor** | live lobby: recent form, streaks, smurf/autofill, counterpick | pre-game prep | live lobby intel | ✅ |
| **League of Graphs** | global-percentile rankings, rare-stat framing | percentile framing (where you're unusual) | percentile-everything | ◐ / ❔ (normalization unresolved) |
| **Blitz.gg** | desktop overlay: auto-import runes/items, in-game timers, post-game takeaways | live convenience + "key takeaways" | auto-build-import + overlay | ❔ |
| **Deeplol** | op.gg-like + MMR/"AI" framing | — | MMR estimation (claimed) | ❔ |

**Cross-cutting pattern:** almost none of these show **trajectory** — they show *current* state vs a
baseline. RiftReview's trailing-window improving/declining verdict is a genuine differentiator;
"are you getting better over time" is the question every competitor answers poorly.

---

## 3. Synthesis (A–D)

**A. Table-stakes (everyone has some form):** role/rank-relative baselines for CS@min and
gold/XP diffs at fixed minutes; KDA / kill-participation / damage-share; **vision** (wards
placed/cleared, vision score); objective participation; rank/MMR/LP tracking; champion/matchup
build data. *RiftReview covers all but **vision** and **objective participation** — the clearest
in-constraint gaps (data already fetched in the timeline).* 

**B. Differentiated ideas worth copying:** (1) GPI-style named-axis decomposition with
**rank-relative** baselines — RiftReview has the decomposition, add the rank layer. (2) lolalytics'
**measured-not-50%** bracket baselining. (3) u.gg's **optimal-vs-popular hybrid** + per-element
"why" justification. (4) League-of-Graphs **percentile framing** as a complement to raw numbers.

**C. Recurring failure modes (don't copy):** single opaque composite scores (the "OP Score is
meaningless" pattern, backed by op.gg's own beta admission); descriptive-without-causal ("here's
your gold graph" with no "so fix X"); vanity percentiles with no action; generic auto-advice;
snapshot-only with no trajectory.

**D. Mapped to RiftReview → see §5 milestones.** Highest-leverage, fully in-constraint: vision +
objective metrics from timeline events already fetched; timeline causality ("where the game
turned"). The two most valuable *new* methodology ideas (rank baselining, build comparison) both
want external aggregate data — the decision is how far to take that without breaking local-only /
data-honest (resolved: hybrid seed table for baselines; own-best-build only for builds).

---

## 4. The 20 actionable items (feasibility-tagged)

**[F]** feasible in-constraint · **[C]** constrained (needs external data / caveats) · **[A]**
deliberate non-goal.

*You vs your rank, on graphs (→ M6)*
1. **[C]** Embedded **per-rank benchmark table** (CS@10, gold@15, KDA, deaths, KP, dmg share,
   vision), patch-tagged, source-labeled, owner-editable, **sparse/never-fabricate**.
2. **[F]** Rank-baseline **band** on the deep-dive CS-pace line chart.
3. **[F]** Rank-average **second series** on the Trends sparklines (requires extending the
   single-line Sparkline control — the M2 "double-series" was double-*precision*, not two lines).
4. **[F]** **Delta badge** per metric ("+1.2 CS@10 vs Gold avg"), colored by direction.
5. **[F]** **Percentile label** only where distribution data exists; else mean-delta — never a
   fabricated percentile.
6. **[F]** Comparison rank **auto-tracks current rank** (league-v4) + manual tier override.

*Measure baselines, don't assume them (→ M6)*
7. **[F]** Audit role baselines — *already measured* (`DeepDiveViewModel.BuildBaseline` averages
   own prior games); add the **rank** baseline alongside.
8. **[F→corrected]** Champion/matchup win-rates: anchor to a **measured** baseline, not a bare 50%.
   **But the player's own overall WR keeps a 50% anchor** (matchmaking equilibrium) — lolalytics'
   51.77% is a *champion* baseline, not a player one. Do not misapply.
9. **[F]** **Dual-baseline toggle:** Rank ⇄ Own-trailing (own needs zero external data — the
   rigorous fallback).

*In-constraint metric gaps (→ M7)*
10. **[F]** **Vision** metrics from `WARD_PLACED`/`WARD_KILL` timeline events (already fetched).
11. **[F]** **Objective participation** from `ELITE_MONSTER_KILL` + building events.
12. **[F]** Surface vision + objective deltas in Matchups and the deep-dive.

*Causality — the "why" (→ M8)*
13. **[F]** **"Where the game turned"** marker on the gold-diff chart (largest swing).
14. **[F]** **Game-state-at-death** annotation (gold-diff at each death — diagnostic, not just
   descriptive).
15. **[F]** **Back-timing / item-spike lag** from timeline purchase events.

*Build/rune (→ M9)*
16. **[A — dropped]** Compare your build vs an *external* "recommended" build — needs a third-party
    build-data dependency; **rejected** to protect local-only/data-honest.
17. **[F]** Surface **your own** highest-WR rune/item set per champion (no external data).

*Design discipline (→ M9)*
18. **[F]** **"Number under every verdict"** audit (counters "the score told me nothing").
19. **[A]** **Never** a single composite "RiftScore" — documented non-goal (op.gg beta admission is
    the evidence).
20. **[F]** *(optional)* per-game timeline mini-score → label intervals with actionable **deltas**,
    never op.gg-style flavor keywords.

---

## 5. Hopeful goals — milestone roadmap (M6–M9)

These are the *aspirational* product directions distilled from the research. Authoritative status
lives in `ROADMAP.md`; this is the "why."

- **M6 — You-vs-rank on graphs** (items 1–9). Rank-relative baselines everywhere a metric is shown,
  with the honest own-trailing fallback. **Decision:** HYBRID seed table + own-trailing toggle.
  *Plan ready.*
- **M7 — Vision + objective participation** (items 10–12). The biggest table-stakes gap; data
  already in the timeline. *First confirm ward/objective events are stored before scoping.*
- **M8 — Timeline causality** (items 13–15). Move from descriptive to "where/why the game turned" —
  the thing competitors fail at.
- **M9 — Own-best-build + design discipline** (items 17–20). Self-coach build surfacing (no external
  data) + the discipline guardrails. **Decision:** item 16 (external build comparison) dropped.

### Deliberate non-goals (the "do NOT copy" list — as valuable as the build list)
- A single composite vanity score ("RiftScore").
- Any **live overlay / draft / lobby scouting** (Blitz/Porofessor pattern) — architecturally
  excluded by local-only + post-game, and correctly so.
- External recommended-build / matchup-video integration — breaks the data-honest, dependency-free
  identity.

---

## 6. Open questions / future research
- **League of Graphs, Blitz.gg, Deeplol, and community sentiment remain unverified** (method
  ceiling, §0). If wanted, gather community sentiment by *direct* r/summonerschool reading, labeled
  anecdotal — not via the verification harness.
- For the M6 rank baseline: what trustworthy public per-rank, per-role benchmark source can replace
  the approximate seed numbers? (Riot exposes no percentile API; lolalytics gives *champion* WR
  baselines, not per-rank *player-stat* distributions.)
- dpm.lol's actual per-game analytics (descriptive vs prescriptive) — unverified.
