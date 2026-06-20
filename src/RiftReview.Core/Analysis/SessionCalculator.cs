using System;
using System.Collections.Generic;
using System.Linq;
using RiftReview.Core.Data;

namespace RiftReview.Core.Analysis;

// Pure: group a player's games into time-gapped sessions and score each for tilt.
// No DB, no I/O, deterministic. Reads only MatchRow fields (null-guards the derived scalars).
public static class SessionCalculator
{
    public const int    SessionGapSeconds     = 3 * 3600;
    public const int    MinGamesForDecay       = 3;
    public const double DeathsDecayThreshold   = 1.5;
    public const int    Cs10DecayThreshold     = 8;
    public const double KdaDecayThreshold       = 1.5;
    public const int    TiltedEndStreak         = 3;
    public const int    LongStreakCaution       = 3;
    public const double CautionWinRate          = 0.40;
    public const int    CautionWinRateMinGames  = 4;

    public static IReadOnlyList<PlaySession> BuildSessions(IReadOnlyList<MatchRow> matches)
    {
        if (matches.Count == 0) return Array.Empty<PlaySession>();
        var ordered = matches.OrderBy(m => m.GameStartUtc).ToList();
        var sessions = new List<PlaySession>();
        var current = new List<MatchRow> { ordered[0] };
        for (int i = 1; i < ordered.Count; i++)
        {
            var prev = ordered[i - 1];
            long gap = ordered[i].GameStartUtc - (prev.GameStartUtc + prev.DurationS);
            if (gap <= SessionGapSeconds) current.Add(ordered[i]);
            else { sessions.Add(Build(current)); current = new List<MatchRow> { ordered[i] }; }
        }
        sessions.Add(Build(current));
        sessions.Reverse();
        return sessions;
    }

    private static PlaySession Build(List<MatchRow> games)
    {
        int n = games.Count;
        int wins = games.Count(g => g.Win);
        int losses = n - wins;

        int longest = 0, run = 0;
        foreach (var g in games) { if (!g.Win) { run++; longest = Math.Max(longest, run); } else run = 0; }
        int endLoss = 0; for (int i = n - 1; i >= 0 && !games[i].Win; i--) endLoss++;
        int endWin  = 0; for (int i = n - 1; i >= 0 &&  games[i].Win; i--) endWin++;

        double? deathsDelta = null, cs10Delta = null, kdaDelta = null;
        if (n >= MinGamesForDecay)
        {
            int half = n / 2;
            var first  = games.Take(half).ToList();
            var second = games.Skip(n - half).ToList();
            deathsDelta = Avg(second, g => g.Deaths) - Avg(first, g => g.Deaths);
            kdaDelta    = AvgKda(first) - AvgKda(second);
            double? cF = AvgN(first, g => g.CsAt10), cS = AvgN(second, g => g.CsAt10);
            cs10Delta   = (cF.HasValue && cS.HasValue) ? cF - cS : null;
        }

        bool deathsDecay = deathsDelta is double dd && dd >= DeathsDecayThreshold;
        bool csDecay     = cs10Delta  is double cd && cd >= Cs10DecayThreshold;
        bool kdaDecay    = kdaDelta    is double kd && kd >= KdaDecayThreshold;
        bool decay = deathsDecay || csDecay || kdaDecay;

        double wr = wins / (double)n;
        TiltSeverity sev =
            (endLoss >= TiltedEndStreak || (endLoss >= 2 && decay)) ? TiltSeverity.Tilted
          : (endLoss == 2 || longest >= LongStreakCaution || decay
             || (n >= CautionWinRateMinGames && wr < CautionWinRate)) ? TiltSeverity.Caution
          : TiltSeverity.Calm;

        var reasons = new List<string>();
        if (endLoss >= 2) reasons.Add($"{endLoss}-loss skid to close");
        else if (longest >= LongStreakCaution) reasons.Add($"{longest}-loss skid earlier");
        if (deathsDecay) reasons.Add("deaths climbing");
        if (csDecay)     reasons.Add("CS@10 falling");
        if (kdaDecay)    reasons.Add("KDA falling");
        if (n >= CautionWinRateMinGames && wr < CautionWinRate) reasons.Add($"{wins}/{n} in this session");
        if (reasons.Count == 0) reasons.Add(endWin >= 3 ? $"{endWin}-win heater" : "no tilt signals");

        return new PlaySession(
            StartUtc: games[0].GameStartUtc,
            EndUtc: games[^1].GameStartUtc + games[^1].DurationS,
            Games: n, Wins: wins, Losses: losses,
            LongestLossStreak: longest, EndLossStreak: endLoss, EndWinStreak: endWin,
            DeathsDelta: deathsDelta, Cs10Delta: cs10Delta, KdaDelta: kdaDelta,
            DecayPresent: decay, Severity: sev, Reasons: reasons, GamesList: games);
    }

    private static double Avg(IEnumerable<MatchRow> gs, Func<MatchRow, int> sel) => gs.Average(sel);
    private static double AvgKda(IEnumerable<MatchRow> gs) =>
        gs.Average(g => (g.Kills + g.Assists) / (double)Math.Max(1, g.Deaths));
    private static double? AvgN(IReadOnlyList<MatchRow> gs, Func<MatchRow, int?> sel)
    {
        var v = gs.Where(g => sel(g).HasValue).Select(g => (double)sel(g)!.Value).ToList();
        return v.Count == 0 ? (double?)null : v.Average();
    }
}
