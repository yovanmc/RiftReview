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
        // 24 Ahri games (≥2N for trends eligibility), 8 Yasuo, 4 one-offs.
        int[] plan =
        {
            103,103,103,103,103,103,103,103,103,103,103,103,
            103,103,103,103,103,103,103,103,103,103,103,103,
            157,157,157,157,157,157,157,157, 7, 238, 99, 142
        };

        // Varied lane opponents for Ahri (i=0..23) so the Matchups page renders richly.
        // 4 distinct opponents with different game counts and win rates:
        //   vs 238 (Zed):     i=0..7   → 2W/6L (25%) — unfavorable, renders red
        //   vs 134 (Syndra):  i=8..14  → 5W/2L (71%) — favorable, renders green
        //   vs 61  (Orianna): i=15..20 → 3W/3L (50%) — neutral
        //   vs 99  (Lux):     i=21..23 → 1W/2L (33%) — thin sample, muted
        int[] ahriOppPlan = { 238,238,238,238,238,238,238,238,
                               134,134,134,134,134,134,134,
                                61, 61, 61, 61, 61, 61,
                                99, 99, 99 };
        bool[] ahriWinPlan = { false,false,true ,false,false,true ,false,false,  // vs Zed:    2W 6L
                                true ,true ,false,true ,true ,true ,false,        // vs Syndra: 5W 2L
                                true ,false,true ,false,true ,false,             // vs Ori:    3W 3L
                                false,true ,false };                              // vs Lux:    1W 2L

        for (int i = 0; i < plan.Length; i++)
        {
            int?  overrideOpp = i < 24 ? ahriOppPlan[i] : null;
            bool? overrideWin = i < 24 ? ahriWinPlan[i] : null;
            var (match, tl) = BuildGame(i, baseCreation - i * 86_400_000L, plan[i], overrideOpp, overrideWin);
            var s = MatchExtractor.Summarize(match, "ME");
            var cs10 = TimelineExtractor.CsAtMinute(tl, s.MyParticipantId, 10);
            var g15 = TimelineExtractor.GoldDiffAtMinute(tl, s.MyParticipantId, s.OpponentParticipantId, 15);
            var row = new MatchRow(match.Metadata.MatchId, s.QueueId, s.GameStartUtc, s.DurationS, s.Patch,
                s.MyChampionId, s.MyTeamPosition, s.Win, s.Kills, s.Deaths, s.Assists, s.Cs,
                cs10, g15, s.OpponentParticipantId, s.OpponentChampionId, s.GameStartUtc,
                s.KillParticipation, s.DamageShare);
            db.UpsertMatch(row, JsonSerializer.Serialize(match, Json), JsonSerializer.Serialize(tl, Json));
        }

        // LP snapshots for the Climb page — a Gold IV → Gold I Solo climb across the demo timeline,
        // taken_utc values interleaved among the demo ranked games so each segment has games in-window.
        //
        // Demo Solo (queue-420) game_start_utc span:
        //   Oldest: i=35 → 1_696_976_000 s  (1_700_000_000 − 35*86400)
        //   Newest: tilted t=4 → 1_700_031_200 s
        //   Note: i=7 is queue-400 (NORMAL), so 35 Solo main games + 5 tilted = 40 Solo games total.
        //
        // 4 Solo snapshots → 3 segments:
        //   Snap1 = 1_696_975_000  (just before oldest Solo game at 1_696_976_000)
        //   Snap2 = 1_697_840_000  (= 1_700_000_000 − 25*86400; game i=25 exactly at boundary, captured by <=)
        //   Snap3 = 1_698_840_000
        //   Snap4 = 1_700_031_201  (just after newest tilted game at 1_700_031_200)
        //
        // Segment 1 (Snap1..Snap2]: games k=25..35 (all Solo) → 11 games, Gold IV 30 → Gold III 60, NetLp=+130
        // Segment 2 (Snap2..Snap3]: games k=14..24 (all Solo) → 11 games, Gold III 60 → Gold II 12, NetLp=+52
        // Segment 3 (Snap3..Snap4]: games k=0..13 excl k=7 (13) + 5 tilted → 18 games, Gold II 12 → Gold I 75, NetLp=+263
        db.InsertLpSnapshot(new LpSnapshot(1_696_975_000L, "RANKED_SOLO_5x5", "GOLD", "IV",   30, 60, 55));
        db.InsertLpSnapshot(new LpSnapshot(1_697_840_000L, "RANKED_SOLO_5x5", "GOLD", "III",  60, 66, 58));
        db.InsertLpSnapshot(new LpSnapshot(1_698_840_000L, "RANKED_SOLO_5x5", "GOLD", "II",   12, 72, 61));
        db.InsertLpSnapshot(new LpSnapshot(1_700_031_201L, "RANKED_SOLO_5x5", "GOLD", "I",    75, 79, 64));
        db.InsertLpSnapshot(new LpSnapshot(1_697_840_000L, "RANKED_FLEX_SR",  "SILVER", "I",  40, 20, 15));
        db.InsertLpSnapshot(new LpSnapshot(1_700_031_201L, "RANKED_FLEX_SR",  "GOLD", "IV",    5, 24, 18));

        // Tilted latest session: 5 Ahri games clustered ~40 min apart, starting 6 h after the
        // existing newest game (i=0 at baseCreation/1000 seconds).  The 6 h gap >> SessionGapSeconds
        // (3 h), so SessionCalculator treats these as a NEW session separate from all existing games.
        // Per-game targets: 4-loss skid to close (EndLossStreak=4 ≥ TiltedEndStreak=3) → Tilted.
        //   game index within tilted session (0=oldest,4=newest): win, deaths, cs@10 target, csRate
        //   0: Win,  deaths=2,  cs@10=80  csRate=7.0
        //   1: Loss, deaths=4,  cs@10=76  csRate=6.6
        //   2: Loss, deaths=6,  cs@10=70  csRate=6.0
        //   3: Loss, deaths=8,  cs@10=64  csRate=5.4
        //   4: Loss, deaths=10, cs@10=58  csRate=4.8
        // CsAtMinute(tl, pid, 10) reads frame f=10 (timestamp 600_000 ms): mk=(int)(csRate*10), jk=10.
        // So cs@10 = (int)(csRate*10)+10 which hits targets exactly.
        // Session gap check (within session): each start is 2400 s apart; durationS is ~1500 s, so
        // inter-game gap ~900 s << 10800 s → all 5 form one session.
        const long tiltedBaseSec = 1_700_000_000L + 6 * 3600L; // first (oldest) tilted game in UTC seconds
        const long tiltedBaseMs  = tiltedBaseSec * 1000L;       // same as milliseconds for gameCreation
        bool[]   tiltedWins    = { true,  false, false, false, false };
        int[]    tiltedDeaths  = { 2,     4,     6,     8,     10    };
        double[] tiltedCsRates = { 7.0,   6.6,   6.0,   5.4,   4.8  };
        for (int t = 0; t < 5; t++)
        {
            // gameCreation passed in ms; each game 40 min = 2400 s = 2_400_000 ms after the previous.
            long gameCreationMs = tiltedBaseMs + t * 2_400_000L;
            var (match, tl) = BuildGame(
                40 + t, gameCreationMs, 103,
                overrideOppChamp: 238,
                overrideWin:      tiltedWins[t],
                overrideDeaths:   tiltedDeaths[t],
                overrideCsRate:   tiltedCsRates[t],
                overrideMatchId:  $"NA1_TILTED_{t}");
            var s = MatchExtractor.Summarize(match, "ME");
            var cs10 = TimelineExtractor.CsAtMinute(tl, s.MyParticipantId, 10);
            var g15 = TimelineExtractor.GoldDiffAtMinute(tl, s.MyParticipantId, s.OpponentParticipantId, 15);
            var row = new MatchRow(match.Metadata.MatchId, s.QueueId, s.GameStartUtc, s.DurationS, s.Patch,
                s.MyChampionId, s.MyTeamPosition, s.Win, s.Kills, s.Deaths, s.Assists, s.Cs,
                cs10, g15, s.OpponentParticipantId, s.OpponentChampionId, s.GameStartUtc,
                s.KillParticipation, s.DamageShare);
            db.UpsertMatch(row, JsonSerializer.Serialize(match, Json), JsonSerializer.Serialize(tl, Json));
        }
    }

    private static (MatchDto, TimelineDto) BuildGame(int i, long gameCreation, int myChamp,
        int? overrideOppChamp = null, bool? overrideWin = null,
        int? overrideDeaths = null, double? overrideCsRate = null, string? overrideMatchId = null)
    {
        bool win = overrideWin ?? (i % 3) != 0;          // mix of wins/losses: 0=loss,1=win,2=win,3=loss,...
        int oppChamp = overrideOppChamp ?? MidChamps[(i + 3) % MidChamps.Length];
        int durationS = 1500 + (i % 5) * 150;
        int frames = durationS / 60 + 1;
        int deathCount = overrideDeaths ?? (1 + (i % 3));
        int kills = 3 + (i * 2) % 9, assists = 4 + (i * 5) % 11;
        int sign = win ? 1 : -1;
        int laneSlope = 30 + i * 12;
        double csRate = overrideCsRate ?? (7.0 - i * 0.1); // cs per frame for pid=3; cs@10 = (int)(csRate*10)+10
        string matchId = overrideMatchId ?? $"NA1_DEMO_{i}";
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
            // For "ME": csRate drives CS@10 = (int)(csRate*10)+10; default = 7.0 - i*0.1.
            //   i=0(newest)→rate=7.0→cs@10=80, i=23(oldest)→rate=4.7→cs@10=57; delta≈10 >> 2.0 floor → "Improving".
            int totalMinions = pid == 3 ? (int)(csRate * frames) : 80;
            int neutralMinions = pid == 3 ? (frames) : 10;   // ~1/min jungle bonus
            // Damage: lower i = newer game; ME scales up so damage share trends.
            //   i=0 (newest) → 9000+3450=12450, i=23 (oldest) → 9000+0=9000.
            int dmg = pid == 3 ? (9000 + (23 - i) * 150)
                    : pid <= 5 ? 8000                        // ally
                    : 7500;                                  // enemy
            parts.Add(new ParticipantDto(puuid, pid, champ, team, position, w, k, d, a, totalMinions, neutralMinions, dmg));
        }
        var match = new MatchDto(
            new MatchMetadata(matchId, parts.Select(p => p.Puuid).ToList()),
            new MatchInfo(i == 7 ? 400 : 420, gameCreation, durationS, "15.12.1", parts));

        var frameList = new List<FrameDto>();
        for (int f = 0; f < frames; f++)
        {
            var pf = new Dictionary<string, ParticipantFrameDto>();
            for (int pid = 1; pid <= 10; pid++)
            {
                // "ME" (pid=3): csRate drives MinionsKilled per frame; cs@10 = (int)(csRate*10)+jk@f10.
                // Seeding is newest-first (gameCreation = base - i*day), so lower i = newer game.
                int mk = pid == 3 ? (int)(csRate * f) : 5 * f;
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

        // M7: vision + objective events (attributed to ME = pid 3, team 100) so the deep-dive
        // Vision & objectives section renders with realistic demo data. Minutes <= 24 so they
        // survive shorter demo games; the guard drops any that exceed this game's length.
        void AddEv(int minute, EventDto ev) { if (minute < frames) frameList[minute].Events.Add(ev); }

        foreach (var m in new[] { 2, 5, 9, 14, 20 })
            AddEv(m, new EventDto("WARD_PLACED", 60000L * m, null, null,
                CreatorId: 3, WardType: (m == 9 || m == 14) ? "CONTROL_WARD" : "YELLOW_TRINKET"));
        foreach (var m in new[] { 7, 16, 24 })
            AddEv(m, new EventDto("WARD_KILL", 60000L * m, KillerId: 3, VictimId: null, WardType: "SIGHT_WARD"));

        AddEv(8,  new EventDto("ELITE_MONSTER_KILL", 60000L * 8,  KillerId: 3, VictimId: null,
            KillerTeamId: 100, MonsterType: "DRAGON", MonsterSubType: "FIRE_DRAGON",
            AssistingParticipantIds: new List<int> { 1, 2 }));
        AddEv(19, new EventDto("ELITE_MONSTER_KILL", 60000L * 19, KillerId: 2, VictimId: null,
            KillerTeamId: 100, MonsterType: "DRAGON", MonsterSubType: "WATER_DRAGON",
            AssistingParticipantIds: new List<int> { 3, 4 }));
        AddEv(11, new EventDto("ELITE_MONSTER_KILL", 60000L * 11, KillerId: 1, VictimId: null,
            KillerTeamId: 100, MonsterType: "RIFTHERALD", AssistingParticipantIds: new List<int> { 3 }));

        AddEv(13, new EventDto("BUILDING_KILL", 60000L * 13, KillerId: 3, VictimId: null,
            TeamId: 200, BuildingType: "TOWER_BUILDING", TowerType: "OUTER_TURRET", LaneType: "MID_LANE"));
        AddEv(17, new EventDto("BUILDING_KILL", 60000L * 17, KillerId: 6, VictimId: null,
            TeamId: 200, BuildingType: "TOWER_BUILDING", TowerType: "OUTER_TURRET", LaneType: "TOP_LANE"));
        AddEv(22, new EventDto("BUILDING_KILL", 60000L * 22, KillerId: 3, VictimId: null,
            TeamId: 200, BuildingType: "TOWER_BUILDING", TowerType: "INNER_TURRET", LaneType: "MID_LANE",
            AssistingParticipantIds: new List<int> { 2 }));
        AddEv(15, new EventDto("BUILDING_KILL", 60000L * 15, KillerId: 8, VictimId: null,
            TeamId: 100, BuildingType: "TOWER_BUILDING", TowerType: "OUTER_TURRET", LaneType: "MID_LANE"));

        var tl = new TimelineDto(
            new TimelineMetadata(matchId, parts.Select(p => p.Puuid).ToList()),
            new TimelineInfo(60000, frameList));
        return (match, tl);
    }
}
