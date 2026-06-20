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

    // ---- M8: timeline causality (swing / death-context / back-timing) ----
    private const int  SwingWindowFrames = 3;     // ~3 min (frames are 1-min); rolling window width
    private const double SwingEpsilonGold = 1.0;  // |Δ| below this => no decisive swing
    private const long BackClusterGapMs = 10_000; // purchases > 10s apart => a separate recall

    public static CausalityResult BuildCausality(TimelineDto tl, int myParticipantId)
    {
        var team = TeamGoldDiffSeries(tl, myParticipantId);   // signed: + = my team ahead

        // Item 13: largest-magnitude rolling-window swing on the team gold-diff curve.
        SwingPoint? swing = null;
        if (team.Count >= 2)
        {
            int w = Math.Min(SwingWindowFrames, team.Count - 1);
            double bestAbs = -1;
            for (int i = 0; i + w < team.Count; i++)
            {
                double delta = team[i + w].Value - team[i].Value;
                if (Math.Abs(delta) > bestAbs)
                {
                    bestAbs = Math.Abs(delta);
                    swing = new SwingPoint(
                        team[i].Minute, team[i + w].Minute,
                        team[i].Value, team[i + w].Value,
                        delta, delta > 0);
                }
            }
            if (swing is not null && Math.Abs(swing.Delta) < SwingEpsilonGold) swing = null;
        }

        // Item 14: team gold-diff at each of my deaths.
        var deaths = tl.Info.Frames
            .SelectMany(f => f.Events)
            .Where(e => e.Type == "CHAMPION_KILL" && e.VictimId == myParticipantId)
            .Select(e => e.Timestamp / 60000.0)
            .OrderBy(m => m)
            .Select(m => new DeathContext(Math.Round(m, 2), NearestValue(team, m)))
            .ToList();

        // Item 15: recalls = clusters of my ITEM_PURCHASED events.
        var myPurchases = tl.Info.Frames
            .SelectMany(f => f.Events)
            .Where(e => e.Type == "ITEM_PURCHASED" && e.ParticipantId == myParticipantId)
            .OrderBy(e => e.Timestamp)
            .ToList();

        var backs = new List<BackEvent>();
        long clusterStart = 0, prev = 0;
        int count = 0;
        void Flush() { if (count > 0) backs.Add(new BackEvent(Math.Round(clusterStart / 60000.0, 2), count)); }
        foreach (var e in myPurchases)
        {
            if (count == 0 || e.Timestamp - prev > BackClusterGapMs) { Flush(); clusterStart = e.Timestamp; count = 0; }
            count++;
            prev = e.Timestamp;
        }
        Flush();

        // Turning-point lag: from the most recent recall at-or-before the swing start.
        double? lag = null;
        if (swing is not null && backs.Count > 0)
        {
            var preceding = backs.Where(b => b.Minute <= swing.StartMinute)
                                 .OrderByDescending(b => b.Minute)
                                 .FirstOrDefault();
            if (preceding is not null) lag = Math.Round(swing.StartMinute - preceding.Minute, 2);
        }

        return new CausalityResult(swing, deaths, backs, lag);
    }

    // Team gold-diff curve (mirrors BuildDeepDive's team logic + its skip-rule, so the swing
    // aligns with the displayed chart). Kept separate so the tested BuildDeepDive stays untouched.
    private static List<ChartPoint> TeamGoldDiffSeries(TimelineDto tl, int myParticipantId)
    {
        var line = new List<ChartPoint>();
        int myTeam = TeamOf(myParticipantId);
        foreach (var f in tl.Info.Frames)
        {
            if (!f.ParticipantFrames.ContainsKey(myParticipantId.ToString())) continue; // same guard as BuildDeepDive
            long myTeamGold = 0, enemyTeamGold = 0;
            foreach (var pf in f.ParticipantFrames.Values)
            {
                if (TeamOf(pf.ParticipantId) == myTeam) myTeamGold += pf.TotalGold;
                else enemyTeamGold += pf.TotalGold;
            }
            line.Add(new ChartPoint(f.Timestamp / 60000.0, myTeamGold - enemyTeamGold));
        }
        return line;
    }

    private static double NearestValue(List<ChartPoint> series, double minute)
    {
        if (series.Count == 0) return 0;
        var best = series[0];
        double bestDist = Math.Abs(best.Minute - minute);
        foreach (var p in series)
        {
            double d = Math.Abs(p.Minute - minute);
            if (d < bestDist) { bestDist = d; best = p; }
        }
        return best.Value;
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
