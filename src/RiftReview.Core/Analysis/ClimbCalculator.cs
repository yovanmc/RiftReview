using System;
using System.Collections.Generic;
using System.Linq;
using RiftReview.Core.Data;

namespace RiftReview.Core.Analysis;

// Pure: ranked momentum (streaks) from matches + LP segments by pairing snapshot deltas
// with the ranked games in each time window. No DB, no I/O, deterministic.
public static class ClimbCalculator
{
    public static StreakSummary Streaks(IReadOnlyList<MatchRow> rankedMatches, int recentFormCount = 10)
    {
        var ordered = rankedMatches.OrderBy(m => m.GameStartUtc).ToList();
        if (ordered.Count == 0) return new StreakSummary(0, 0, 0, Array.Empty<bool>());

        int longestW = 0, longestL = 0, runW = 0, runL = 0;
        foreach (var m in ordered)
        {
            if (m.Win) { runW++; runL = 0; longestW = Math.Max(longestW, runW); }
            else       { runL++; runW = 0; longestL = Math.Max(longestL, runL); }
        }

        bool lastWin = ordered[^1].Win;
        int run = 0;
        for (int i = ordered.Count - 1; i >= 0 && ordered[i].Win == lastWin; i--) run++;
        int current = lastWin ? run : -run;

        var form = ordered.Skip(Math.Max(0, ordered.Count - recentFormCount)).Select(m => m.Win).ToList();
        return new StreakSummary(current, longestW, longestL, form);
    }

    public static IReadOnlyList<LpSegment> Segments(
        IReadOnlyList<LpSnapshot> snapshots, IReadOnlyList<MatchRow> rankedMatches, string queueType)
    {
        int qid = QueueIdFor(queueType);
        var snaps = snapshots.Where(s => s.QueueType == queueType).OrderBy(s => s.TakenUtc).ToList();
        var segs = new List<LpSegment>();
        for (int i = 1; i < snaps.Count; i++)
        {
            var p = snaps[i - 1]; var c = snaps[i];
            int fp = RankLadder.ToPoints(p.Tier, p.Division, p.LeaguePoints);
            int cp = RankLadder.ToPoints(c.Tier, c.Division, c.LeaguePoints);
            int games = rankedMatches.Count(m => m.QueueId == qid
                && m.GameStartUtc > p.TakenUtc && m.GameStartUtc <= c.TakenUtc);
            segs.Add(new LpSegment(queueType, p.TakenUtc, c.TakenUtc, fp, cp, cp - fp, games,
                RankLadder.Format(p.Tier, p.Division, p.LeaguePoints),
                RankLadder.Format(c.Tier, c.Division, c.LeaguePoints)));
        }
        segs.Reverse();
        return segs;
    }

    public static int QueueIdFor(string queueType) => queueType switch
    {
        "RANKED_SOLO_5x5" => 420,
        "RANKED_FLEX_SR" => 440,
        _ => -1,
    };
}
