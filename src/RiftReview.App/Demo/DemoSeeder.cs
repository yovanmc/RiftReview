using System.Text.Json;
using RiftReview.Core.Analysis;
using RiftReview.Core.Data;
using RiftReview.Core.Riot.Dtos;

namespace RiftReview.App.Demo;

// Loads clearly-synthetic match data into the demo DB so the UI renders without a live Riot key.
// Never used outside --seed-demo. Each game stores full match+timeline JSON so the deep-dive
// derives real curves the same way a live sync would.
public static class DemoSeeder
{
    private static readonly JsonSerializerOptions Json = new();
    // Real champion IDs: Ahri=103, Yasuo=157, LeBlanc=7, Zed=238, Syndra=134, Orianna=61, Lux=99, Zoe=142
    private static readonly int[] MidChamps = { 103, 157, 7, 238, 134, 61, 99, 142 };

    public static void Seed(RiftReviewDb db)
    {
        db.SetMeta("puuid", "ME");
        db.SetMeta("riot_id", "DemoSummoner#NA1");
        const long baseCreation = 1_700_000_000_000L;
        // Concentrated demo pool: champ 103 (x12), champ 157 (x8), then 4 one-offs — mirrors a two-champ grind.
        int[] plan =
        {
            103,103,103,103,103,103,103,103,103,103,103,103,
            157,157,157,157,157,157,157,157,
            7, 238, 99, 142
        };
        for (int i = 0; i < plan.Length; i++)
        {
            var (match, tl) = BuildGame(i, baseCreation - i * 86_400_000L, plan[i]);
            var s = MatchExtractor.Summarize(match, "ME");
            var cs10 = TimelineExtractor.CsAtMinute(tl, s.MyParticipantId, 10);
            var g15 = TimelineExtractor.GoldDiffAtMinute(tl, s.MyParticipantId, s.OpponentParticipantId, 15);
            var row = new MatchRow(match.Metadata.MatchId, s.QueueId, s.GameStartUtc, s.DurationS, s.Patch,
                s.MyChampionId, s.MyTeamPosition, s.Win, s.Kills, s.Deaths, s.Assists, s.Cs,
                cs10, g15, s.OpponentParticipantId, s.OpponentChampionId, s.GameStartUtc);
            db.UpsertMatch(row, JsonSerializer.Serialize(match, Json), JsonSerializer.Serialize(tl, Json));
        }
    }

    private static (MatchDto, TimelineDto) BuildGame(int i, long gameCreation, int myChamp)
    {
        bool win = (i % 3) != 0;                         // mix of wins/losses: 0=loss,1=win,2=win,3=loss,...
        int oppChamp = MidChamps[(i + 3) % MidChamps.Length];
        int durationS = 1500 + (i % 5) * 150;
        int frames = durationS / 60 + 1;
        int deathCount = 1 + (i % 3);
        int kills = 3 + (i * 2) % 9, assists = 4 + (i * 5) % 11;
        int sign = win ? 1 : -1;
        int laneSlope = 30 + i * 12;
        var pos = new[] { "TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY" };

        // Participant 3 (pid=3) = "ME", position MIDDLE (index 2 in pos array for pid 1..5)
        // Opponent is participant 8 (pid=8), same position MIDDLE in enemy team (pid 6..10, index 2 = pid 8)
        var parts = new List<ParticipantDto>();
        for (int pid = 1; pid <= 10; pid++)
        {
            int team = pid <= 5 ? 100 : 200;
            string position = pos[(pid - 1) % 5];
            string puuid = pid == 3 ? "ME" : $"P{pid}";
            int champ = pid == 3 ? myChamp : (pid == 8 ? oppChamp : 100 + pid);
            bool w = team == 100 ? win : !win;
            int k = pid == 3 ? kills : 2;
            int d = pid == 3 ? deathCount : 3;
            int a = pid == 3 ? assists : 4;
            // TotalMinionsKilled + NeutralMinionsKilled together form CS
            // For "ME": ~7 cs/min * frames minutes, split as mostly lane cs
            int totalMinions = pid == 3 ? (6 * frames) : 80;
            int neutralMinions = pid == 3 ? (frames) : 10;   // ~1/min jungle bonus
            parts.Add(new ParticipantDto(puuid, pid, champ, team, position, w, k, d, a, totalMinions, neutralMinions));
        }
        var match = new MatchDto(
            new MatchMetadata($"NA1_DEMO_{i}", parts.Select(p => p.Puuid).ToList()),
            new MatchInfo(i == 7 ? 400 : 420, gameCreation, durationS, "15.12.1", parts));

        var frameList = new List<FrameDto>();
        for (int f = 0; f < frames; f++)
        {
            var pf = new Dictionary<string, ParticipantFrameDto>();
            for (int pid = 1; pid <= 10; pid++)
            {
                // "ME" (pid=3) accumulates ~7 cs/min in minions + some jungle
                int mk = pid == 3 ? (int)((6.0 + (i % 3) * 0.4) * f) : 5 * f;
                int jk = pid == 3 ? f : 0;
                // Gold: base + per-frame accumulation; opponent (pid=8) varies by sign/laneSlope for gold diff curves
                int gold = pid == 3 ? 500 + (480 + i * 10) * f
                         : pid == 8 ? 500 + (480 + i * 10) * f - sign * laneSlope * f
                         : 500 + 100 * pid + 300 * f;
                pf[pid.ToString()] = new ParticipantFrameDto(pid, gold, mk, jk);
            }
            // Events list is mutable (List<EventDto>) — death events added below
            frameList.Add(new FrameDto(60000L * f, pf, new List<EventDto>()));
        }
        // Inject death events: killer=8 (opponent mid), victim=3 (me)
        for (int d = 0; d < deathCount; d++)
        {
            int fm = 6 + d * 5;
            if (fm < frames)
                frameList[fm].Events.Add(new EventDto("CHAMPION_KILL", 60000L * fm, 8, 3));
        }
        var tl = new TimelineDto(
            new TimelineMetadata($"NA1_DEMO_{i}", parts.Select(p => p.Puuid).ToList()),
            new TimelineInfo(60000, frameList));
        return (match, tl);
    }
}
