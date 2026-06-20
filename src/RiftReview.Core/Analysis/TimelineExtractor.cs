using RiftReview.Core.Riot.Dtos;

namespace RiftReview.Core.Analysis;

public static class TimelineExtractor
{
    private static int TeamOf(int pid) => pid <= 5 ? 100 : 200;

    // Nullable overload: participantId 0/null = no participant => no team.
    private static int? TeamOfNullable(int? participantId) =>
        participantId is null or <= 0 ? null : (participantId <= 5 ? 100 : 200);

    public static DeepDive BuildDeepDive(TimelineDto tl, int myParticipantId, int? opponentParticipantId)
    {
        var laneLine = new List<ChartPoint>();
        var teamLine = new List<ChartPoint>();
        var csLine = new List<ChartPoint>();
        var myTeam = TeamOf(myParticipantId);

        foreach (var f in tl.Info.Frames)
        {
            double minute = f.Timestamp / 60000.0;
            if (!f.ParticipantFrames.TryGetValue(myParticipantId.ToString(), out var mine)) continue;

            long myTeamGold = 0, enemyTeamGold = 0;
            foreach (var pf in f.ParticipantFrames.Values)
            {
                if (TeamOf(pf.ParticipantId) == myTeam) myTeamGold += pf.TotalGold;
                else enemyTeamGold += pf.TotalGold;
            }
            teamLine.Add(new ChartPoint(minute, myTeamGold - enemyTeamGold));

            if (opponentParticipantId is int oid &&
                f.ParticipantFrames.TryGetValue(oid.ToString(), out var opp))
                laneLine.Add(new ChartPoint(minute, mine.TotalGold - opp.TotalGold));

            int cs = mine.MinionsKilled + mine.JungleMinionsKilled;
            csLine.Add(new ChartPoint(minute, minute > 0 ? cs / minute : 0)); // running-average pace
        }

        var deaths = tl.Info.Frames
            .SelectMany(f => f.Events)
            .Where(e => e.Type == "CHAMPION_KILL" && e.VictimId == myParticipantId)
            .Select(e => Math.Round(e.Timestamp / 60000.0, 2))
            .OrderBy(m => m)
            .ToList();

        return new DeepDive(laneLine, teamLine, csLine, deaths, opponentParticipantId is not null && laneLine.Count > 0);
    }

    public static int? CsAtMinute(TimelineDto tl, int participantId, int minute)
    {
        var f = NearestFrame(tl, minute);
        if (f is null || !f.ParticipantFrames.TryGetValue(participantId.ToString(), out var pf)) return null;
        return pf.MinionsKilled + pf.JungleMinionsKilled;
    }

    public static int? GoldDiffAtMinute(TimelineDto tl, int myParticipantId, int? opponentParticipantId, int minute)
    {
        if (opponentParticipantId is null) return null;
        var f = NearestFrame(tl, minute);
        if (f is null || !f.ParticipantFrames.TryGetValue(myParticipantId.ToString(), out var mine)
            || !f.ParticipantFrames.TryGetValue(opponentParticipantId.Value.ToString(), out var opp)) return null;
        return mine.TotalGold - opp.TotalGold;
    }

    public static int DeathsBeforeMinute(TimelineDto tl, int myParticipantId, int minute)
    {
        long cutoff = minute * 60000L;
        return tl.Info.Frames
            .SelectMany(f => f.Events)
            .Count(e => e.Type == "CHAMPION_KILL" && e.VictimId == myParticipantId && e.Timestamp < cutoff);
    }

    private static FrameDto? NearestFrame(TimelineDto tl, int minute)
    {
        long target = minute * 60000L;
        return tl.Info.Frames.Count == 0 ? null
            : tl.Info.Frames.OrderBy(f => Math.Abs(f.Timestamp - target)).First();
    }

    public static VisionObjectivesResult BuildVisionObjectives(
        TimelineDto tl, int myParticipantId, int myTeamId)
    {
        var events = tl.Info.Frames.SelectMany(f => f.Events).ToList();

        int wardsPlaced  = events.Count(e => e.Type == "WARD_PLACED" && e.CreatorId == myParticipantId);
        int wardsCleared = events.Count(e => e.Type == "WARD_KILL"   && e.KillerId  == myParticipantId);
        int controlWards = events.Count(e => e.Type == "WARD_PLACED" && e.CreatorId == myParticipantId
                                             && e.WardType == "CONTROL_WARD");
        int visionProxy = wardsPlaced + wardsCleared + controlWards;
        var vision = new VisionStats(wardsPlaced, wardsCleared, controlWards, visionProxy);

        bool IParticipated(EventDto e) =>
            e.KillerId == myParticipantId ||
            (e.AssistingParticipantIds?.Contains(myParticipantId) ?? false);

        var monsters = events.Where(e => e.Type == "ELITE_MONSTER_KILL").ToList();
        ObjectiveParticipation Monster(string monsterType, string label)
        {
            var mine = monsters.Where(e => e.MonsterType == monsterType
                                           && (e.KillerTeamId ?? TeamOfNullable(e.KillerId)) == myTeamId).ToList();
            return new ObjectiveParticipation(label, mine.Count(IParticipated), mine.Count);
        }

        var buildings = events.Where(e => e.Type == "BUILDING_KILL").ToList();
        ObjectiveParticipation Building(string buildingType, string label)
        {
            var mine = buildings.Where(e => e.BuildingType == buildingType
                                            && e.TeamId is 100 or 200 && e.TeamId != myTeamId).ToList();
            return new ObjectiveParticipation(label, mine.Count(IParticipated), mine.Count);
        }

        var objectives = new List<ObjectiveParticipation>
        {
            Monster("DRAGON", "Dragons"),
            Monster("RIFTHERALD", "Rift Herald"),
            Monster("BARON_NASHOR", "Baron"),
            Building("TOWER_BUILDING", "Towers"),
            Building("INHIBITOR_BUILDING", "Inhibitors"),
        };
        if (monsters.Any(e => e.MonsterType == "HORDE"))
            objectives.Insert(3, Monster("HORDE", "Void Grubs"));

        return new VisionObjectivesResult(vision, objectives);
    }
}
