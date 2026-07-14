# RiftReview — ROADMAP archive (shipped milestones)

Shipped-milestone rows archived from `ROADMAP.md` per the roadmap archive policy (2026-07-14).
Rows M1–M7 were **moved** here; rows M8–M10 remain in `ROADMAP.md` (three most recently merged)
and are **copied** here in full. All rows are verbatim from the ROADMAP's condensed
shipped-history table. The verbose per-milestone decision-log prose (gotchas, exact math,
screenshot-harness detail) lives in `docs/roadmap-archive-2026-07.md` (moved there 2026-07-07).

| # | Title | PR | What shipped |
|---|-------|----|----|
| 1 | Review shell + nav | — | NavigationView shell; per-match deep-dive (gold-diff + CS-pace-vs-baseline + death markers) |
| 2 | Trends | — | per-champ trailing-window trajectory across 8 metrics + verdicts; Sparkline (double-precision) |
| 3 | Matchups | — | per-champ vs each enemy-laner aggregates; drill into deep-dive |
| 4 | Session Health | — | sessions + loss-streak/decay tilt guard; cross-page banner (singleton VM) |
| 5 | Climb | — | current rank + ranked streaks + net-LP-per-snapshot segments; RankLadder |
| 6 | You-vs-rank on graphs | #1 | rank baseline on Trends sparklines + deep-dive CS-pace chart; sparse never-fabricate seed table. Suite 123. |
| 7 | Vision + objectives | #2 | deep-dive Vision & objectives: exact wards + labeled vision proxy + objective participation; on-demand from timeline blob (no migration). Suite 126. |
| 8 | Timeline causality | #3 | gold-diff swing marker + death context + back-timing cadence + turning-point lag; EventDto +2 fields. Suite 133. |
| 9 | Build analysis + discipline | #4 | own-best-build per champ (Data Dragon item.json for names only) + number-under-every-verdict audit + no-composite-score non-goal enshrined. Suite 155. |
| 10 | By game phase | #5 | deep-dive "By game phase" card (Early/Mid/Late × gold/CS/deaths/KP, own-role-baseline deltas). Suite 166. **M6–M10 competitive-research expansion COMPLETE.** |
