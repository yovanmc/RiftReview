using RiftReview.Core.Riot.Dtos;

namespace RiftReview.Core.Analysis;

public static class MatchExtractor
{
    public static MatchSummary Summarize(MatchDto match, string myPuuid)
    {
        var me = match.Info.Participants.FirstOrDefault(p => p.Puuid == myPuuid)
            ?? throw new InvalidOperationException($"puuid {myPuuid} not in match {match.Metadata.MatchId}");
        var opp = match.Info.Participants.FirstOrDefault(
            p => p.TeamId != me.TeamId && p.TeamPosition == me.TeamPosition && !string.IsNullOrEmpty(me.TeamPosition));

        var durationS = NormalizeDuration(match.Info.GameDuration);
        return new MatchSummary(
            (int)match.Info.QueueId, match.Info.GameCreation / 1000, durationS,
            PatchFromVersion(match.Info.GameVersion), me.ChampionId, me.TeamPosition, me.Win,
            me.Kills, me.Deaths, me.Assists, me.TotalMinionsKilled + me.NeutralMinionsKilled,
            me.ParticipantId, opp?.ParticipantId, opp?.ChampionId);
    }

    // gameDuration is seconds on modern patches; guard against legacy ms (>100000 => ms).
    private static int NormalizeDuration(long gd) => (int)(gd > 100_000 ? gd / 1000 : gd);

    private static string PatchFromVersion(string gameVersion)
    { var parts = gameVersion.Split('.'); return parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : gameVersion; }
}
