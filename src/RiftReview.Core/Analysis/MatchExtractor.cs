using RiftReview.Core.Riot.Dtos;

namespace RiftReview.Core.Analysis;

public static class MatchExtractor
{
    public static MatchSummary Summarize(MatchDto match, string myPuuid)
    {
        var me = match.Info.Participants.FirstOrDefault(p => p.Puuid == myPuuid)
            ?? throw new InvalidOperationException($"puuid {myPuuid} not in match {match.Metadata.MatchId}");
        // No clean lane opponent when position is unknown (e.g. ARAM): teamPosition is "" for all.
        var opp = string.IsNullOrEmpty(me.TeamPosition)
            ? null
            : match.Info.Participants.FirstOrDefault(
                p => p.TeamId != me.TeamId && p.TeamPosition == me.TeamPosition);

        var myTeam = match.Info.Participants.Where(p => p.TeamId == me.TeamId).ToList();
        int teamKills = myTeam.Sum(p => p.Kills);
        long teamDmg = myTeam.Sum(p => (long)p.TotalDamageDealtToChampions);
        double kp = teamKills <= 0 ? 0 : (me.Kills + me.Assists) / (double)teamKills;
        double dmgShare = teamDmg <= 0 ? 0 : me.TotalDamageDealtToChampions / (double)teamDmg;

        var durationS = NormalizeDuration(match.Info.GameDuration);
        return new MatchSummary(
            (int)match.Info.QueueId, match.Info.GameCreation / 1000, durationS,
            PatchFromVersion(match.Info.GameVersion), me.ChampionId, me.TeamPosition, me.Win,
            me.Kills, me.Deaths, me.Assists, me.TotalMinionsKilled + me.NeutralMinionsKilled,
            me.ParticipantId, opp?.ParticipantId, opp?.ChampionId,
            kp, dmgShare);
    }

    // gameDuration is seconds on modern patches; guard against legacy ms (>100000 => ms).
    private static int NormalizeDuration(long gd) => (int)(gd > 100_000 ? gd / 1000 : gd);

    private static string PatchFromVersion(string gameVersion)
    { var parts = gameVersion.Split('.'); return parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : gameVersion; }
}
