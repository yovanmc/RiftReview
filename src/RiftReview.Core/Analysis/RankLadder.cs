using System;

namespace RiftReview.Core.Analysis;

// Maps a (tier, division, lp) rank to absolute "ladder points" for diffing, and to a display string.
// Tiers stack at 400 pts each (4 divisions × 100); apex tiers are one continuous pool above Diamond I.
public static class RankLadder
{
    private static readonly string[] Tiers =
        { "IRON", "BRONZE", "SILVER", "GOLD", "PLATINUM", "EMERALD", "DIAMOND" };
    private static readonly string[] Divisions = { "IV", "III", "II", "I" };

    public static int ToPoints(string tier, string division, int lp)
    {
        string t = (tier ?? "").ToUpperInvariant();
        int ti = Array.IndexOf(Tiers, t);
        if (ti >= 0)
        {
            int di = Array.IndexOf(Divisions, (division ?? "").ToUpperInvariant());
            if (di < 0) di = 0;
            return ti * 400 + di * 100 + lp;
        }
        return Tiers.Length * 400 + lp;   // apex (MASTER/GRANDMASTER/CHALLENGER): 2800 + lp
    }

    public static bool IsApex(string tier)
    {
        var t = (tier ?? "").ToUpperInvariant();
        return t is "MASTER" or "GRANDMASTER" or "CHALLENGER";
    }

    public static string Format(string tier, string division, int lp) =>
        IsApex(tier) ? $"{Cap(tier)} {lp} LP" : $"{Cap(tier)} {division} · {lp} LP";

    public static string Cap(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..].ToLowerInvariant();
}
